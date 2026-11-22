import { icu } from '../core/i18n/icu';
import { TagScope } from '../core/tag.models';

/* The words of the tags dialog of a folder or a search (ScopeTagsDialog). */

/** The fixed texts of its buttons and boxes. */
export const SCOPE_TAG_TEXTS = {
    defaultPlaceholder: $localize`Add a default tag…`,
    addTitle: $localize`Add the tag to these files.`,
    removeTitle: $localize`Remove the tag from these files.`,
    keepOnFilesTitle: $localize`The files already there keep the tag.`,
    newFilesOnlyTitle: $localize`Only the files put there from now on get it.`,
    removeFromFilesTitle: $localize`Also remove it from the files already there.`,
    addToFilesTitle: $localize`Also add it to the files already there.`,
};

/** “Media”, or the library (its own folder). */
export function folderNameOf(path: string): string {
    return path ? `“${path}”` : $localize`:the library's own folder:the library`;
}

/** Where the files are, after "files": "in “Media” and its sub-folders", "found by “cat” in “Media”". */
export function whereOf({ path, recursive, query }: TagScope): string {
    if (query)
        return path
            ? $localize`:where files are:found by “${query}:query:” in “${path}:folder:”`
            : $localize`:where files are:found by “${query}:query:”`;
    if (path)
        return recursive
            ? $localize`:where files are:in “${path}:folder:” and its sub-folders`
            : $localize`:where files are:in “${path}:folder:”`;
    return recursive
        ? $localize`:where files are:in the library`
        : $localize`:where files are:at the top of the library`;
}

export function subtitleOf({ path, recursive, query }: TagScope): string {
    if (query)
        return path ? $localize`Search “${query}:query:” in “${path}:folder:”` : $localize`Search “${query}:query:”`;
    if (path)
        return recursive
            ? $localize`Files in “${path}:folder:” and its sub-folders`
            : $localize`Files in “${path}:folder:”`;
    return recursive ? $localize`Every file of the library` : $localize`Files at the top of the library`;
}

/** How many files the change would touch, and what happens to those the index lacks. */
export function countOf(files: number, search: boolean, connection: boolean): string {
    if (search)
        return files
            ? icu($localize`{count, plural, one {# file found.} other {# files found.}}`, { count: files })
            : $localize`The search finds no file.`;
    if (connection)
        return files
            ? icu(
                  $localize`{count, plural, \
one {# indexed file. Files the library hasn't indexed yet don't change.} \
other {# indexed files. Files the library hasn't indexed yet don't change.}}`,
                  { count: files },
              )
            : $localize`None of these files is indexed in the library yet: \
open the folder again once its sync has run.`;
    return files
        ? icu(
              $localize`{count, plural, \
one {# indexed file. Files not indexed yet are indexed when a tag is added or removed.} \
other {# indexed files. Files not indexed yet are indexed when a tag is added or removed.}}`,
              { count: files },
          )
        : $localize`No file of this folder is indexed yet: they are indexed when a tag is added or removed.`;
}

/** The question before adding or removing `tag`; `count`: the files found (a search), or having it (a removal). */
export function questionOf(tag: string, remove: boolean, where: string, count: number, search: boolean): string {
    const values = { tag, where, count };
    if (remove)
        return icu(
            $localize`{count, plural, \
one {Remove “{tag}” from the # file {where} that has it?} \
other {Remove “{tag}” from the # files {where} that have it?}}`,
            values,
        );
    return search
        ? icu(
              $localize`{count, plural, \
one {Add “{tag}” to the # file {where}?} other {Add “{tag}” to the # files {where}?}}`,
              values,
          )
        : icu($localize`Add “{tag}” to every file {where}?`, values);
}

/** What adding a tag also does: create it, how many have it, the time it takes in a folder. */
export function hintOf(create: boolean, having: number, folder: boolean): string {
    return [
        create ? $localize`The tag is created first.` : '',
        having
            ? icu($localize`{count, plural, one {# file already has it.} other {# files already have it.}}`, {
                  count: having,
              })
            : '',
        folder ? $localize`This can take a while for a large folder: its files are indexed first.` : '',
    ]
        .filter(Boolean)
        .join(' ');
}

export function defaultQuestionOf(tag: string, remove: boolean, folder: string): string {
    return remove
        ? $localize`Stop giving “${tag}:tag:” to the files put into ${folder}:folder:?`
        : $localize`Give “${tag}:tag:” to every file put into ${folder}:folder: or its sub-folders from now on?`;
}

/** What a default tag change does to the files already there; `canTagFiles`: they may change too. */
export function defaultHintOf(create: boolean, remove: boolean, canTagFiles: boolean): string {
    if (remove)
        return canTagFiles
            ? $localize`The files already in it and its sub-folders keep it, unless you remove it from them too.`
            : $localize`The files already there keep it.`;
    return [
        create ? $localize`The tag is created first.` : '',
        canTagFiles
            ? $localize`The files already in it and its sub-folders can get it now too (they are indexed first, \
which can take a while for a large folder).`
            : $localize`The files already there don't change.`,
    ]
        .filter(Boolean)
        .join(' ');
}

/** The hint of a tag on `some` of the `files`: on how many, and what a click does. */
export function someTitleOf(some: number, files: number): string {
    return icu(
        $localize`{count, plural, \
one {On {some} of # file. Click to add it to all of them (asks first).} \
other {On {some} of # files. Click to add it to all of them (asks first).}}`,
        { count: files, some: some.toLocaleString() },
    );
}

/** The hint of a default tag given by a folder above. */
export function inheritedTitleOf(path: string): string {
    return path
        ? $localize`A default tag of “${path}:folder:”: change it there.`
        : $localize`A default tag of the library: change it there.`;
}

/** What adding or removing a tag did. */
export function appliedOf(tag: string, remove: boolean, count: number): string {
    return remove
        ? icu($localize`{count, plural, one {“{tag}” removed from # file.} other {“{tag}” removed from # files.}}`, {
              tag,
              count,
          })
        : icu($localize`{count, plural, one {“{tag}” added to # file.} other {“{tag}” added to # files.}}`, {
              tag,
              count,
          });
}

/** What a default tag change did: to the folder, and to the files already there (`files`, null when not asked). */
export function defaultChangedOf(
    tag: string,
    remove: boolean,
    folder: string,
    files: { added: number; removed: number } | null | undefined,
): string {
    const values = { tag, folder, count: remove ? (files?.removed ?? 0) : (files?.added ?? 0) };
    if (remove)
        return files
            ? icu(
                  $localize`{count, plural, \
one {“{tag}” is no longer a default tag of {folder}: removed from # file.} \
other {“{tag}” is no longer a default tag of {folder}: removed from # files.}}`,
                  values,
              )
            : $localize`“${tag}:tag:” is no longer a default tag of ${folder}:folder:; the files keep it.`;
    return files
        ? icu(
              $localize`{count, plural, \
one {“{tag}” is a default tag of {folder}: added to # file.} \
other {“{tag}” is a default tag of {folder}: added to # files.}}`,
              values,
          )
        : $localize`“${tag}:tag:” is a default tag of ${folder}:folder:: new files there get it.`;
}
