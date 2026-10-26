import { inject, InjectionToken } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { LibraryItem, TextFile } from '../core/models';

/**
 * Where a format view reads the file it shows: the library (the default), or an entry of an archive shown in the
 * archive view (agents/docs/features/archive-viewer.md). An entry is read only and read whole: no parts, no Follow, no
 * save, no links to other files.
 */
export interface FileSource {
    /** A file of the library: everything a view offers; false for an entry of an archive. */
    readonly library: boolean;
    /** The text of the file, decoded as `api/files/text` does (`encoding`: read in that one). */
    textFile(item: LibraryItem, signal?: AbortSignal, encoding?: string | null): Promise<TextFile>;
    /** The file's bytes (pdf.js, pictures). */
    original(item: LibraryItem): string;
    /** The address saving it, or null. */
    download(item: LibraryItem): string | null;
    /**
     * Where a document's relative links and pictures lead, when not into the library: `path` is the document's own
     * path among its neighbours (its archive's folders), null when unknown (they are text then).
     */
    readonly documents?: { path: string | null; links: DocumentLinks };
}

/** The addresses of a document's links (paths as `path` resolves them: `/`-separated, among its neighbours). */
export interface DocumentLinks {
    /** The file itself (pictures, files without a viewer). */
    file(path: string): string;
    /** The file in its viewer. */
    view(path: string): string;
    /** A folder. */
    folder(path: string): string;
    /** The file at `path` is shown by a viewer (a click opens it there, through `FileViewHost.open`). */
    viewable(path: string): boolean;
}

export const FILE_SOURCE = new InjectionToken<FileSource>('FILE_SOURCE', {
    providedIn: 'root',
    factory: (): FileSource => {
        const api = inject(Api);
        const config = inject(ConfigService);
        return {
            library: true,
            // As the views asked before: no encoding argument unless one is chosen.
            textFile: (item, signal, encoding) => encoding === undefined ? api.textFile(item.path, signal) : api.textFile(item.path, signal, encoding),
            original: item => config.urls()!.original(item.path),
            download: item => config.urls()?.download(item.path) ?? null,
        };
    },
});
