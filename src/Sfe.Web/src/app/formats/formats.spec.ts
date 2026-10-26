import { item } from '../../testing/fakes';
import { ARCHIVE_FORMAT, CSV_FORMAT, EPUB_FORMAT, extensionOf, FLASH_FORMAT, isExplorable, setArchives, setFlashMovies, formatOf, FORMATS, isViewable, JSON_FORMAT, PDF_FORMAT, SUBTITLES_FORMAT, MARKDOWN_FORMAT, PROSE_FORMAT, TEXT_FORMAT } from './formats';

describe('formats', () => {
    it('keeps the viewer of the media the server classified', () => {
        expect(formatOf(item('a/photo.jpg', { kind: 'image' }))?.view).toEqual({ builtin: 'image' });
        expect(formatOf(item('a/clip.mkv', { kind: 'video' }))?.view).toEqual({ builtin: 'video' });
        expect(formatOf(item('a/song.mp3', { kind: 'audio' }))?.view).toEqual({ builtin: 'audio' });
        // .ts is a video by default on the server (MPEG transport stream): it stays one.
        expect(formatOf(item('a/recording.ts', { kind: 'video' }))?.id).toBe('video');
    });

    it('shows text files by extension or by their whole name, whatever the case', () => {
        for (const name of ['server.log', 'Program.cs', 'app.JS', 'Dockerfile', '.gitignore', 'settings.yaml', 'film.ass', 'build.ps1'])
            expect(formatOf(item(`a/${name}`, { kind: 'file' })), name).toBe(TEXT_FORMAT);
        // The server's extensions have no dot; the name decides anyway.
        expect(formatOf({ ...item('a/server.log', { kind: 'file' }), extension: 'log' })).toBe(TEXT_FORMAT);
    });

    it('reads notes and documents as prose, code as code, both with a page of their own', () => {
        for (const name of ['notes.txt', 'Trip.MD', 'README', 'license', 'guide.rst'])
            expect(formatOf(item(`a/${name}`, { kind: 'file' }))?.profile, name).toBe('prose');
        expect(formatOf(item('a/notes.txt', { kind: 'file' }))).toBe(PROSE_FORMAT);
        expect(TEXT_FORMAT.profile).toBe('code');
        expect(PROSE_FORMAT.view).toBe(TEXT_FORMAT.view);
        expect(TEXT_FORMAT.page && PROSE_FORMAT.page).toBe(true);
        expect(formatOf(item('a/photo.jpg', { kind: 'image' }))?.page).toBeFalsy();
    });

    it('shows Markdown rendered, above plain text', () => {
        for (const name of ['README.md', 'Trip.MD', 'notes.markdown'])
            expect(formatOf(item(`a/${name}`, { kind: 'file' })), name).toBe(MARKDOWN_FORMAT);
        expect(formatOf(item('a/md', { kind: 'file' }))).toBeNull();
        expect(MARKDOWN_FORMAT.profile).toBe('prose');
        expect(extensionOf(item('a/.gitignore', { kind: 'file' }))).toBe('gitignore');
        expect(extensionOf(item('a/Makefile', { kind: 'file' }))).toBe('');
    });

    it('shows JSON as a tree, above plain text; JSON Lines and JSON5 stay text', () => {
        for (const name of ['data.json', 'Settings.JSONC', 'map.geojson', 'site.webmanifest', 'trace.har'])
            expect(formatOf(item(`a/${name}`, { kind: 'file' })), name).toBe(JSON_FORMAT);
        for (const name of ['events.jsonl', 'events.ndjson', 'config.json5'])
            expect(formatOf(item(`a/${name}`, { kind: 'file' })), name).toBe(TEXT_FORMAT);
        expect(JSON_FORMAT.profile).toBe('code');
        expect(FORMATS.filter(format => format.edits).map(format => format.id)).toEqual(['text', 'prose', 'markdown', 'json', 'csv', 'subtitles']);
    });

    it('shows comma, tab and pipe separated files as a table', () => {
        for (const name of ['people.csv', 'Export.CSV', 'data.tsv', 'log.psv'])
            expect(formatOf(item(`a/${name}`, { kind: 'file' })), name).toBe(CSV_FORMAT);
        expect(CSV_FORMAT.page && CSV_FORMAT.edits).toBe(true);
    });

    it('shows SubRip and WebVTT files as cues; SSA stays text', () => {
        for (const name of ['Film.en.srt', 'clip.VTT'])
            expect(formatOf(item(`a/${name}`, { kind: 'file' })), name).toBe(SUBTITLES_FORMAT);
        expect(formatOf(item('a/Film.ass', { kind: 'file' }))).toBe(TEXT_FORMAT);
    });

    it('reads PDF documents in a viewer of their own, without editing them', () => {
        for (const name of ['Guide.pdf', 'SCAN.PDF'])
            expect(formatOf(item(`a/${name}`, { kind: 'file' })), name).toBe(PDF_FORMAT);
        expect(PDF_FORMAT.page).toBe(true);
        expect(PDF_FORMAT.edits).toBeFalsy();
        expect(PDF_FORMAT.profile).toBeUndefined();
    });

    it('reads EPUB books in a reader of their own, as prose, without editing them', () => {
        for (const name of ['Hut stories.epub', 'NOVEL.EPUB'])
            expect(formatOf(item(`a/${name}`, { kind: 'file' })), name).toBe(EPUB_FORMAT);
        expect(EPUB_FORMAT.profile).toBe('prose');
        expect(EPUB_FORMAT.page).toBe(true);
        expect(EPUB_FORMAT.edits).toBeFalsy();
    });

    it('plays Flash movies when the library plays them, without editing them', () => {
        const movie = item('a/Run.swf', { kind: 'file' });
        expect(formatOf(movie), 'off until the configuration says so').toBeNull();

        setFlashMovies(true);
        try {
            for (const name of ['Run.swf', 'GAME.SWF'])
                expect(formatOf(item(`a/${name}`, { kind: 'file' })), name).toBe(FLASH_FORMAT);
            expect(FLASH_FORMAT.page).toBe(true);
            expect(FLASH_FORMAT.edits).toBeFalsy();
            expect(FLASH_FORMAT.profile).toBeUndefined();
        } finally {
            setFlashMovies(false);
        }
        expect(formatOf(movie)).toBeNull();
    });

    it('explores archives only where it works: never one file compressed alone, nor a big compressed tar', () => {
        const zip = item('a/backup.zip', { kind: 'file' });
        expect(formatOf(zip), 'off until the configuration says so (the right to view files)').toBeNull();

        setArchives({ maxCompressedTarMegabytes: 10 });
        try {
            for (const name of ['backup.zip', 'Photos.7Z', 'old.rar', 'src.tar', 'src.tar.gz', 'src.TGZ', 'logs.tar.xz', 'logs.tbz2'])
                expect(formatOf(item(`a/${name}`, { kind: 'file', size: 1024 })), name).toBe(ARCHIVE_FORMAT);
            for (const name of ['dump.gz', 'dump.xz', 'dump.bz2', 'zip', '.zip', 'notes.txt'])
                expect(isExplorable(item(`a/${name}`, { kind: 'file', size: 1024 })), name).toBe(false);
            expect(isExplorable(item('a/big.tar.gz', { kind: 'file', size: 10 * 1024 * 1024 + 1 })), 'listing it would decompress all of it').toBe(false);
            expect(isExplorable(item('a/ten.tar.gz', { kind: 'file', size: 10 * 1024 * 1024 })), 'up to the size').toBe(true);
            expect(isExplorable(item('a/folder.zip', { kind: 'directory' })), 'a folder named so').toBe(false);
            expect(ARCHIVE_FORMAT.page).toBe(true);
            expect(ARCHIVE_FORMAT.edits).toBeFalsy();
        } finally {
            setArchives(null);
        }
        expect(formatOf(zip)).toBeNull();
    });

    it('has no viewer for folders and other files, unless opened as text', () => {
        const archive = item('a/backup.bin', { kind: 'file' });

        expect(formatOf(item('a/folder.txt', { kind: 'directory' }))).toBeNull();
        expect(formatOf(archive)).toBeNull();
        expect(isViewable(archive)).toBe(false);
        expect(formatOf(archive, { asText: true })).toBe(TEXT_FORMAT);
        expect(formatOf(item('a/photo.jpg', { kind: 'image' }), { asText: true })?.id).toBe('image');
        expect(formatOf(item('a/folder', { kind: 'directory' }), { asText: true })).toBeNull();
        expect(formatOf(null)).toBeNull();
    });

    it('loads the text view on demand', async () => {
        const view = TEXT_FORMAT.view;
        expect('load' in view).toBe(true);
        const component = await (view as { load: () => Promise<unknown> }).load();
        expect((component as { name: string }).name).toContain('TextViewComponent');
    });
});
