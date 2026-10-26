import { InjectionToken, signal, Type } from '@angular/core';
import { LibraryItem } from '../core/models';

/** The viewer's own markup for media: those formats keep it. */
export type BuiltinView = 'image' | 'video' | 'audio';

/**
 * A kind of file the web client shows (agents/docs/development/file-viewers-plan.md). Adding a format is one entry in
 * `FORMATS` and a component loaded on demand: this file is in the initial bundle, so it stays small.
 */
export interface FileFormat {
    readonly id: string;
    /** What the status bar and menus call it. */
    readonly label: string;
    /** Several formats match a file: the highest priority wins (markdown above plain text). */
    readonly priority: number;
    matches(item: LibraryItem): boolean;
    readonly view: { readonly builtin: BuiltinView } | { readonly load: () => Promise<Type<unknown>> };
    /** The reading preferences it uses: code (monospace, no wrap) or prose (wrapped, width-limited). */
    readonly profile?: 'code' | 'prose';
    /** It has a page of its own (`view?path=`): what "open in a new tab" opens, rather than the raw file. */
    readonly page?: boolean;
    /** Its view edits the file (Edit in the item menu, `edit` input); others only show it. */
    readonly edits?: boolean;
}

/**
 * What a format component may ask of the place showing it (the viewer, or a page of its own). The component takes the
 * inputs `item`, `format` and `edit` (opened ready to be changed: a format that doesn't edit ignores it).
 */
export interface FileViewHost {
    readonly place: 'viewer' | 'page';
    /** The session may use `right` on the file shown. */
    can(right: string): boolean;
    /**
     * Shows another file in its place (a link of a document, a copy just saved; `edit`: ready to be changed); absent:
     * the link is followed.
     */
    open?(item: LibraryItem, options?: { edit?: boolean }): void;
    /**
     * Registers what to ask before the file shown is left (unsaved changes): `canLeave` answers whether it may be,
     * asking the person when needed. Returns the way to take it back.
     */
    guard?(canLeave: () => boolean): () => void;
}

export const FILE_VIEW_HOST = new InjectionToken<FileViewHost>('FILE_VIEW_HOST');

/**
 * Extensions shown as text (lower case, without the dot). Media extensions are the server's (`Sfe:FileTypes`), so a
 * name here that the server calls a video (`.ts`) stays a video.
 */
export const TEXT_EXTENSIONS: ReadonlySet<string> = new Set([
    // Plain text, logs, notes
    'txt', 'text', 'log', 'err', 'nfo', 'diz', 'asc', 'me', '1st',
    'md', 'markdown', 'mdown', 'mkd', 'rst', 'adoc', 'asciidoc', 'org', 'tex', 'bib',
    // Data and configuration
    'json', 'jsonc', 'json5', 'jsonl', 'ndjson', 'geojson', 'topojson', 'har', 'webmanifest', 'xml', 'xsd', 'xsl', 'xslt', 'plist', 'svg',
    'yaml', 'yml', 'toml', 'ini', 'cfg', 'conf', 'config', 'properties', 'env', 'editorconfig', 'reg', 'inf',
    'csv', 'tsv', 'psv', 'sql', 'graphql', 'gql', 'proto', 'prisma',
    // Subtitles and playlists
    'srt', 'vtt', 'ass', 'ssa', 'lrc', 'cue', 'm3u', 'm3u8', 'pls',
    // Web
    'html', 'htm', 'xhtml', 'css', 'scss', 'sass', 'less', 'js', 'mjs', 'cjs', 'jsx', 'tsx', 'mts', 'cts', 'vue', 'svelte', 'astro',
    // Programming languages
    'c', 'h', 'cc', 'cpp', 'cxx', 'hpp', 'hxx', 'ino', 'cs', 'csx', 'vb', 'fs', 'fsx', 'fsi', 'java', 'kt', 'kts', 'scala', 'groovy', 'gradle',
    'go', 'rs', 'swift', 'm', 'mm', 'dart', 'py', 'pyw', 'pyi', 'rb', 'php', 'pl', 'pm', 'lua', 'r', 'jl', 'hs', 'elm', 'erl', 'ex', 'exs',
    'clj', 'cljs', 'lisp', 'scm', 'ml', 'mli', 'nim', 'zig', 'v', 'sv', 'vhd', 'vhdl', 'asm', 's', 'pas', 'd', 'f', 'f90', 'cob', 'tcl',
    // Scripts and builds
    'sh', 'bash', 'zsh', 'fish', 'ksh', 'ps1', 'psm1', 'psd1', 'bat', 'cmd', 'vbs', 'ahk', 'awk', 'sed',
    'csproj', 'vbproj', 'fsproj', 'props', 'targets', 'sln', 'slnx', 'resx', 'xaml', 'razor', 'cshtml',
    'cmake', 'mk', 'mak', 'gitignore', 'gitattributes', 'gitmodules', 'dockerignore', 'npmrc', 'nvmrc', 'lock', 'patch', 'diff',
]);

