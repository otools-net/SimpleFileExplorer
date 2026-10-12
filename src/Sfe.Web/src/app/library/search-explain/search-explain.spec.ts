import { explain, parseQuery, Segment, splitOutsideQuotes } from './search-explain';

/** The explanation as plain text, terms in [brackets] and logical words in CAPITALS. */
function words(segments: Segment[]): string {
    return segments.map(segment => segment.kind === 'term' ? `[${segment.value}]`
        : segment.kind === 'op' ? segment.word.toUpperCase()
            : segment.text).join('');
}

describe('search explanation (mirrors SearchQuery.Parse)', () => {
    it('splits on separators outside quotes only', () => {
        expect(splitOutsideQuotes('a "b c" d', ' ')).toEqual(['a', '"b c"', 'd']);
        expect(splitOutsideQuotes('a, "b, c"', ',')).toEqual(['a', '"b, c"']);
    });

    it('puts one alternative into a sentence', () => {
        const result = explain('cat -kitten', '');
        expect(words(result.sentence)).toBe('Finds files in the whole library with [cat] but WITHOUT [kitten] in their name.');
        expect(result.alternatives).toEqual([]);
    });

    it('lists several alternatives, in the folder searched', () => {
        const result = explain('*hol, s:>1mb e:mp4 e:mkv', 'Movies/2024');
        expect(words(result.sentence)).toBe('Finds files in [2024] and its sub-folders:');
        expect(result.alternatives.map(words)).toEqual([
            'EITHER with [hol] in their full path',
            'OR larger than [1 MB] AND with the extension [.mp4] OR [.mkv]',
        ]);
    });

    it('notes the parts that do not work as they may seem', () => {
        const notes = parseQuery('clip.mp4 a*b s:big -s:huge -e: t:').notes.map(words);
        expect(notes).toContain('A [*] only means “search the whole path” at the very start of the search; here it is searched as a character.');
        expect(notes.some(note => note.startsWith('Names are searched without their extension'))).toBe(true);
        expect(notes.some(note => note.startsWith('[s:big] is ignored'))).toBe(true);
        expect(notes.some(note => note.startsWith('[-s:huge] is ignored'))).toBe(true);
        expect(notes.some(note => note.startsWith('[-e:] is ignored'))).toBe(true);
        expect(notes.some(note => note.startsWith('[t:] has no tag name'))).toBe(true);
    });

    it('reads tags and special:untagged', () => {
        expect(words(explain('t:"Two words" -t:work', '').sentence))
            .toBe('Finds files in the whole library tagged [Two words] AND NOT tagged [work].');
        expect(words(explain('special:untagged', '').sentence)).toBe('Finds files in the whole library WITHOUT any tag.');
        expect(words(explain('-special:untagged', '').sentence)).toBe('Finds files in the whole library with at least one tag.');
    });

    it('reads widths, heights and resolutions', () => {
        expect(words(explain('w:>=1920 h:<720', '').sentence))
            .toBe('Finds files in the whole library at least [1920 pixels] wide AND less than [720 pixels] high.');
        expect(words(explain('r:>720X1024', '').sentence))
            .toBe('Finds files in the whole library more than [720 pixels] wide AND more than [1024 pixels] high.');
        expect(words(explain('h:1080', '').sentence)).toBe('Finds files in the whole library exactly [1080 pixels] high.');
        expect(explain('w:1920', '').notes.map(words)).toContain('A resolution only finds photos and videos whose resolution a scan has read.');
    });

    it('notes resolutions it ignores, as the server does', () => {
        const parsed = parseQuery('movie w:big r:1920 -h:7x');
        const notes = parsed.notes.map(words);
        expect(parsed.groups[0].clauses.map(clause => clause.kind)).toEqual(['text']);
        expect(notes.some(note => note.startsWith('[w:big] is ignored: not a valid resolution'))).toBe(true);
        expect(notes.some(note => note.startsWith('[r:1920] is ignored'))).toBe(true);
        expect(notes.some(note => note.startsWith('[-h:7x] is ignored'))).toBe(true);
    });

    it('negates sizes, extensions and resolutions, as the server does', () => {
        const parsed = parseQuery('movie -s:>1gb -e:mp4 -e:.mkv e:avi -w:1920 -r:>720x1024');
        expect(parsed.groups[0].clauses.map(clause => clause.kind)).toEqual(['text', 'size', 'extension', 'extension', 'dimension', 'dimension']);
        const result = explain('movie -s:>1gb -e:mp4 -e:.mkv e:avi -w:1920 -r:>720x1024', '');
        expect(words(result.sentence)).toBe('Finds files in the whole library with [movie] in their name AND NOT larger than [1 GB]'
            + ' AND WITHOUT the extension [.mp4] OR [.mkv] AND with the extension [.avi] AND NOT exactly [1920 pixels] wide'
            + ' AND NOT BOTH more than [720 pixels] wide AND more than [1024 pixels] high.');
        const notes = result.notes.map(words);
        expect(notes.some(note => note.startsWith('An excluded resolution also finds the files whose resolution is not known'))).toBe(true);
        expect(notes.some(note => note.startsWith('A resolution only finds'))).toBe(false);
        expect(result.sentence.filter(segment => segment.kind === 'term' && segment.excluded)).toHaveLength(6);
    });

    it('keeps the negations of each alternative to itself', () => {
        const result = explain('-e:mp4 cat, e:mp4 -cat, -t:dog,t:dog', '');
        expect(result.alternatives.map(words)).toEqual([
            'EITHER WITHOUT the extension [.mp4] AND with [cat] in their name',
            'OR with the extension [.mp4] AND WITHOUT [cat] in their name',
            'OR NOT tagged [dog]',
            'OR tagged [dog]',
        ]);
    });

    it('keeps included and excluded filters of one kind apart', () => {
        const parsed = parseQuery('e:mp4 -e:mkv e:avi s:>1mb -s:>1gb w:>100 -w:>1000');
        expect(parsed.groups[0].clauses.map(clause => `${clause.kind}${'negated' in clause && clause.negated ? '-' : '+'}`))
            .toEqual(['extension+', 'extension-', 'size+', 'size-', 'dimension+', 'dimension-']);
        expect(words(explain('e:mp4 -e:mkv e:avi', '').sentence))
            .toBe('Finds files in the whole library with the extension [.mp4] OR [.avi] AND WITHOUT the extension [.mkv].');
    });

    it('reads the edge cases as the server does', () => {
        // "--x" excludes the word "-x"; a lone "-" is nothing; a full-path search drops every "*".
        expect(words(explain('--e:mp4', '').sentence)).toBe('Finds files in the whole library WITHOUT [-e:mp4] in their name.');
        expect(explain(' - , -', '').sentence.map(segment => segment.kind === 'text' ? segment.text : '')).toEqual(['Nothing to search for yet.']);
        expect(words(explain('*a*b, -*c e:*mp4', '').alternatives[1])).toBe('OR WITHOUT [c] in their full path AND with the extension [.mp4]');
        // A quote before a prefix makes a plain word.
        expect(parseQuery('"e:mkv" "t:cat"').groups[0].clauses).toEqual([
            { kind: 'text', include: [{ value: 'e:mkv', token: '"e:mkv"' }, { value: 't:cat', token: '"t:cat"' }], exclude: [] },
        ]);
    });

    it('reads durations, as DurationFilter.TryParse does', () => {
        expect(words(explain('d:>10m d:<=1:30:00', '').sentence))
            .toBe('Finds files in the whole library longer than [10 minutes] AND lasting at most [1:30:00].');
        expect(explain('D:1h, d:=45s, d:>=1.5', '').alternatives.map(words).join(' '))
            .toBe('EITHER lasting about [1 hour] OR lasting exactly [45 seconds] OR lasting at least [1.5 minutes]');
        expect(explain('d:30s', '').notes.map(words)).toContain('A duration only finds videos and audio files whose duration a scan has read.');
        const about = explain('d:10', '').sentence.find(segment => segment.kind === 'term');
        expect(about).toMatchObject({ value: '10 minutes', title: 'd:10: from 9:30 to 10:30' });
    });

    it('negates durations and notes the ones it ignores', () => {
        const result = explain('movie -d:>2h d:long d:10min', '');
        expect(words(result.sentence)).toBe('Finds files in the whole library with [movie] in their name AND NOT longer than [2 hours].');
        const notes = result.notes.map(words);
        expect(notes.some(note => note.startsWith('An excluded duration also finds the files whose duration is not known'))).toBe(true);
        expect(notes.some(note => note.startsWith('[d:long] is ignored: not a valid duration'))).toBe(true);
        expect(notes.some(note => note.startsWith('[d:10min] is ignored'))).toBe(true);
    });

    it('reads high and low resolution', () => {
        const result = explain('r:HR -r:lr', '');
        expect(words(result.sentence)).toBe('Finds files in the whole library in [high resolution] AND NOT in [low resolution].');
        const notes = result.notes.map(words);
        expect(notes.some(note => note.startsWith('High and low resolution compare the shorter side'))).toBe(true);
        expect(notes).toContain('A resolution only finds photos and videos whose resolution a scan has read.');
        expect(notes.some(note => note.startsWith('An excluded resolution also finds'))).toBe(true);
        expect(parseQuery('r:mr').notes.map(words).some(note => note.startsWith('[r:mr] is ignored: not a valid resolution'))).toBe(true);
    });

    it('gives an approximate size its range, as the server rounds it', () => {
        const size = explain('s:700mb', '').sentence.find(segment => segment.kind === 'term');
        expect(size).toMatchObject({ value: '700 MB', title: 's:700mb: from 699.5 MB to 700.5 MB' });
    });
});
