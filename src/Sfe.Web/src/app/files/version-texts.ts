import { formatDate, formatSize, joinParts } from '../core/format';
import { icu } from '../core/i18n/icu';
import { TrashItem } from './file.models';

/**
 * The sentences of a file's Previous versions, as whole messages marked for translation
 * (agents/docs/development/translations.md).
 */

/** What the sheet says first: where the versions come from. */
export function versionsIntro(): string {
    return $localize`Each time this file is saved over in SFE (text editor, picture editor), the content it had \
is kept in the trash until the trash is purged. Restore one as a copy next to it, or over it: the file as it is \
now then becomes a version.`;
}

/** The list is empty. */
export function noVersions(): string {
    return $localize`No previous versions: this file has not been saved over in SFE lately.`;
}

/** "22 Nov 2026, 14:03": when the file was saved over (the version is the content it had then). */
export function versionTitle(version: TrashItem): string {
    return formatDate(version.deletedAt);
}

/** "2 KB · Saved over by Sam.": the version's size, then who saved over it. */
export function versionDetails(version: TrashItem): string {
    const who = version.deletedBy;
    return joinParts([
        version.size === null ? null : formatSize(version.size),
        who ? $localize`Saved over by ${who}:account:.` : null,
    ]);
}

/** The hints of a version's buttons. */
export function versionHints(): { open: string; copy: string; over: string } {
    return {
        open: $localize`Open this version in a new tab.`,
        copy: $localize`Copy this version next to the file. A number is added to its name.`,
        over: $localize`Put this version in place of the file (asks first). The file as it is now becomes a version.`,
    };
}

/** The question before a version is restored over the file. */
export function restoreOverQuestion(name: string, date: string): string {
    return $localize`Put the version of ${date}:date: in place of “${name}:name:”? \
The file as it is now is kept as a version in the trash.`;
}

/** The toasts of a restore. */
export function restoredOverText(name: string): string {
    return $localize`“${name}:name:” is back as it was. Its content before is a version now.`;
}

export function restoredCopyText(copy: string): string {
    return $localize`The version is restored as “${copy}:name:”.`;
}

export function notRestoredText(reason: string | null | undefined): string {
    return reason
        ? $localize`Could not restore the version: ${reason}:reason:`
        : $localize`Could not restore the version.`;
}

/** The Properties line: how many versions a file has. */
export function versionCount(count: number, more: boolean): string {
    return more
        ? icu($localize`{count, plural, other {More than # versions}}`, { count })
        : icu($localize`{count, plural, =0 {None} one {# version} other {# versions}}`, { count });
}
