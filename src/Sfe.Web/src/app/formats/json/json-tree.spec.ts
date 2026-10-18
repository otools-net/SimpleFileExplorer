import {
    CHUNK, expandAll, filterTree, formatted, indentOf, parseJson, pathLabel, previewOf, rowsOf, sizeOf, sourceOf,
} from './json-tree';

const keys = (rows: ReturnType<typeof rowsOf>) => rows.map(row => row.more ? `more:${row.more.shown}/${row.more.total}` : row.key ?? '$');

describe('json-tree', () => {
    it('keeps members in the order written and numbers as written', () => {
        const document = parseJson('{"b": 1, "2": 2, "id": 12345678901234567890, "price": 1.50}');
        const rows = rowsOf(document.root, { expanded: new Set(['']) });

        expect(document.strict).toBe(true);
        expect(document.problems).toEqual([]);
        expect(keys(rows)).toEqual(['$', 'b', '2', 'id', 'price']);
        expect(sourceOf(rows[3].node, document.text)).toBe('12345678901234567890');
        expect(sourceOf(rows[4].node, document.text)).toBe('1.50');
    });

    it('allows comments and trailing commas, as not strict JSON', () => {
        const document = parseJson('{\n  // the port\n  "port": 80,\n}');

        expect(document.strict).toBe(false);
        expect(document.problems).toEqual([]);
        expect(keys(rowsOf(document.root, { expanded: new Set(['']) }))).toEqual(['$', 'port']);
    });

    it('tells where it isn\'t JSON, and keeps what parses', () => {
        const document = parseJson('{\n  "a": 1\n  "b": 2\n}');

        expect(document.problems[0]).toMatchObject({ message: 'A comma is expected', line: 3, column: 3 });
        expect(keys(rowsOf(document.root, { expanded: new Set(['']) }))).toEqual(['$', 'a', 'b']);
    });

    it('shows only open containers, a chunk of members at a time', () => {
        const items = Array.from({ length: CHUNK + 50 }, (_, index) => index);
        const document = parseJson(JSON.stringify({ list: items, closed: { a: 1 } }));

        const closed = rowsOf(document.root, { expanded: new Set(['']) });
        expect(keys(closed)).toEqual(['$', 'list', 'closed']);

        const open = rowsOf(document.root, { expanded: new Set(['', '/list']) });
        expect(open).toHaveLength(3 + CHUNK + 1);
        expect(open[2 + CHUNK]).toMatchObject({ id: '/list#more', more: { shown: CHUNK, total: CHUNK + 50 } });

        const all = rowsOf(document.root, { expanded: new Set(['', '/list']), shown: new Map([['/list', CHUNK * 2]]) });
        expect(all).toHaveLength(3 + CHUNK + 50);
    });

    it('opens everything of a small file, a large one as far as the limit', () => {
        const document = parseJson('{"a": {"b": {"c": [1, 2]}}, "d": []}');
        expect([...expandAll(document.root)].sort()).toEqual(['', '/a', '/a/b', '/a/b/c', '/d']);
        expect([...expandAll(document.root, undefined, '/a/b')]).toEqual(['/a/b', '/a/b/c']);

        const large = parseJson(JSON.stringify(Array.from({ length: 50 }, () => ({ x: [1, 2, 3] }))));
        const open = expandAll(large.root, 60);
        expect(open.has('')).toBe(true);
        expect(open.size).toBeLessThan(51);
    });

    it('filters on keys and values, keeping the containers that hold the matches', () => {
        const document = parseJson('{"people": [{"name": "Ann", "age": 30}, {"name": "Bob", "age": 41}], "town": {"name": "Annecy"}}');
        const filter = filterTree(document.root, 'ann');

        expect(filter.count).toBe(2);
        const rows = rowsOf(document.root, { expanded: new Set(), keep: filter.keep });
        expect(keys(rows)).toEqual(['$', 'people', '0', 'name', 'town', 'name']);

        // A container matching by its key shows all it holds when opened.
        const byKey = filterTree(document.root, 'town');
        expect(keys(rowsOf(document.root, { expanded: new Set(['/town']), keep: byKey.keep }))).toEqual(['$', 'town', 'name']);
    });

    it('writes paths as code does, and previews closed containers', () => {
        expect(pathLabel('')).toBe('$');
        expect(pathLabel('/store/books/0/first name')).toBe('$.store.books[0]["first name"]');
        expect(pathLabel('/a~1b/~0')).toBe('$["a/b"]["~"]');

        const document = parseJson('{"id": 4, "tags": ["x", "y"], "more": {"deep": true}}');
        expect(previewOf(document.root!, document.text)).toBe('{ "id": 4, "tags": […], "more": {…} }');
        expect(sizeOf(document.root!)).toBe('3 keys');
        expect(sizeOf(document.root!.children![1].children![1])).toBe('2 items');
    });

    it('lays the source out again as it is indented, comments kept', () => {
        expect(indentOf('{\n    "a": 1\n}')).toEqual({ insertSpaces: true, tabSize: 4 });
        expect(indentOf('{\n\t"a": 1\n}')).toEqual({ insertSpaces: false, tabSize: 4 });
        expect(formatted('{"a":1,\n  // note\n"b":[1,2]}')).toBe('{\n  "a": 1,\n  // note\n  "b": [\n    1,\n    2\n  ]\n}');
    });
});
