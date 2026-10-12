import { Injectable, signal } from '@angular/core';
import { Theme } from './models';
import { readPreference, writePreference } from './preferences';

// Light or dark colors, as `data-theme` on <html> (see styles/base.css). This browser's pick (System section) wins over the
// server default (`config.theme`: system | light | dark), and "system" follows the device's mode, live.
// src/theme-boot.ts applies the same keys before the page is drawn: keep them in step.
const CHOICE = 'sfe.theme';                     // light | dark; empty = the server default
const SERVER_DEFAULT = 'sfe.theme.default';     // last default seen, for theme-boot.ts
export type ThemeChoice = '' | 'light' | 'dark';

@Injectable({ providedIn: 'root' })
export class ThemeService {
    private readonly deviceLight = matchMedia('(prefers-color-scheme: light)');
    private serverDefault: Theme = this.readServerDefault();
    private readonly picked = signal<ThemeChoice>('');
    private readonly defaultText = signal('');

    /** This browser's pick: `'light'`, `'dark'`, or `''` for the server default. */
    readonly current = this.picked.asReadonly();
    /** The server default in words: "light", "dark" or "follows device". */
    readonly defaultName = this.defaultText.asReadonly();

    constructor() {
        this.deviceLight.addEventListener('change', () => this.apply());
        this.apply();
    }

    /** The server default (`config.theme`), used unless this browser picked a theme. */
    setDefault(theme: Theme | null | undefined): void {
        this.serverDefault = theme || 'system';
        writePreference(SERVER_DEFAULT, this.serverDefault);
        this.apply();
    }

    /** Sets this browser's pick; `''` goes back to the server default. */
    choose(choice: ThemeChoice): void {
        writePreference(CHOICE, choice);
        this.apply();
    }

    private choice(): ThemeChoice {
        const choice = readPreference(CHOICE, '');
        return choice === 'light' || choice === 'dark' ? choice : '';
    }

    private readServerDefault(): Theme {
        const theme = readPreference(SERVER_DEFAULT, 'system');
        return theme === 'light' || theme === 'dark' ? theme : 'system';
    }

    /** `'light'` or `'dark'` for a theme name, the device's mode for `'system'`. */
    private resolve(theme: Theme | ThemeChoice): 'light' | 'dark' {
        return theme === 'light' || theme === 'dark' ? theme : this.deviceLight.matches ? 'light' : 'dark';
    }

    private apply(): void {
        const choice = this.choice();
        this.picked.set(choice);
        document.documentElement.dataset['theme'] = this.resolve(choice || this.serverDefault);
        this.defaultText.set(this.serverDefault === 'system' ? 'follows device' : this.serverDefault);
    }
}
