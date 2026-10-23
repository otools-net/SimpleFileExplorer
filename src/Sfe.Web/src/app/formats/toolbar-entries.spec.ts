import { findEntry, lineNumbersEntry, modesEntry, readingEntry } from './toolbar-entries';

describe('toolbar entries', () => {
    const modes = [{ id: 'tree', label: 'Tree', title: 'As a tree' }, { id: 'source', label: 'Source', title: 'Its text' }] as const;

    it('gives the view switch as a submenu, the one shown checked', () => {
        const set = vi.fn();
        const entry = modesEntry(modes, 'source', set);
        expect(entry.label).toBe('Show: Source');
        const choices = entry.submenu!();
        expect(choices.map(choice => [choice.label, choice.checked])).toEqual([['Tree', false], ['Source', true]]);
        choices[0].run!();
        expect(set).toHaveBeenCalledWith('tree');
    });

    it('checks the on/off ones and greys Find while nothing is shown', () => {
        expect(lineNumbersEntry(true, () => {}).checked).toBe(true);
        expect(readingEntry(false, () => {}).checked).toBe(false);
        expect(findEntry(true, () => {}).disabled).toBe(true);
        expect(findEntry(false, () => {}).shortcut).toBe('Ctrl+F');
    });
});
