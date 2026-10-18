import { computed, inject, Injectable, signal } from '@angular/core';
import { Api, ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { SettingChanges, SettingLevel, SettingState, SettingsView } from '../core/models';
import { readPreference, writePreference } from '../core/preferences';
import { Rights, SETTINGS_RIGHTS } from '../core/rights';
import { ThemeService } from '../core/theme.service';
import { ToastService } from '../core/toast.service';
import { SearchHistory } from '../library/search-history.service';
import { SidebarService } from '../library/sidebar/sidebar.service';
import { getValue, SettingField, SettingGroup, SIMPLE_SETTINGS, TABS } from './settings-schema';

const SHOW_ALL_KEY = 'sfe.settings.showAll';
const LEVEL_KEY = 'sfe.settings.level';

export type ControlValue = string | boolean;

/** What a save came to: `invalid` = errors are shown by their fields. */
export type SaveOutcome = 'saved' | 'invalid' | 'failed';

/**
 * The changes not saved yet of a level: the controls changed, the locks ticked or unticked, the preset choices set
 * to "Custom…". Kept for a level while another one is shown, then put back over its settings, loaded again.
 */
interface LevelEdits {
    controls: Record<string, ControlValue>;
    locked: ReadonlySet<string>;
    unlocked: ReadonlySet<string>;
    customPresets: ReadonlySet<string>;
}

/** The levels in the order they save: a level above first, so that one below is checked against what it then inherits. */
const SAVE_ORDER: SettingLevel[] = ['server', 'tenant', 'user'];
const LEVEL_NAMES: Partial<Record<SettingLevel, string>> = { server: 'the whole server', tenant: 'this space' };

export const settingFieldId = (key: string) => `setting-${key.replaceAll('.', '-')}`;
export const isSimpleGroup = (group: SettingGroup) => group.fields.some(field => SIMPLE_SETTINGS.has(field.key));

/**
 * The server settings being edited on the administration page. Each tab of settings-schema.ts shows in its own
 * sub-page, but they are one form: changes are kept while moving between pages and saved together, and the errors
 * coming back mark the tabs (`faultyTabs`) holding them. Only the most used settings show until `showAll`.
 *
 * The levels (this space, the whole server) are edited one at a time, but each keeps its changes while another is
 * shown (`pending`), and Save saves every level changed. Only the settings changed are sent: a level's other values,
 * loaded before another level changed, never pin what it inherits.
 */
@Injectable()
export class SettingsForm {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    private readonly sidebar = inject(SidebarService);
    private readonly config = inject(ConfigService);
    private readonly theme = inject(ThemeService);
    private readonly searchHistory = inject(SearchHistory);

    private readonly fields = new Map(TABS.flatMap(tab => tab.groups.flatMap(group => group.fields.map(field => [field.key, field] as const))));
    /** Field or group key → tab id. */
    private readonly tabOfKey = new Map(TABS.flatMap(tab => tab.groups.flatMap(group =>
        [...(group.key ? [[group.key, tab.id] as const] : []), ...group.fields.map(field => [field.key, tab.id] as const)])));

    /** Field filled by a `preset` choice → that choice. */
    private readonly presetOfFill = new Map([...this.fields.values()].flatMap(field =>
        field.type === 'preset' ? [[field.fills.url, field] as const, [field.fills.name, field] as const] : []));
    /** The `preset` choices set to "Custom…" (picked, or a filled field edited) since the form was filled. */
    private readonly customPresets = signal<ReadonlySet<string>>(new Set());

    /** The controls as loaded: what "unsaved" compares with. */
    private readonly saved = signal<Record<string, ControlValue>>({});
    /** The controls' values by field key: text, or a boolean for check boxes. */
    private readonly controls = signal<Record<string, ControlValue>>({});

    readonly loaded = signal(false);
    readonly loadError = signal<string | null>(null);
    /** Messages by field or group key. */
    readonly errors = signal<Record<string, string>>({});
    readonly summary = signal<string | null>(null);
    readonly customized = signal(false);
    readonly saving = signal(false);
    readonly showAll = signal(readPreference(SHOW_ALL_KEY, '0') === '1');

    /**
     * The level edited: the space's settings (`tenant`), the server's, which are the defaults of every space
     * (`server`), or the signed-in user's own preferences (`user`, see `forUser`).
     */
    readonly level = signal<SettingLevel>('tenant');
    /** How each setting shows at that level: where its value comes from, whether it can change or be locked; null from servers without levels. */
    readonly states = signal<Record<string, SettingState> | null>(null);
    /** The settings locked at this level, as loaded and as ticked. */
    private readonly savedLocks = signal<ReadonlySet<string>>(new Set());
    private readonly locks = signal<ReadonlySet<string>>(new Set());
    /** The level was picked (or fixed by `forUser`): loading again keeps it. */
    private levelChosen = false;
    /** The changes not saved of the levels not shown (or still loading), put back when they show again. */
    private readonly pending = signal<ReadonlyMap<SettingLevel, LevelEdits>>(new Map());

    /** The levels of the administration page the session may edit: the space's, and the server's for server administrators. */
    readonly levels = computed<SettingLevel[]>(() => {
        const rights = this.config.rights();
        return [
            ...(Object.values(SETTINGS_RIGHTS).some(right => rights.has(right)) ? ['tenant' as const] : []),
            ...(rights.has(Rights.serverSettings) ? ['server' as const] : []),
        ];
    });

    readonly faultyTabs = computed(() => new Set(Object.keys(this.errors()).map(key => this.tabOfKey.get(key)).filter(Boolean)));
    /** The changes not saved of the level shown; null when there are none. */
    private readonly edits = computed<LevelEdits | null>(() => {
        const saved = this.saved();
        const controls = this.controls();
        const savedLocks = this.savedLocks();
        const locks = this.locks();
        const changed = Object.fromEntries(Object.entries(controls).filter(([key, value]) => value !== saved[key]));
        const locked = new Set([...locks].filter(key => !savedLocks.has(key)));
        const unlocked = new Set([...savedLocks].filter(key => !locks.has(key)));
        return Object.keys(changed).length || locked.size || unlocked.size
            ? { controls: changed, locked, unlocked, customPresets: this.customPresets() }
            : null;
    });
    /** The tabs of the level shown with changes not saved yet (values or locks). */
    readonly changedTabs = computed(() => {
        const edits = this.edits();
        return new Set(edits ? [...Object.keys(edits.controls), ...edits.locked, ...edits.unlocked].map(key => this.tabOfKey.get(key)!) : []);
    });
    /** The levels with changes not saved yet: the one shown, and those kept while another shows. */
    readonly changedLevels = computed(() => new Set([...this.pending().keys(), ...(this.edits() ? [this.level()] : [])]));
    /** Some level has changes not saved yet: Save saves them all, Discard forgets them all. */
    readonly changed = computed(() => this.changedLevels().size > 0);

    /**
     * Edits the user's own preferences (the user level) rather than the space's or server's settings: only the
     * settings each user may set for themselves show.
     */
    forUser(): this {
        this.level.set('user');
        this.levelChosen = true;
        return this;
    }

    async load(): Promise<void> {
        if (!this.levelChosen) {
            // The level picked last time in this browser, when the session may still edit it.
            const remembered = readPreference(LEVEL_KEY) as SettingLevel | null;
            const levels = this.levels();
            this.level.set(remembered && levels.includes(remembered) ? remembered : levels[0] ?? 'tenant');
            this.levelChosen = true;
        }
        const level = this.level();
        try {
            const view = await (level === 'user' ? this.api.preferences() : this.api.settings(level));
            // Another level was picked meanwhile: its own load fills the form.
            if (level !== this.level())
                return;
            this.fill(view);
            this.restore(level);
            this.loadError.set(null);
        } catch (error) {
            if (level === this.level())
                this.loadError.set((error as Error).message);
        }
    }

    /**
     * Edits another level; remembered by the browser. The changes not saved of the level left are kept, and come
     * back over its settings (loaded again, so what it inherits is current) when it shows again.
     */
    async useLevel(level: SettingLevel): Promise<void> {
        if (level === this.level())
            return;
        this.keep();
        writePreference(LEVEL_KEY, level);
        this.level.set(level);
        this.loaded.set(false);
        await this.load();
    }

    /** Keeps the changes of the level shown in `pending` (none: forgets what it had there). */
    private keep(): void {
        // Not loaded yet: its changes, if any, are still in `pending`.
        if (!this.loaded())
            return;
        const edits = this.edits();
        const level = this.level();
        this.pending.update(pending => {
            const next = new Map(pending);
            if (edits)
                next.set(level, edits);
            else
                next.delete(level);
            return next;
        });
    }

    /**
     * Puts the kept changes of `level` back over its settings, just loaded. A setting this level can no longer change
     * (locked above, or no longer in the person's sections) drops its change, as does a lock it can no longer set.
     */
    private restore(level: SettingLevel): void {
        const edits = this.pending().get(level);
        if (!edits)
            return;
        this.pending.update(pending => {
            const next = new Map(pending);
            next.delete(level);
            return next;
        });
        const editable = (key: string) => this.state(key)?.editable !== false;
        const lockable = (key: string) => Boolean(this.state(key)?.lockable);
        this.controls.update(controls => ({
            ...controls,
            ...Object.fromEntries(Object.entries(edits.controls).filter(([key]) => key in controls && editable(key))),
        }));
        this.locks.update(locks => {
            const next = new Set(locks);
            edits.locked.forEach(key => lockable(key) && next.add(key));
            edits.unlocked.forEach(key => lockable(key) && next.delete(key));
            return next;
        });
        this.customPresets.set(edits.customPresets);
    }

    /** The state of a setting at this level; undefined from servers without levels. */
    state(key: string): SettingState | undefined {
        return this.states()?.[key];
    }

    /** A setting this level cannot change: shown, disabled, with why (`lockNote`). */
    fieldLocked(field: SettingField): boolean {
        const state = this.state(field.type === 'preset' ? field.fills.url : field.key);
        return state !== undefined && !state.editable;
    }

    /** Why a setting cannot change at this level; null when it can. */
    lockNote(field: SettingField): string | null {
        const state = this.state(field.type === 'preset' ? field.fills.url : field.key);
        if (!state || state.editable)
            return null;
        switch (state.lockedBy) {
            case 'environment':
                return 'Set by the server\'s configuration: it cannot be changed here.';
            case 'server':
                return 'Locked by the server\'s settings.';
            case 'tenant':
                return 'Locked by the space\'s settings.';
        }
        const level = this.level();
        if (state.scope === 'server' && level !== 'server')
            return 'A setting of the whole server: server administrators change it in the server\'s settings.';
        if (level === 'server' && state.scope !== 'server')
            return 'Set in each space\'s own settings.';
        return 'You don\'t have the right to change the settings of this section.';
    }

    /** This level can lock the setting for the levels below (libraries, users). */
    lockable(field: SettingField): boolean {
        return field.type !== 'preset' && Boolean(this.state(field.key)?.lockable);
    }

    locked(key: string): boolean {
        return this.locks().has(key);
    }

    setLocked(key: string, locked: boolean): void {
        this.locks.update(locks => {
            const next = new Set(locks);
            if (locked)
                next.add(key);
            else
                next.delete(key);
            return next;
        });
    }

    control(key: string): ControlValue {
        return this.controls()[key] ?? '';
    }

    setControl(key: string, value: ControlValue): void {
        this.controls.update(controls => ({ ...controls, [key]: value }));
        // Editing a filled field by hand makes its choice "Custom…", which stays even if the link is emptied.
        const preset = this.presetOfFill.get(key);
        if (preset)
            this.customPresets.update(keys => new Set(keys).add(preset.key));
    }

    fieldHidden(field: SettingField): boolean {
        // A user's preferences: only the settings users set for themselves, all of them.
        if (this.level() === 'user')
            return this.state(field.type === 'preset' ? field.fills.url : field.key)?.scope !== 'user';
        return !this.showAll() && !SIMPLE_SETTINGS.has(field.key);
    }

    groupHidden(group: SettingGroup): boolean {
        if (this.level() === 'user')
            return group.fields.every(field => this.fieldHidden(field));
        return !this.showAll() && !isSimpleGroup(group);
    }

    setShowAll(all: boolean): void {
        writePreference(SHOW_ALL_KEY, all ? '1' : '0');
        this.showAll.set(all);
    }

    /**
     * The entry of a `preset` choice matching its link field, "custom" when none does, '' without a link, unless
     * "Custom…" was picked (or the fields edited) since: an empty link is then being written.
     */
    presetValue(field: SettingField): string {
        if (field.type !== 'preset')
            return '';
        const url = String(this.control(field.fills.url)).trim();
        const index = field.presets.findIndex(preset => preset[2] === url);
        return !url ? (this.customPresets().has(field.key) ? 'custom' : '') : index >= 0 ? String(index) : 'custom';
    }

    /** The fields a `preset` choice fills are disabled while it is "None": there is nothing to write in them. */
    fieldDisabled(field: SettingField): boolean {
        const preset = this.presetOfFill.get(field.key);
        return Boolean(preset) && this.presetValue(preset!) === '';
    }

    /**
     * Fills the fields of a `preset` choice. "Custom…" fills nothing: it enables the fields, brings the link field
     * into view (the full view, if needed) and returns its key, to be written by hand.
     */
    applyPreset(field: SettingField, value: string): string | null {
        if (field.type !== 'preset')
            return null;
        if (value === 'custom') {
            this.customPresets.update(keys => new Set(keys).add(field.key));
            if (!SIMPLE_SETTINGS.has(field.fills.url))
                this.showAll.set(true);
            return field.fills.url;
        }
        const preset = field.presets[Number(value)];
        this.setControl(field.fills.url, value && preset ? preset[2] : '');
        this.setControl(field.fills.name, value && preset ? preset[1] : '');
        this.customPresets.update(keys => {
            const next = new Set(keys);
            next.delete(field.key);
            return next;
        });
        return null;
    }

    /** Back to the values last loaded or saved, at every level. */
    discard(): void {
        this.pending.set(new Map());
        this.controls.set(this.saved());
        this.locks.set(this.savedLocks());
        this.customPresets.set(new Set());
        this.showErrors(null);
    }

    /**
     * Saves the changes of every level, the server's before the space's, sending only the settings changed. A level
     * whose values are refused shows (with its errors) and keeps its changes; the levels saved before it stay saved.
     */
    async save(): Promise<SaveOutcome> {
        const shown = this.level();
        const all = new Map(this.pending());
        const own = this.edits();
        if (own)
            all.set(shown, own);
        const levels = SAVE_ORDER.filter(level => all.has(level));

        // Numbers are checked here first, in the order the levels save.
        for (const level of levels) {
            const local = this.invalidNumbers(all.get(level)!);
            if (local) {
                await this.showLevel(level);
                this.showErrors(local);
                return 'invalid';
            }
        }

        const saved: SettingLevel[] = [];
        this.saving.set(true);
        try {
            for (const level of levels) {
                const changes = this.toChanges(all.get(level)!, level);
                try {
                    const view = level === 'user'
                        ? await this.api.savePreferenceChanges(changes)
                        : await this.api.saveSettingChanges(changes, this.states() ? level : undefined);
                    saved.push(level);
                    if (level === this.level())
                        this.fill(view);
                    else
                        this.pending.update(pending => {
                            const next = new Map(pending);
                            next.delete(level);
                            return next;
                        });
                } catch (error) {
                    if (saved.length)
                        await this.applied();
                    if (error instanceof ApiError && error.errors) {
                        await this.showLevel(level);
                        this.showErrors(error.errors);
                        if (saved.length)
                            this.toasts.show(`Saved the settings of ${saved.map(l => LEVEL_NAMES[l]).join(' and ')}; those of ${LEVEL_NAMES[level]} need fixing.`);
                        return 'invalid';
                    }
                    this.toasts.error(`Could not save the settings: ${(error as Error).message}`);
                    return 'failed';
                }
            }
        } finally {
            this.saving.set(false);
        }
        this.toasts.show(shown === 'user' ? 'Preferences saved.' : 'Settings saved.');
        if (saved.length)
            await this.applied();
        return 'saved';
    }

    /** Shows `level` (its kept changes over its settings) unless it shows already. */
    private async showLevel(level: SettingLevel): Promise<void> {
        if (level !== this.level())
            await this.useLevel(level);
    }

    /** What a level's changes send: the values as the server stores them, and the locks changed. */
    private toChanges(edits: LevelEdits, level: SettingLevel): SettingChanges {
        const values = Object.fromEntries(Object.entries(edits.controls)
            .filter(([key]) => this.fields.has(key))
            .map(([key, control]) => [key, this.toValue(this.fields.get(key)!, control)]));
        return level === 'user' ? { values } : { values, lock: [...edits.locked], unlock: [...edits.unlocked] };
    }

    /** Forgets the saved settings (after asking); true when done. */
    async reset(): Promise<boolean> {
        const level = this.level();
        const [question, done] = {
            environment: ['', ''],
            server: ['Forget the saved settings and go back to the values in the server\'s configuration file?', 'Settings reset to the server\'s configuration.'],
            tenant: ['Forget the space\'s own settings and follow the server\'s again?', 'The space follows the server\'s settings again.'],
            user: ['Forget your own preferences and follow the space\'s settings again?', 'Your preferences follow the space\'s settings again.'],
        }[level];
        if (!confirm(question))
            return false;
        try {
            this.fill(await (level === 'user' ? this.api.resetPreferences() : this.api.resetSettings(this.states() ? level : undefined)));
            this.toasts.show(done);
            await this.applied();
            return true;
        } catch (error) {
            this.toasts.error(`Could not reset the settings: ${(error as Error).message}`);
            return false;
        }
    }

    async clearHistory(): Promise<void> {
        if (!confirm('Forget the searched and viewed tags? The sidebar\'s “Last searched” and “Last viewed” lists start empty.'))
            return;
        try {
            await this.api.clearTagHistory();
            this.sidebar.refresh();
            this.toasts.show('Tag history cleared.');
        } catch (error) {
            this.toasts.error(`Could not clear the history: ${(error as Error).message}`);
        }
    }

    /** Kept by the browser, so nothing is asked of the server. */
    clearSearchHistory(): void {
        if (!confirm('Forget the searches made in this browser? The search box stops suggesting them.'))
            return;
        this.searchHistory.clear();
        this.toasts.show('Search history cleared.');
    }

    /** The settings changed on the server: reload what the client derives from them (theme, links, page sizes...). */
    private async applied(): Promise<void> {
        try {
            this.theme.setDefault((await this.config.load()).theme);
        } catch (error) {
            this.toasts.error(`Could not reload the settings: ${(error as Error).message}`);
        }
    }

    private fill(settings: SettingsView): void {
        const controls: Record<string, ControlValue> = {};
        for (const [key, field] of this.fields) {
            if (field.type === 'preset')
                continue;
            const value = getValue(settings.values, key);
            controls[key] = field.type === 'checkbox' ? Boolean(value)
                : field.type === 'list' ? ((value as string[] | null) ?? []).join('\n')
                    : field.type === 'extensions' ? ((value as string[] | null) ?? []).join(', ')
                        : value === null || value === undefined ? '' : String(value);
        }
        this.saved.set(controls);
        this.controls.set(controls);
        const states = settings.keys ?? null;
        const locks = new Set(Object.entries(states ?? {}).filter(([, state]) => state.locked).map(([key]) => key));
        this.states.set(states);
        this.savedLocks.set(locks);
        this.locks.set(locks);
        this.customPresets.set(new Set());
        this.customized.set(settings.customized);
        this.loaded.set(true);
        this.showErrors(null);
    }

    /** A control's value as the server stores it (a `preset` choice only fills other fields: not called for one). */
    private toValue(field: SettingField, control: ControlValue): unknown {
        if (field.type === 'checkbox')
            return Boolean(control);
        if (field.type === 'number' || field.type === 'select' && field.numeric)
            return String(control).trim() === '' ? NaN : Number(control);
        if (field.type === 'list')
            return String(control).split('\n').map(v => v.trim()).filter(Boolean);
        if (field.type === 'extensions')
            return String(control).split(/[,\s]+/).map(v => v.trim()).filter(Boolean);
        return String(control).trim() || null;
    }

    /** Shows `errors` (messages by setting key) next to their fields; messages for unknown keys go to the summary. */
    private showErrors(errors: Record<string, string[]> | null): void {
        const known: Record<string, string> = {};
        const remaining: string[] = [];
        for (const [key, messages] of Object.entries(errors ?? {})) {
            if (this.tabOfKey.has(key))
                known[key] = messages.join(' ');
            else
                remaining.push(...messages);
        }
        // An error about a setting the simple view hides needs it shown to be fixed.
        if (!this.showAll() && Object.keys(known).some(key => !SIMPLE_SETTINGS.has(key)))
            this.showAll.set(true);
        this.errors.set(known);
        this.summary.set(errors
            ? (this.faultyTabs().size ? 'Some settings need fixing (see the marked sections).' : 'The settings could not be saved.') + (remaining.length ? ` ${remaining.join(' ')}` : '')
            : null);
    }

    /** The changed numbers that are not whole (or empty), checked before asking the server. */
    private invalidNumbers(edits: LevelEdits): Record<string, string[]> | null {
        const errors: Record<string, string[]> = {};
        for (const [key, control] of Object.entries(edits.controls)) {
            const field = this.fields.get(key);
            if (field?.type === 'number' && !Number.isInteger(this.toValue(field, control)))
                errors[key] = ['Enter a whole number.'];
        }
        return Object.keys(errors).length ? errors : null;
    }
}
