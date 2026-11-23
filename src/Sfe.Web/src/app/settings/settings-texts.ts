import { SettingLevel } from '../core/setting.models';

/**
 * The questions and toasts of the settings form (saving, resetting), as whole messages marked for translation
 * (agents/docs/development/translations.md).
 */

/** Two levels saved together, the second refused: `failed` is the level whose values need fixing. */
export function partlySaved(failed: SettingLevel): string {
    return failed === 'server'
        ? $localize`Settings of this space saved. Those of the whole server need fixing.`
        : $localize`Settings of the whole server saved. Those of this space need fixing.`;
}

/** What resetting a level asks first, and the toast once done; null for a level that can't be reset. */
export function resetTexts(level: SettingLevel): { question: string; done: string } | null {
    switch (level) {
        case 'server':
            return {
                question: $localize`Forget the saved settings and go back to the values in the server's \
configuration file? This cannot be undone.`,
                done: $localize`Settings reset to the server's configuration file.`,
            };
        case 'tenant':
            return {
                question: $localize`Forget this space's own settings and follow the server's again? \
This cannot be undone.`,
                done: $localize`This space follows the server's settings again.`,
            };
        case 'user':
            return {
                question: $localize`Forget your own settings and follow the space's again? This cannot be undone.`,
                done: $localize`Your settings follow the space's again.`,
            };
        default:
            return null;
    }
}
