import { icu, textLocale } from '../../core/i18n/icu';

/**
 * The sentences of Scan (its question, its toasts), as whole messages marked for translation
 * (agents/docs/development/translations.md).
 */

/** What is scanned: the folder quoted, or the whole library with its name. */
export function scanName(path: string, rootName: string): string {
    return path ? `“${path}”` : $localize`the whole library (${rootName}:library:)`;
}

/** The toast of a scan refused because the folder is kept out of scans (`through`: the folder excluded). */
export function excludedFromScans(name: string, through: string, own: boolean): string {
    return own
        ? $localize`${name}:name: is excluded from scans: what the index has of it stays as it is.`
        : $localize`${name}:name: is inside “${through}:folder:”, which is excluded from scans.`;
}

/** The question before a scan; `skipped`: the folders listed here that scans skip, quoted. */
export function scanQuestion(name: string, skipped: readonly string[]): string {
    const question = $localize`Scan ${name}:name: and all its sub-folders?\n\n\
The search index is updated in the background. This can take a while on large folders.`;
    return skipped.length ? `${question}\n\n${skippedNote(skipped)}` : question;
}

/** The folders that scans skip, the first three named. */
function skippedNote(skipped: readonly string[]): string {
    const names =
        skipped.length > 3
            ? $localize`${skipped.slice(0, 3).join(', ')}:names: and ${skipped.length - 3}:count: more`
            : new Intl.ListFormat(textLocale(), { type: 'conjunction' }).format(skipped);
    return icu(
        $localize`{count, plural, \
one {Excluded from scans: {names}. It is skipped, and what the index has of it stays as it is.} \
other {Excluded from scans: {names}. They are skipped, and what the index has of them stays as it is.}}`,
        { count: skipped.length, names },
    );
}

/** The toast of a scan not started because another one runs (`other`: its folder, '' for the whole library). */
export function otherScanRunning(name: string, other: string): string {
    return other
        ? $localize`Could not scan ${name}:name:: another scan is running, in “${other}:folder:”.`
        : $localize`Could not scan ${name}:name:: another scan is running, in the whole library.`;
}