/** Files without an extension, or whose whole name says they are text. */
export const TEXT_NAMES: ReadonlySet<string> = new Set([
    'readme', 'license', 'licence', 'copying', 'authors', 'contributors', 'changelog', 'changes', 'news', 'todo', 'notice', 'install',
    'makefile', 'gnumakefile', 'dockerfile', 'containerfile', 'jenkinsfile', 'vagrantfile', 'gemfile', 'rakefile', 'procfile', 'brewfile',
    'cmakelists.txt', '.gitignore', '.gitattributes', '.editorconfig', '.env', '.bashrc', '.zshrc', '.profile', '.npmrc', 'hosts',
]);

/**
 * The extension of the item's name, lower case without the dot ('' without one; `.gitignore` → `gitignore`). From
 * the name: the server's `extension` and the test fakes' differ (with or without the dot).
 */
export function extensionOf(item: LibraryItem): string {
    const dot = item.name.lastIndexOf('.');
    return dot >= 0 ? item.name.slice(dot + 1).toLowerCase() : '';
}

/** The file's name and extension say it is text: only files the server doesn't call media. */
/** The library's own extensions shown as text (`Sfe:FileTypes:Text`, from the config): a signal, so that listings follow. */
const libraryTextExtensions = signal<ReadonlySet<string>>(new Set());

export function setTextExtensions(extensions: readonly string[] | null | undefined): void {
    libraryTextExtensions.set(new Set((extensions ?? []).map(extension => extension.toLowerCase().replace(/^\./, ''))));
}

export function isTextFile(item: LibraryItem): boolean {
    if (item.kind !== 'file')
        return false;
    const extension = extensionOf(item);
    return TEXT_NAMES.has(item.name.toLowerCase()) || TEXT_EXTENSIONS.has(extension) || libraryTextExtensions().has(extension);
}

export const IMAGE_FORMAT: FileFormat = { id: 'image', label: 'Picture', priority: 0, matches: item => item.kind === 'image', view: { builtin: 'image' } };
export const VIDEO_FORMAT: FileFormat = { id: 'video', label: 'Video', priority: 0, matches: item => item.kind === 'video', view: { builtin: 'video' } };
export const AUDIO_FORMAT: FileFormat = { id: 'audio', label: 'Audio', priority: 0, matches: item => item.kind === 'audio', view: { builtin: 'audio' } };

const textView = { load: () => import('./text/text-view.component').then(m => m.TextViewComponent) };

export const TEXT_FORMAT: FileFormat = { id: 'text', label: 'Text', priority: 10, matches: isTextFile, view: textView, profile: 'code', page: true, edits: true };

/** Text files meant to be read rather than run: wrapped, in a reading font (the prose profile). */
export const PROSE_EXTENSIONS: ReadonlySet<string> = new Set(['txt', 'text', 'md', 'markdown', 'mdown', 'mkd', 'rst', 'adoc', 'asciidoc', 'org']);
export const PROSE_NAMES: ReadonlySet<string> = new Set(['readme', 'license', 'licence', 'copying', 'authors', 'contributors', 'changelog', 'changes', 'news', 'todo', 'notice', 'install']);

export function isProseFile(item: LibraryItem): boolean {
    return item.kind === 'file' && (PROSE_NAMES.has(item.name.toLowerCase()) || PROSE_EXTENSIONS.has(extensionOf(item)));
}

export const PROSE_FORMAT: FileFormat = { id: 'prose', label: 'Text', priority: 11, matches: isProseFile, view: textView, profile: 'prose', page: true, edits: true };

/** Markdown: read rendered (with its source a click away). */
export const MARKDOWN_EXTENSIONS: ReadonlySet<string> = new Set(['md', 'markdown', 'mdown', 'mkd', 'mkdn']);

