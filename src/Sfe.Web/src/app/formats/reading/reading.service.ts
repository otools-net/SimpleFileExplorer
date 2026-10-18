import { computed, inject, Injectable, linkedSignal, signal } from '@angular/core';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { Rights } from '../../core/rights';
import { readPreference, writePreference } from '../../core/preferences';
import {
    DEFAULT_READING, fontFamily, fontOf, FONT_SIZES, ProfilePreferences, ReaderTheme, ReadingPreferences, ReadingProfile, sanitizeReading,
} from './reading';

/** This browser's changes to the reading preferences (a versioned JSON value: only what was changed is in it). */
export const READING_KEY = 'sfe.reading';

/** What this browser changed: parts left out follow the defaults. */
interface ReadingOverrides {
    version: 1;
    theme?: ReaderTheme;
    customFont?: string;
    code?: ProfilePreferences;
    prose?: ProfilePreferences;
}

function readOverrides(): ReadingOverrides {
    try {
        const value = JSON.parse(readPreference(READING_KEY) ?? 'null') as Partial<ReadingOverrides> | null;
        return value && value.version === 1 ? value as ReadingOverrides : { version: 1 };
    } catch {
        return { version: 1 };
    }
}

/**
 * The reading preferences of the text views (agents/docs/features/reading-preferences.md): the account's defaults
 * (`reading.*` settings: the library's, with the account's own over them, from `api/config`) with this browser's
 * changes over them, saved as they are made. **Save as my default** makes this browser's the account's.
 */
@Injectable({ providedIn: 'root' })
export class ReadingService {
    private readonly config = inject(ConfigService);
    private readonly api = inject(Api);
    private readonly overrides = signal<ReadingOverrides>(readOverrides());
    private fontsLinked = false;

    /** The defaults this browser's changes go over: the account's (following the config as it loads). */
    readonly defaults = linkedSignal<ReadingPreferences>(() => sanitizeReading(this.config.config()?.reading ?? DEFAULT_READING));

    /** This browser changed something the account's defaults don't have. */
    readonly changed = computed(() => JSON.stringify(this.preferences()) !== JSON.stringify(this.defaults()));

    /** A signed-in account that may keep its own preferences. */
    readonly canSaveDefaults = computed(() => Boolean(this.config.config()?.user) && this.config.can(Rights.ownPreferences));

    /** The preferences in effect. */
    readonly preferences = computed((): ReadingPreferences => {
        const defaults = this.defaults();
        const overrides = this.overrides();
        return sanitizeReading({ ...defaults, ...overrides, code: overrides.code ?? defaults.code, prose: overrides.prose ?? defaults.prose }, defaults);
    });

    profile(profile: ReadingProfile): ProfilePreferences {
        return this.preferences()[profile];
    }

    update(profile: ReadingProfile, changes: Partial<ProfilePreferences>): void {
        this.save({ ...this.overrides(), [profile]: { ...this.profile(profile), ...changes } });
    }

    setTheme(theme: ReaderTheme): void {
        this.save({ ...this.overrides(), theme });
    }

    setCustomFont(customFont: string): void {
        this.save({ ...this.overrides(), customFont: customFont.slice(0, 100) });
    }

    /** One step bigger (+1) or smaller (−1), within the limits; 0 = back to the default size. */
    changeSize(profile: ReadingProfile, step: -1 | 0 | 1): void {
        const size = step === 0 ? this.defaults()[profile].size : this.profile(profile).size + step;
        this.update(profile, { size: Math.min(FONT_SIZES.max, Math.max(FONT_SIZES.min, size)) });
    }

    /** The profile and the theme back to the defaults (the other profile keeps its changes). */
    reset(profile: ReadingProfile): void {
        const { [profile]: _, theme: __, ...rest } = this.overrides();
        this.save({ ...rest, version: 1 });
    }

    /** The CSS variables of `reader.css` for a view of this profile. */
    style(profile: ReadingProfile): Record<string, string> {
        const preferences = this.preferences();
        const own = preferences[profile];
        return {
            '--reader-font': fontFamily(profile, own, preferences.customFont),
            '--reader-size': `${own.size}px`,
            '--reader-line-height': String(own.lineHeight),
            '--reader-width': own.width ? `${own.width}ch` : 'none',
            '--reader-padding': `${own.padding}px`,
        };
    }

    /** Downloads the bundled fonts (`reader-fonts.css`) the first time a profile uses one of them. */
    linkFonts(profile: ReadingProfile): void {
        if (this.fontsLinked || !fontOf(this.profile(profile).font).bundled)
            return;
        this.fontsLinked = true;
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        // Not hashed by the build: the version keeps an old copy from being used after an update.
        link.href = `reader-fonts.css?v=${encodeURIComponent(this.config.config()?.version ?? '')}`;
        link.dataset['readerFonts'] = '';
        document.head.appendChild(link);
    }

    /**
     * The preferences in effect become the account's own (every browser it signs in from starts from them); this
     * browser's changes are then nothing more.
     */
    async saveAsDefaults(): Promise<void> {
        const preferences = this.preferences();
        const values: Record<string, unknown> = { 'reading.theme': preferences.theme, 'reading.customFont': preferences.customFont };
        for (const profile of ['code', 'prose'] as const) {
            for (const [key, value] of Object.entries(preferences[profile]))
                values[`reading.${profile}.${key}`] = value;
        }
        await this.api.savePreferenceChanges({ values });
        await this.config.load();
        this.save({ version: 1 });
    }

    private save(overrides: ReadingOverrides): void {
        this.overrides.set(overrides);
        writePreference(READING_KEY, JSON.stringify(overrides));
    }
}
