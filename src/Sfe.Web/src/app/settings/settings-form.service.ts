import { computed, inject, Injectable, signal } from '@angular/core';
import { Api, ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { SettingLevel, SettingState, SettingsValues, SettingsView } from '../core/models';
import { readPreference, writePreference } from '../core/preferences';
import { Rights, SETTINGS_RIGHTS } from '../core/rights';
import { ThemeService } from '../core/theme.service';
import { ToastService } from '../core/toast.service';
import { SearchHistory } from '../library/search-history.service';
import { SidebarService } from '../library/sidebar/sidebar.service';
import { getValue, SettingField, SettingGroup, setValue, SIMPLE_SETTINGS, TABS } from './settings-schema';

const SHOW_ALL_KEY = 'sfe.settings.showAll';
const LEVEL_KEY = 'sfe.settings.level';

export type ControlValue = string | boolean;

/** What a save came to: `invalid` = errors are shown by their fields. */
export type SaveOutcome = 'saved' | 'invalid' | 'failed';

export const settingFieldId = (key: string) => `setting-${key.replaceAll('.', '-')}`;
export const isSimpleGroup = (group: SettingGroup) => group.fields.some(field => SIMPLE_SETTINGS.has(field.key));

/**
 * The server settings being edited on the administration page. Each tab of settings-schema.ts shows in its own
 * sub-page, but they are one form: changes are kept while moving between pages and saved together, and the errors
 * coming back mark the tabs (`faultyTabs`) holding them. Only the most used settings show until `showAll`.
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

    /** The values as loaded, for the keys the form doesn't show. */
    private values: SettingsValues = {};
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

    /** The levels of the administration page the session may edit: the space's, and the server's for server administrators. */
    readonly levels = computed<SettingLevel[]>(() => {
        const rights = this.config.rights();
        return [
            ...(Object.values(SETTINGS_RIGHTS).some(right => rights.has(right)) ? ['tenant' as const] : []),
            ...(rights.has(Rights.serverSettings) ? ['server' as const] : []),
        ];
    });

    readonly faultyTabs = computed(() => new Set(Object.keys(this.errors()).map(key => this.tabOfKey.get(key)).filter(Boolean)));
    /** The tabs with changes not saved yet (values or locks). */
    readonly changedTabs = computed(() => {
        const saved = this.saved();
        const controls = this.controls();
        const savedLocks = this.savedLocks();
        const locks = this.locks();
        const lockChanges = [...new Set([...savedLocks, ...locks])].filter(key => savedLocks.has(key) !== locks.has(key));
        return new Set([...Object.keys(controls).filter(key => controls[key] !== saved[key]), ...lockChanges].map(key => this.tabOfKey.get(key)!));
    });
    readonly changed = computed(() => this.changedTabs().size > 0);

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
        try {
            this.fill(await (this.level() === 'user' ? this.api.preferences() : this.api.settings(this.level())));
            this.loadError.set(null);
        } catch (error) {
            this.loadError.set((error as Error).message);
        }
    }

    /** Edits another level (asks first when changes are not saved yet); remembered by the browser. */
    async useLevel(level: SettingLevel): Promise<void> {
        if (level === this.level() || this.changed() && !confirm('Forget the settings you changed and edit another level?'))
            return;
        writePreference(LEVEL_KEY, level);
        this.level.set(level);
        this.loaded.set(false);
        await this.load();
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

    /** Back to the values last loaded or saved. */
    discard(): void {
        this.controls.set(this.saved());
        this.locks.set(this.savedLocks());
        this.customPresets.set(new Set());
        this.showErrors(null);
    }

    async save(): Promise<SaveOutcome> {
        const settings = this.collect();
        const local = this.invalidNumbers(settings);
        if (local) {
            this.showErrors(local);
            return 'invalid';
        }
        const level = this.level();
        const locksChanged = [...new Set([...this.locks(), ...this.savedLocks()])].some(key => this.locks().has(key) !== this.savedLocks().has(key));
        const locks = [...this.locks()];
        this.saving.set(true);
        try {
            let view = level === 'user' ? await this.api.savePreferences(settings) : await this.api.saveSettings(settings, this.states() ? level : undefined);
            if (locksChanged && level !== 'user')
                view = await this.api.saveSettingLocks(locks, level);
            this.fill(view);
            this.toasts.show(level === 'user' ? 'Preferences saved.' : 'Settings saved.');
            await this.applied();
            return 'saved';
        } catch (error) {
            if (error instanceof ApiError && error.errors) {
                this.showErrors(error.errors);
                return 'invalid';
            }
            this.toasts.error(`Could not save the settings: ${(error as Error).message}`);
            return 'failed';
        } finally {
            this.saving.set(false);
        }
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
        this.values = structuredClone(settings.values);
        const controls: Record<string, ControlValue> = {};
        for (const [key, field] of this.fields) {
            if (field.type === 'preset')
                continue;
            const value = getValue(this.values, key);
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

    private collect(): Record<string, unknown> {
        const result = structuredClone(this.values);
        for (const [key, field] of this.fields) {
            const control = this.control(key);
            let value: unknown;
            if (field.type === 'preset')
                continue;   // only fills other fields
            else if (field.type === 'checkbox')
                value = Boolean(control);
            else if (field.type === 'number')
                value = String(control).trim() === '' ? NaN : Number(control);
            else if (field.type === 'list')
                value = String(control).split('\n').map(v => v.trim()).filter(Boolean);
            else if (field.type === 'extensions')
                value = String(control).split(/[,\s]+/).map(v => v.trim()).filter(Boolean);
            else
                value = String(control).trim() || null;
            setValue(result, key, value);
        }
        return result;
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

    private invalidNumbers(settings: Record<string, unknown>): Record<string, string[]> | null {
        const errors: Record<string, string[]> = {};
        for (const [key, field] of this.fields) {
            if (field.type === 'number' && !Number.isInteger(getValue(settings, key)))
                errors[key] = ['Enter a whole number.'];
        }
        return Object.keys(errors).length ? errors : null;
    }
}
