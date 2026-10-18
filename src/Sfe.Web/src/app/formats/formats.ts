import { InjectionToken, Type } from '@angular/core';
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
}

/** What a format component may ask of the place showing it (the viewer, or a page of its own). */
export interface FileViewHost {
    readonly place: 'viewer' | 'page';
    /** The session may use `right` on the file shown. */
    can(right: string): boolean;
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
    'json', 'jsonc', 'json5', 'jsonl', 'ndjson', 'geojson', 'webmanifest', 'xml', 'xsd', 'xsl', 'xslt', 'plist', 'svg',
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

/** The file's name and extension say it is text: only files the server doesn't call media. */
export function isTextFile(item: LibraryItem): boolean {
    if (item.kind !== 'file')
        return false;
    const name = item.name.toLowerCase();
    const dot = name.lastIndexOf('.');
    return TEXT_NAMES.has(name) || (dot >= 0 && TEXT_EXTENSIONS.has(name.slice(dot + 1)));
}

export const IMAGE_FORMAT: FileFormat = { id: 'image', label: 'Picture', priority: 0, matches: item => item.kind === 'image', view: { builtin: 'image' } };
export const VIDEO_FORMAT: FileFormat = { id: 'video', label: 'Video', priority: 0, matches: item => item.kind === 'video', view: { builtin: 'video' } };
export const AUDIO_FORMAT: FileFormat = { id: 'audio', label: 'Audio', priority: 0, matches: item => item.kind === 'audio', view: { builtin: 'audio' } };

export const TEXT_FORMAT: FileFormat = {
    id: 'text',
    label: 'Text',
    priority: 10,
    matches: isTextFile,
    view: { load: () => import('./text/text-view.component').then(m => m.TextViewComponent) },
    profile: 'code',
};

/** Every format, any order (the priority decides). */
export const FORMATS: readonly FileFormat[] = [IMAGE_FORMAT, VIDEO_FORMAT, AUDIO_FORMAT, TEXT_FORMAT];

/**
 * How `item` is shown, or null when it isn't (downloaded, or opened in a tab). `asText` shows any file as text
 * ("Open as text"): the server refuses what isn't.
 */
export function formatOf(item: LibraryItem | null | undefined, options?: { asText?: boolean }): FileFormat | null {
    if (!item || item.kind === 'directory')
        return null;
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
