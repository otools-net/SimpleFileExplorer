import { MenuEntry } from '../menus/context-menu';

/**
 * The document toolbars' buttons as entries of their "⋯" menu (overflow rows: on a narrow screen they leave the bar
 * from the right; agents/docs/features/overflow-menus.md): the ones every reader has.
 */

/** Show › Tree / Table / Source / Split, the one shown checked. */
export function modesEntry<T extends string>(
    modes: readonly { id: T; label: string; title: string }[],
    current: T,
    set: (mode: T) => void,
): MenuEntry {
    const shown = modes.find((mode) => mode.id === current);
    return {
        id: 'mode',
        label: $localize`Show: ${shown?.label ?? current}:mode:`,
        icon: 'rows',
        title: $localize`Choose how the document is shown.`,
        submenu: () =>
            modes.map((mode) => ({
                id: `mode-${mode.id}`,
                label: mode.label,
                icon: 'rows',
                title: mode.title,
                checked: mode.id === current,
                run: () => set(mode.id),
            })),
    };
}

export function lineNumbersEntry(on: boolean, toggle: () => void): MenuEntry {
    return {
        id: 'lineNumbers',
        label: $localize`Line numbers`,
        icon: 'line-numbers',
        title: $localize`Show or hide the number of each line.`,
        checked: on,
        run: toggle,
    };
}

export function findEntry(
    disabled: boolean,
    find: () => void,
    title = $localize`Find words in the source (Ctrl+F).`,
): MenuEntry {
    return { id: 'find', label: $localize`Find`, icon: 'search', shortcut: 'Ctrl+F', title, disabled, run: find };
}

export function readingEntry(
    open: boolean,
    toggle: () => void,
    title = $localize`Change the font, size and colours of the text.`,
): MenuEntry {
    return { id: 'reading', label: $localize`Reading settings`, icon: 'settings', title, checked: open, run: toggle };
}