export const MARKDOWN_FORMAT: FileFormat = {
    id: 'markdown',
    label: 'Markdown',
    priority: 20,
    matches: item => item.kind === 'file' && MARKDOWN_EXTENSIONS.has(extensionOf(item)),
    view: { load: () => import('./markdown/markdown-view.component').then(m => m.MarkdownViewComponent) },
    profile: 'prose',
    page: true,
    edits: true,
};

/**
 * JSON: a tree of its members (with its source a click away). Not JSON5 (unquoted keys) nor JSON Lines (logs, read and
 * followed as text).
 */
export const JSON_EXTENSIONS: ReadonlySet<string> = new Set(['json', 'jsonc', 'geojson', 'topojson', 'webmanifest', 'har']);

export const JSON_FORMAT: FileFormat = {
    id: 'json',
    label: 'JSON',
    priority: 20,
    matches: item => item.kind === 'file' && JSON_EXTENSIONS.has(extensionOf(item)),
    view: { load: () => import('./json/json-view.component').then(m => m.JsonViewComponent) },
    profile: 'code',
    page: true,
    edits: true,
};

/** Tables: comma (or semicolon) separated, tab separated, pipe separated. */
export const CSV_EXTENSIONS: ReadonlySet<string> = new Set(['csv', 'tsv', 'psv']);

export const CSV_FORMAT: FileFormat = {
    id: 'csv',
    label: 'Table',
    priority: 20,
    matches: item => item.kind === 'file' && CSV_EXTENSIONS.has(extensionOf(item)),
    view: { load: () => import('./csv/csv-view.component').then(m => m.CsvViewComponent) },
    profile: 'code',
    page: true,
    edits: true,
};

/** Subtitles: SubRip and WebVTT (SSA / ASS stay text). */
export const SUBTITLE_EXTENSIONS: ReadonlySet<string> = new Set(['srt', 'vtt']);

export const SUBTITLES_FORMAT: FileFormat = {
    id: 'subtitles',
    label: 'Subtitles',
    priority: 20,
    matches: item => item.kind === 'file' && SUBTITLE_EXTENSIONS.has(extensionOf(item)),
    view: { load: () => import('./subtitles/subtitles-view.component').then(m => m.SubtitlesViewComponent) },
    profile: 'code',
    page: true,
    edits: true,
};

/** PDF documents: read (pdf.js, bundled), not edited. */
export const PDF_FORMAT: FileFormat = {
    id: 'pdf',
    label: 'PDF',
    priority: 20,
    matches: item => item.kind === 'file' && extensionOf(item) === 'pdf',
    view: { load: () => import('./pdf/pdf-view.component').then(m => m.PdfViewComponent) },
    page: true,
};

/** EPUB books: read page by page, two pages side by side or scrolled (epub.js), not edited. */
export const EPUB_FORMAT: FileFormat = {
    id: 'epub',
    label: 'Book',
    priority: 20,
    matches: item => item.kind === 'file' && extensionOf(item) === 'epub',
    view: { load: () => import('./epub/epub-view.component').then(m => m.EpubViewComponent) },
    profile: 'prose',
    page: true,
};

/** The library plays Flash movies (`Player:Flash`, `config.flash`): a signal, so that listings follow. */
const flashMovies = signal(false);

export function setFlashMovies(enabled: boolean | null | undefined): void {
    flashMovies.set(enabled === true);
}

/** Flash movies and games: played by Ruffle in a frame of no origin, when the library plays them. */
export const FLASH_FORMAT: FileFormat = {
    id: 'flash',
    label: 'Flash',
    priority: 20,
    matches: item => item.kind === 'file' && extensionOf(item) === 'swf' && flashMovies(),
    view: { load: () => import('./flash/flash-view.component').then(m => m.FlashViewComponent) },
    page: true,
};

/**
 * Which archives the library explores (`config.archives`; null: none, without the right to view files or from an
 * older server): a signal, so that listings follow.
 */
const archiveSettings = signal<{ maxCompressedTarMegabytes: number } | null>(null);

export function setArchives(settings: { maxCompressedTarMegabytes: number } | null | undefined): void {
    archiveSettings.set(settings ?? null);
}

/** Archives explored by their name; compressed tars (no list of entries: listing one decompresses it) up to a size. */
const ARCHIVE_SUFFIXES: readonly string[] = ['.zip', '.7z', '.rar', '.tar'];
const COMPRESSED_TAR_SUFFIXES: readonly string[] = ['.tar.gz', '.tgz', '.tar.bz2', '.tbz2', '.tbz', '.tar.xz', '.txz'];

