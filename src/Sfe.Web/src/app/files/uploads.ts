import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { Api, ApiError } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { plural } from '../core/format';
import { ConflictChoice, isFileConflict, LibraryItem, UploadInfo } from '../core/models';
import { baseName } from '../core/paths';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { currentPosition } from '../library/items/listing-position';
import { ListingStore } from '../library/listing.store';

/** Bytes per chunk: well under the server's 16 MB and the usual proxy limits once raised from their defaults. */
export const CHUNK_BYTES = 8 * 1024 * 1024;
/** The smallest chunk tried when a proxy refuses large requests (413). */
const MIN_CHUNK_BYTES = 256 * 1024;
/** Files sent at once. */
const PARALLEL = 2;
/** Tries of a chunk before the file is marked failed (with Retry), waiting longer each time. */
const TRIES = 5;
const RETRY_DELAY = 1000;
/** Names asked about in one check (the server's limit). */
const CHECK_BATCH = 1000;

export type UploadState = 'asking' | 'queued' | 'uploading' | 'paused' | 'done' | 'skipped' | 'failed' | 'cancelled' | 'interrupted';

/** A file to upload, picked or dropped: its name is a path below the folder for a folder upload. */
export interface PickedFile {
    file: File;
    name: string;
}

/** One file of an upload batch. */
export interface UploadItem {
    key: number;
    /** Null for an upload interrupted by a reload, until the file is picked or dropped again. */
    file: File | null;
    name: string;
    size: number;
    modified: number | null;
    id: string | null;
    sent: number;
    state: UploadState;
    /** The name is taken there (from the check, or at the finish). */
    exists: boolean;
    /** The answer about the name taken. */
    choice: ConflictChoice;
    message: string | null;
    tries: number;
}

/** Files uploaded together into a folder. */
export interface UploadBatch {
    key: number;
    target: string;
    items: UploadItem[];
    /** The session may replace files there (they go to the trash). */
    canReplace: boolean;
    started: number;
    /** Bytes sent in this page, for the speed. */
    sentHere: number;
}

/**
 * Uploads (agents/docs/features/uploads.md): files and folders picked or dropped from the computer are checked for
 * names taken (asked once, with "the same for the rest"), started on the server and sent in chunks, two files at a
 * time, with pause, retry and cancel. An upload cut off by a reload is listed again from the server ("interrupted"):
 * picking or dropping the same file (name, size, date) goes on where it stopped.
 */
