import {
    csvTexts,
    problemLine,
    problemReason,
    separatedBy,
    separatorHint,
    separatorLabel,
    sortHint,
    tableCounts,
} from './csv-texts';

describe('csv texts', () => {
    it('counts the rows and columns with the separator read', () => {
        expect(tableCounts({ shown: 4, total: 4, columns: 3, filtered: false }, ',')).toBe(
            '4 rows × 3 columns · comma separated',
        );
        expect(tableCounts({ shown: 1, total: 1, columns: 1, filtered: false }, '\t')).toBe(
            '1 row × 1 column · tab separated',
        );
        expect(tableCounts({ shown: 2, total: 1200, columns: 2, filtered: true }, ';')).toBe(
            '2 of 1,200 rows × 2 columns · semicolon separated',
        );
        expect(separatedBy(':')).toBe('separated by “:”');
    });

    it('names each separator with its own message', () => {
        expect(separatorLabel('guess')).toBe('Guess');
        expect(separatorLabel('guess', true)).toBe('Separator: guess');
        expect(separatorLabel('pipe', true)).toBe('Pipe');
        expect(separatorHint('semicolon')).toBe('Read the cells as separated by semicolons.');
        expect(separatedBy('|')).toBe('pipe separated');
    });

    it('says what a click on a column does next', () => {
        expect(sortHint('km', null)).toBe('Sort the rows by “km”, smallest first.');
        expect(sortHint('km', 'ascending')).toBe('Sort the rows by “km”, largest first.');
        expect(sortHint('km', 'descending')).toBe('Show the rows in the order of the file.');
    });

    it('tells the cell selected and what is missing', () => {
        expect(csvTexts.cell(5000, 'km', '1,014')).toBe('Row 5,000 · km: 1,014');
        expect(csvTexts.noMatch('zermatt')).toBe('No row holds “zermatt”.');
        expect(csvTexts.empty()).toBe('This file has no rows.');
        expect(csvTexts.cellCopied(1200, 'km')).toBe('Cell in row 1,200, column “km” copied.');
    });

    it('tells where the file is wrong, in words of its own', () => {
        const problem = { record: 1200, message: problemReason('MissingQuotes', 'Quoted field unterminated') };

        expect(problemLine(problem, 0)).toBe("Record 1,200: A quoted cell isn't closed.");
        expect(problemLine(problem, 2)).toBe("Record 1,200: A quoted cell isn't closed (and 2 more).");
        expect(problemReason('Unknown', 'Something else')).toBe('Something else');
    });
});
