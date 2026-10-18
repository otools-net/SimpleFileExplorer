import {
    columnLetters, columnsOf, delimiterFor, delimiterName, filterRecords, isNumber, numberOf, parseCsv, shownCell, sortRecords,
} from './csv-table';

describe('csv-table', () => {
    it('reads quoted cells, separators and line breaks inside quotes, any line ending', () => {
        const table = parseCsv('name,notes\r\n"Smith, Ann","said ""hi""\nthen left"\r\nBob,\r\n');

        expect(table.delimiter).toBe(',');
        expect(table.rows).toEqual([['name', 'notes'], ['Smith, Ann', 'said "hi"\nthen left'], ['Bob', '']]);
        expect(table.problems).toEqual([]);
    });

    it('guesses semicolons and tabs, or takes the separator given', () => {
        expect(parseCsv('a;b;c\n1;2,5;3\n').delimiter).toBe(';');
        expect(parseCsv('a\tb\n1\t2\n').rows).toEqual([['a', 'b'], ['1', '2']]);
        expect(parseCsv('a,b|c\n1,2|3\n', '|').rows).toEqual([['a,b', 'c'], ['1,2', '3']]);
        expect(parseCsv('just one column\nsecond\n').problems).toEqual([]);
        expect(delimiterFor('tsv')).toBe('\t');
        expect(delimiterFor('psv')).toBe('|');
        expect(delimiterFor('csv')).toBe('auto');
        expect(delimiterName('\t')).toBe('tab');
    });

    it('tells which record is wrong', () => {
        const table = parseCsv('a,b\n1,"unclosed\n');

        expect(table.problems[0].record).toBe(2);
    });

    it('names columns from the header, or with letters', () => {
        const rows = [['town', '', 'town', 'km'], ['Annecy', 'x', 'y', '12'], ['Chamonix', '', '', '1,018.5']];

        expect(columnsOf(rows, true).map(column => column.name)).toEqual(['town', 'B', 'C', 'km']);
        expect(columnsOf(rows, false).map(column => column.name)).toEqual(['A', 'B', 'C', 'D']);
        expect(columnsOf(rows, true).map(column => column.numeric)).toEqual([false, false, false, true]);
        expect(columnsOf(rows, true)[0].width).toBe(9);
        expect(columnLetters(25)).toBe('Z');
        expect(columnLetters(26)).toBe('AA');
        expect(columnLetters(701)).toBe('ZZ');
    });

    it('reads numbers written in the usual ways', () => {
        for (const [cell, value] of [['12', 12], ['-3.5', -3.5], ['1,234', 1234], ['1,234,567.8', 1234567.8], ['1.234,5', 1234.5], ['2,5', 2.5], ['1 234', 1234], ['15%', 15], ['1.234.567', 1234567]] as const)
            expect(numberOf(cell), cell).toBe(value);
        expect(isNumber('12 apples')).toBe(false);
        expect(numberOf('n/a')).toBeNaN();
    });

    it('sorts numbers as numbers and text as people do, empty cells last, ties in the file order', () => {
        const rows = [['name', 'km'], ['b', '10'], ['A', '9'], ['c', ''], ['a2', '10'], ['a10', '1,000']];
        const records = [1, 2, 3, 4, 5];

        expect(sortRecords(records, rows, 1, true, 'ascending')).toEqual([2, 1, 4, 5, 3]);
        expect(sortRecords(records, rows, 1, true, 'descending')).toEqual([5, 1, 4, 2, 3]);
        expect(sortRecords(records, rows, 0, false, 'ascending').map(index => rows[index][0])).toEqual(['A', 'a2', 'a10', 'b', 'c']);
    });

    it('filters records on any cell, any case', () => {
        const rows = [['town'], ['Annecy'], ['Chamonix'], ['annemasse']];

        expect(filterRecords(rows, 1, 'ANN')).toEqual([1, 3]);
        expect(filterRecords(rows, 1, '  ')).toEqual([1, 2, 3]);
    });

    it('shows long cells cut, line breaks as marks', () => {
        expect(shownCell('two\nlines')).toBe('two ↵ lines');
        expect(shownCell('x'.repeat(600))).toHaveLength(501);
        expect(shownCell(undefined)).toBe('');
    });
});
