import type { ServerMode } from './access.models';

/**
 * The words naming the server's mode (Local / External, agents/docs/features/server-modes.md): the ⋮ menu's badge,
 * System › About, and the reason a setting only one mode uses is greyed out. What each mode allows is listed in
 * docs/administration/system.md#what-each-mode-allows (the matrix); these sentences say the same.
 */

/** The mode's name, as the Installation page writes it. */
export function modeName(mode: ServerMode): string {
    return mode === 'local' ? $localize`:server mode:Local` : $localize`:server mode:External`;
}

/** What the mode means for who opens SFE, in a sentence. */
export function modeSummary(mode: ServerMode): string {
    return mode === 'local'
        ? $localize`Local mode: only this computer opens SFE, only administrators sign in, and there are no visitors.`
        : $localize`External mode: other devices open SFE, every account signs in, and the library folder and ffmpeg \
come from the server's configuration.`;
}

/** Why something only `needed` uses is greyed out on a server in the other mode. */
export function onlyInModeNote(needed: ServerMode): string {
    return needed === 'local'
        ? $localize`Only in Local mode: this server is in External mode.`
        : $localize`Only in External mode: this server is in Local mode.`;
}