@Injectable({ providedIn: 'root' })
export class UploadQueue {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);

    /** The batches shown in the operations panel, newest first. */
    readonly batches = signal<UploadBatch[]>([]);

    private nextKey = 0;
    /** Halved when a proxy in front of the server refuses a chunk as too large, for the rest of the page's uploads. */
    private chunkBytes = CHUNK_BYTES;
    private running = 0;
    private readonly aborts = new Map<number, AbortController>();
    private resumed = false;

    constructor() {
        const warn = (event: BeforeUnloadEvent) => {
            if (this.busy()) {
                event.preventDefault();
                event.returnValue = '';
            }
        };
        window.addEventListener('beforeunload', warn);
        inject(DestroyRef).onDestroy(() => window.removeEventListener('beforeunload', warn));
    }

    /** The session may upload into the folder listed (`folder` of it, when given). */
    canUploadInto(folder?: LibraryItem | null): boolean {
        if (!this.config.can(Rights.uploadFiles))
            return false;
        if (folder)
            return this.store.can(Rights.uploadFiles, folder);
        return !this.store.searching() && !this.store.folderAccess()?.pathOnly && this.store.can(Rights.uploadFiles);
    }

    /** Files are being sent or wait: leaving the page would stop them. */
    busy(): boolean {
        return this.batches().some(batch => batch.items.some(item => ['asking', 'queued', 'uploading'].includes(item.state)));
    }

    /** Lists the uploads a reload interrupted, once per page. */
    async resume(): Promise<void> {
        if (this.resumed || !this.config.can(Rights.uploadFiles))
            return;
        this.resumed = true;
        let unfinished: UploadInfo[];
        try {
            unfinished = await this.api.uploads();
        } catch {
            return;
        }
        const byTarget = new Map<string, UploadInfo[]>();
        for (const upload of unfinished)
            byTarget.set(upload.target, [...byTarget.get(upload.target) ?? [], upload]);
        const batches = [...byTarget].map(([target, uploads]) => this.batch(target, uploads.map(upload => ({
            ...this.item(null, upload.name, upload.size, upload.modified ? Date.parse(upload.modified) : null),
            id: upload.id, sent: upload.received, state: 'interrupted' as const,
        }))));
        if (batches.length)
            this.batches.update(list => [...list, ...batches]);
    }

    /** Uploads `files` into the folder `target`: names taken are asked about first. */
    async add(files: PickedFile[], target: string): Promise<void> {
        if (!files.length)
            return;
        const fresh: UploadItem[] = [];
        const max = (this.config.config()?.files?.maxUploadMegabytes ?? 0) * 1024 * 1024;
        const tooLarge = max ? files.filter(picked => picked.file.size > max) : [];
        if (tooLarge.length) {
            this.toasts.error(`${tooLarge.length === 1 ? `“${baseName(tooLarge[0].name)}” is` : `${plural(tooLarge.length, 'file')} are`} larger than the ${max / 1024 / 1024} MB a file may have here: left out.`, 8000);
            files = files.filter(picked => !tooLarge.includes(picked));
        }
        for (const picked of files) {
            // The same file as an interrupted upload: it goes on there.
            const interrupted = this.findInterrupted(target, picked);
            if (interrupted) {
                interrupted.file = picked.file;
                interrupted.state = 'queued';
                interrupted.message = null;
            } else {
                fresh.push(this.item(picked.file, picked.name, picked.file.size, picked.file.lastModified || null));
            }
        }
        if (fresh.length) {
            const batch = this.batch(target, fresh);
            this.batches.update(list => [batch, ...list]);
            try {
                for (let i = 0; i < fresh.length; i += CHECK_BATCH) {
                    const check = await this.api.checkUploads(target, fresh.slice(i, i + CHECK_BATCH).map(item => item.name));
                    batch.canReplace = check.canReplace;
                    const taken = new Set(check.existing);
                    for (const item of fresh.slice(i, i + CHECK_BATCH))
                        item.exists = taken.has(item.name);
                }
            } catch (error) {
                for (const item of fresh)
                    this.fail(item, (error as Error).message);
                this.changed();
                return;
            }
            for (const item of fresh)
                item.state = item.exists ? 'asking' : 'queued';
        }
        this.changed();
        this.pump();
    }

    /** The item a batch asks about now (the first name taken not answered yet). */
    question(batch: UploadBatch): UploadItem | null {
        return batch.items.find(item => item.state === 'asking') ?? null;
    }

    /** Answers the question about `item`'s name; `forAll`: the same for the other names taken in its batch. */
    answer(batch: UploadBatch, item: UploadItem, choice: Exclude<ConflictChoice, 'ask'>, forAll: boolean): void {
        for (const asked of forAll ? batch.items.filter(i => i.state === 'asking') : [item]) {
            asked.choice = choice;
            if (choice === 'skip' && !asked.id)
                asked.state = 'skipped';
            else
                asked.state = 'queued';
        }
        this.changed();
        this.pump();
    }

    pause(item: UploadItem): void {
        if (item.state === 'queued' || item.state === 'uploading') {
            item.state = 'paused';
            this.aborts.get(item.key)?.abort();
            this.changed();
        }
    }

    resumeItem(item: UploadItem): void {
        if (item.state === 'paused' || item.state === 'failed') {
            item.state = 'queued';
            item.tries = 0;
            item.message = null;
            this.changed();
            this.pump();
        }
    }

    pauseAll(batch: UploadBatch): void {
        batch.items.forEach(item => this.pause(item));
    }

    resumeAll(batch: UploadBatch): void {
        batch.items.forEach(item => this.resumeItem(item));
    }

    async cancel(item: UploadItem): Promise<void> {
        if (['done', 'skipped', 'cancelled'].includes(item.state))
            return;
        item.state = 'cancelled';
        this.aborts.get(item.key)?.abort();
        this.changed();
        if (item.id) {
            try {
                await this.api.cancelUpload(item.id);
            } catch {
                // Gone already: nothing to remove.
            }
        }
    }

    async cancelAll(batch: UploadBatch): Promise<void> {
        await Promise.all(batch.items.map(item => this.cancel(item)));
    }

    /** Closes a batch in the panel (its unfinished uploads are cancelled). */
    async dismiss(batch: UploadBatch): Promise<void> {
        await this.cancelAll(batch);
        this.batches.update(list => list.filter(b => b !== batch));
    }

    /** Sends the queued files, `PARALLEL` at a time. */
    private pump(): void {
        while (this.running < PARALLEL) {
            const next = this.next();
            if (!next)
                return;
            this.running++;
            next.item.state = 'uploading';
            void this.upload(next.batch, next.item).finally(() => {
                this.running--;
                this.pump();
            });
        }
    }

    private next(): { batch: UploadBatch; item: UploadItem } | null {
        // Oldest batch first: the files of one folder go up together.
        for (const batch of [...this.batches()].reverse()) {
            const item = batch.items.find(i => i.state === 'queued' && i.file);
            if (item)
                return { batch, item };
        }
        return null;
    }

    private async upload(batch: UploadBatch, item: UploadItem): Promise<void> {
        try {
            if (!item.id) {
                const started = await this.api.startUpload({
                    target: batch.target, name: item.name, size: item.size, modified: item.modified ? new Date(item.modified).toISOString() : null,
                });
                item.id = started.id;
            }
            while (item.sent < item.size && item.state === 'uploading') {
                const abort = new AbortController();
                this.aborts.set(item.key, abort);
                const before = item.sent;
                try {
                    const upload = await this.api.sendUploadChunk(item.id, item.sent, item.file!.slice(item.sent, item.sent + this.chunkBytes), abort.signal);
                    item.sent = upload.received;
                    item.tries = 0;
                } catch (error) {
                    if ((item.state as UploadState) !== 'uploading')
                        return;
                    if (error instanceof ApiError && error.status === 404) {
                        // Removed meanwhile (cancelled elsewhere, kept too long): start again.
                        item.id = null;
                        item.sent = 0;
                        return this.upload(batch, item);
                    }
                    if (error instanceof ApiError && error.status === 413 && this.chunkBytes > MIN_CHUNK_BYTES) {
                        this.chunkBytes = Math.max(MIN_CHUNK_BYTES, this.chunkBytes / 2);
                        continue;
                    }
                    if (++item.tries >= TRIES || (error instanceof ApiError && error.status === 400))
                        throw error;
                    await delay(RETRY_DELAY * 2 ** (item.tries - 1));
                } finally {
                    this.aborts.delete(item.key);
                }
                batch.sentHere += Math.max(0, item.sent - before);
                this.changed();
            }
            if ((item.state as UploadState) !== 'uploading')
                return;
            await this.finish(batch, item);
        } catch (error) {
            if ((item.state as UploadState) === 'uploading')
                this.fail(item, (error as Error).message);
        } finally {
            this.changed();
        }
    }

    private async finish(batch: UploadBatch, item: UploadItem): Promise<void> {
        const result = await this.api.finishUpload(item.id!, item.choice);
        if (result === null) {
            item.state = 'skipped';
        } else if (isFileConflict(result)) {
            if (result.type === 'file-exists') {
                // Taken since the check: asked now.
                item.exists = true;
                item.state = 'asking';
            } else {
                this.fail(item, result.detail ?? 'It could not be saved.');
            }
        } else {
            item.state = 'done';
            this.added(batch, item, result);
        }
        this.ended(batch);
    }

    /** The listing shows a file uploaded into the folder listed (the first folder of a folder upload). */
    private added(batch: UploadBatch, item: UploadItem, file: LibraryItem): void {
        const route = this.store.route();
        if (route.q || route.path !== batch.target)
            return;
        if (!item.name.includes('/'))
            this.store.applyAdded(file);
    }

    /** A batch whose files are all done: the listing is loaded again for its new folders, a toast says it. */
    private ended(batch: UploadBatch): void {
        if (batch.items.some(item => ['asking', 'queued', 'uploading', 'paused'].includes(item.state)))
            return;
        const done = batch.items.filter(item => item.state === 'done');
        const route = this.store.route();
        if (done.some(item => item.name.includes('/')) && !route.q && route.path === batch.target)
            void this.store.load({ ...currentPosition(), loaded: this.store.items().length + 1 });
        if (done.length)
            this.toasts.show(`${plural(done.length, 'file')} uploaded to “${baseName(batch.target) || this.config.config()?.rootName || 'the library'}”.`);
    }

    private fail(item: UploadItem, message: string): void {
        item.state = 'failed';
        item.message = message;
    }

    private findInterrupted(target: string, picked: PickedFile): UploadItem | null {
        for (const batch of this.batches()) {
            if (batch.target !== target)
                continue;
            const match = batch.items.find(item => item.state === 'interrupted' && item.name === picked.name && item.size === picked.file.size
                && (item.modified === null || !picked.file.lastModified || Math.abs(item.modified - picked.file.lastModified) < 2000));
            if (match)
                return match;
        }
        return null;
    }

    private item(file: File | null, name: string, size: number, modified: number | null): UploadItem {
        return { key: ++this.nextKey, file, name, size, modified, id: null, sent: 0, state: 'queued', exists: false, choice: 'ask', message: null, tries: 0 };
    }

    private batch(target: string, items: UploadItem[]): UploadBatch {
        return { key: ++this.nextKey, target, items, canReplace: false, started: Date.now(), sentHere: 0 };
    }

    /** The objects changed in place: a new list, for the panel. */
    private changed(): void {
        this.batches.update(list => [...list]);
    }
}

function delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
}

/**
 * The files of a drop, folders walked (their files named by their path below the drop): from `DataTransferItem`s
 * where the browser can read folders, else the plain file list.
 */
export async function droppedFiles(data: DataTransfer): Promise<PickedFile[]> {
    const entries = [...data.items ?? []].map(item => item.kind === 'file' ? item.webkitGetAsEntry?.() ?? null : null);
    if (!entries.length || entries.some(entry => entry === null))
        return [...data.files].map(file => ({ file, name: file.name }));
    const files: PickedFile[] = [];
    for (const entry of entries)
        await walk(entry!, '', files);
    return files;
}

async function walk(entry: FileSystemEntry, prefix: string, files: PickedFile[]): Promise<void> {
    const name = prefix + entry.name;
    if (entry.isFile) {
        const file = await new Promise<File>((resolve, reject) => (entry as FileSystemFileEntry).file(resolve, reject));
        files.push({ file, name });
        return;
    }
    const reader = (entry as FileSystemDirectoryEntry).createReader();
    // readEntries answers a hundred or so at a time, then an empty list.
    for (;;) {
        const batch = await new Promise<FileSystemEntry[]>((resolve, reject) => reader.readEntries(resolve, reject));
        if (!batch.length)
            break;
        for (const child of batch)
            await walk(child, `${name}/`, files);
    }
}

/** The files of an `<input type="file">`, named by their path below the folder picked for a folder upload. */
export function pickedFiles(input: HTMLInputElement): PickedFile[] {
    return [...input.files ?? []].map(file => ({ file, name: file.webkitRelativePath || file.name }));
}
