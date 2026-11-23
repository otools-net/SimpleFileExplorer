import { refusedText } from './item-command-texts';

describe('the texts of the item commands', () => {
    it("says why an entry is greyed out, naming the item or the selection's items", () => {
        expect(refusedText('delete', ['beach.jpg'])).toBe('You may not delete “beach.jpg”.');
        expect(refusedText('move', ['a.jpg', 'b.jpg'])).toBe('You may not move the selected items.');
        expect(refusedText('view', ['notes.log'])).toBe('You may not open “notes.log” here.');
        expect(refusedText('paste', ['Holidays'])).toBe('You may not put anything into “Holidays”.');
        expect(refusedText('compress', ['Holidays'])).toBe('You may not put “Holidays” in a zip.');
        expect(refusedText('tags', ['a.jpg', 'b.jpg'])).toBe('You may not change the tags of the selected items.');
    });
});
