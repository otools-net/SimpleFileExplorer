import { jsonTexts, matchCount, membersCount, problemLine, problemReason, showMoreLabel, shownOf } from './json-texts';

describe('json texts', () => {
    it('counts the matches of the filter', () => {
        expect(matchCount(0, false)).toBe('Nothing matches');
        expect(matchCount(1, false)).toBe('1 match');
        expect(matchCount(1500, false)).toBe('1,500 matches');
        expect(matchCount(2000, true)).toBe('The first 2,000 matches');
    });

    it('counts the members of a container and those shown', () => {
        expect(membersCount(1, true)).toBe('1 item');
        expect(membersCount(3, false)).toBe('3 keys');
        expect(membersCount(0, false)).toBe('0 keys');
        expect(showMoreLabel(200)).toBe('Show 200 more');
        expect(shownOf(200, 1000)).toBe('200 of 1,000 shown');
    });

    it('tells where the source is not JSON', () => {
        const problem = { line: 3, column: 3, message: problemReason('CommaExpected') };

        expect(problemLine(problem, 0)).toBe('Not JSON at line 3, column 3: A comma is expected.');
        expect(problemLine(problem, 1)).toBe('Not JSON at line 3, column 3: A comma is expected (and 1 more).');
        expect(problemReason('CloseBraceExpected')).toBe('A closing } is expected');
        expect(problemReason('InvalidCommentToken')).toBe("Comments aren't JSON");
        expect(problemReason('SomethingNew')).toBe('SomethingNew');
    });

    it('names the modes and the toggles', () => {
        expect(jsonTexts.modes().map((mode) => mode.label)).toEqual(['Tree', 'Source', 'Split']);
        expect(jsonTexts.toggle(true)).toBe('Close');
        expect(jsonTexts.members('plan.json')).toBe('Members of “plan.json”');
        expect(jsonTexts.kind(false)).toBe('JSON with comments');
        expect(jsonTexts.pathCopied('$.days[1]')).toBe('Path “$.days[1]” copied.');
        expect(jsonTexts.valueCopied('$.days[1]')).toBe('Value of “$.days[1]” copied.');
    });
});