/**
 * The archive viewer is offered for `item` (agents/docs/features/archive-viewer.md): the server's rule
 * (`ArchiveFormats.IsExplorable`), from the listing alone. Never for one file compressed alone (`.gz`, `.xz`), nor a
 * compressed tar over the library's size; the server checks the bytes (a renamed file answers `not-explorable`).
 */
export function isExplorable(item: LibraryItem): boolean {
    const settings = archiveSettings();
    if (!settings || item.kind !== 'file')
        return false;
    const name = item.name.toLowerCase();
    const longer = (suffix: string) => name.endsWith(suffix) && name.length > suffix.length;
    if (COMPRESSED_TAR_SUFFIXES.some(longer))
        return item.size === null || item.size <= settings.maxCompressedTarMegabytes * 1024 * 1024;
    return ARCHIVE_SUFFIXES.some(longer);
}

/** One file compressed alone: extracted, never explored. */
const SINGLE_SUFFIXES: readonly string[] = ['.gz', '.bz2', '.xz'];

/**
 * `item` may be extracted (by its name: the server reads its bytes): every archive explored, compressed tars of any
 * size, and one file compressed alone. Not without `config.archives` (an older server).
 */
export function isExtractable(item: LibraryItem): boolean {
    if (!archiveSettings() || item.kind !== 'file')
        return false;
    const name = item.name.toLowerCase();
    const longer = (suffix: string) => name.endsWith(suffix) && name.length > suffix.length;
    return [...ARCHIVE_SUFFIXES, ...COMPRESSED_TAR_SUFFIXES, ...SINGLE_SUFFIXES].some(longer);
}

/** Archives (zip, 7z, rar, tars): their folders and files, explored without unpacking them. */
export const ARCHIVE_FORMAT: FileFormat = {
    id: 'archive',
    label: 'Archive',
    priority: 20,
    matches: isExplorable,
    view: { load: () => import('./archive/archive-view.component').then(m => m.ArchiveViewComponent) },
    page: true,
};

/**
 * Files that are archives underneath (zips mostly: comics, books, office documents, packages) but keep their own
 * viewer or none: "Open as archive" explores them. The server reads the bytes, whatever the name.
 */
export const CONTAINER_EXTENSIONS: ReadonlySet<string> = new Set([
    'cbz', 'cbr', 'cb7', 'cbt', 'epub', 'docx', 'xlsx', 'pptx', 'docm', 'xlsm', 'pptm', 'odt', 'ods', 'odp', 'odg', 'vsdx',
    'jar', 'war', 'ear', 'apk', 'aab', 'aar', 'ipa', 'xpi', 'crx', 'vsix', 'nupkg', 'snupkg', 'whl', 'appx', 'msix', 'kmz', '3mf', 'sketch',
]);

/** `item` may be opened as an archive ("Open as archive"): a container by its name, not shown as an archive already. */
export function isContainer(item: LibraryItem): boolean {
    return item.kind === 'file' && archiveSettings() !== null && CONTAINER_EXTENSIONS.has(extensionOf(item)) && !isExplorable(item);
}

/** Every format, any order (the priority decides). */
export const FORMATS: readonly FileFormat[] = [IMAGE_FORMAT, VIDEO_FORMAT, AUDIO_FORMAT, TEXT_FORMAT, PROSE_FORMAT, MARKDOWN_FORMAT, JSON_FORMAT, CSV_FORMAT, SUBTITLES_FORMAT, PDF_FORMAT, EPUB_FORMAT, FLASH_FORMAT, ARCHIVE_FORMAT];

/**
 * How `item` is shown, or null when it isn't (downloaded, or opened in a tab). `asText` shows any file as text
 * ("Open as text"): the server refuses what isn't; `asArchive` a container as an archive ("Open as archive").
 */
export function formatOf(item: LibraryItem | null | undefined, options?: { asText?: boolean; asArchive?: boolean }): FileFormat | null {
    if (!item || item.kind === 'directory')
        return null;
    if (options?.asArchive && isContainer(item))
        return ARCHIVE_FORMAT;
    let best: FileFormat | null = null;
    for (const format of FORMATS) {
        if ((best === null || format.priority > best.priority) && format.matches(item))
            best = format;
    }
    return best ?? (options?.asText && item.kind === 'file' ? TEXT_FORMAT : null);
}

/** The file has a viewer: a click opens it there and it is among the files the viewer steps through. */
export function isViewable(item: LibraryItem): boolean {
    return formatOf(item) !== null;
}
