import { computed, DestroyRef, inject, Injectable, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { plural } from '../core/format';
import { CompressRequest, ConflictChoice, ExtractRequest, FileJobItem, FileJobKind, FileJobRequest, FileJobView, LibraryItem } from '../core/models';
import { baseName, parentPath } from '../core/paths';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { isExtractable } from '../formats/formats';
import { Dialogs } from '../library/dialogs.service';
import { currentPosition } from '../library/items/listing-position';
import { ListingStore } from '../library/listing.store';

/** How often (ms) running jobs are asked for. */
const POLL = 700;

/** Items cut or copied (Ctrl+X, Ctrl+C), pasted into a folder by Ctrl+V or the menu. */
export interface FileClipboard {
    kind: FileJobKind;
    items: LibraryItem[];
}

/**
 * Moves and copies (agents/docs/features/move-and-copy.md): starts them on the server, where each is a job running in
 * the background, follows those of the session for the operations panel, answers their questions and updates the
 * listing when they end. The clipboard of Ctrl+X, Ctrl+C and Ctrl+V lives here too.
 */
@Injectable({ providedIn: 'root' })
export class FileTransfers {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);
    private readonly dialogs = inject(Dialogs);
    /** The extractions this page started, by job: one failing on its password is asked again as it was (never the password). */
    private readonly extractions = new Map<number, ExtractRequest>();

    /** The session's jobs shown in the operations panel, newest first. */
    readonly jobs = signal<FileJobView[]>([]);
    readonly clipboard = signal<FileClipboard | null>(null);
    /** The paths cut (Ctrl+X), shown faded until pasted. */
    readonly cutPaths = computed<ReadonlySet<string>>(() => {
        const clipboard = this.clipboard();
        return new Set(clipboard?.kind === 'move' ? clipboard.items.map(item => item.path) : []);
    });

    /** Jobs closed in the panel. */
    private readonly dismissed = new Set<number>();
    /** Jobs seen running: their end updates the listing once. */
    private readonly following = new Set<number>();
    private timer: ReturnType<typeof setTimeout> | undefined;

    constructor() {
        inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
    }

    /** Follows the session's jobs still running (started before the page was loaded). */
    resume(): void {
        if ([Rights.moveFiles, Rights.copyFiles, Rights.extractFiles, Rights.compressFiles].some(right => this.config.can(right)))
            this.poll(0);
    }

    /** May use `right` on `item` and in the folder listed (moves change it). */
    private may(right: string, item: LibraryItem): boolean {
        return this.store.can(right) && this.store.can(right, item);
    }

    canMove(item: LibraryItem): boolean {
        return this.may(Rights.moveFiles, item);
    }

    /** Copies only open what they copy: a read-only folder's files can be copied elsewhere. */
    canCopy(item: LibraryItem): boolean {
        return this.config.can(Rights.copyFiles) && this.store.can(Rights.viewFiles, item);
    }

    /**
     * May extract the archive `item` somewhere: its content is opened (view, download) and written where one may
     * extract (checked when the folder is picked, and by the server).
     */
    canExtract(item: LibraryItem): boolean {
        return isExtractable(item) && this.config.can(Rights.extractFiles) && this.store.can(Rights.viewFiles, item) && this.store.can(Rights.download, item);
    }

    /** May extract `item` beside it, in the folder listed (not in search results, whose folders differ). */
    canExtractBeside(item: LibraryItem): boolean {
        return this.canExtract(item) && !this.store.searching() && this.store.can(Rights.extractFiles) && !this.store.folderAccess()?.pathOnly;
    }

    /** May put `item` in a zip: it is read (view, download); the zip goes where one may compress (the folder listed). */
    canCompress(item: LibraryItem): boolean {
        return this.config.can(Rights.compressFiles) && this.store.can(Rights.viewFiles, item) && this.store.can(Rights.download, item);
    }

    /** The folder a zip of `items` goes into: the one listed, or in search results the first item's. */
    compressTarget(items: LibraryItem[]): string {
        return this.store.route().q ? parentPath(items[0]?.path ?? '') : this.store.route().path;
    }

    /** Starts a compression and follows it. */
    compress(request: CompressRequest): Promise<FileJobView | null> {
        return this.follow(() => this.api.startCompress(request), 'compress');
    }

    /** Starts an extraction and follows it. */
    async extract(request: ExtractRequest): Promise<FileJobView | null> {
        const view = await this.follow(() => this.api.startExtract(request), 'extract');
        if (view)
            this.extractions.set(view.id, { ...request, password: null });
        return view;
    }

    /** The extraction `job` failed on its password, and this page started it: it can be asked again with one. */
    canRetryExtraction(job: FileJobView): boolean {
        return job.kind === 'extract' && job.results[0]?.status === 'needsPassword' && this.extractions.has(job.id) && Boolean(this.dialogs.extract());
    }

    /** Extract… again, as `job` was asked (archive, entries chosen, folder, new folder or not), for its password. */
    retryExtraction(job: FileJobView): void {
        const request = this.extractions.get(job.id);
        if (!request?.archive)
            return;
        const chosen = (request.folders?.length ?? 0) + (request.files?.length ?? 0);
        this.dialogs.extract()?.open({
            path: request.archive, name: baseName(request.archive), folders: request.folders, files: request.files,
            what: chosen ? plural(chosen, 'item') : undefined, target: request.target, newFolder: request.newFolder,
        });
    }

    /** Several archives may each be extracted beside them (all of them: one that may not, and none is offered). */
    canExtractEach(items: LibraryItem[]): boolean {
        return items.length > 1 && items.every(item => this.canExtractBeside(item));
    }

    /** Each archive into a new folder named after it, in the folder listed: one job each, in the panel. */
    async extractEach(items: LibraryItem[]): Promise<void> {
        const target = this.store.route().path;
        for (const item of items)
            await this.extract({ archive: item.path, target, newFolder: true });
    }

    /** The space lets the session move (or copy) somewhere. */
    allows(kind: FileJobKind): boolean {
        return this.config.can(kind === 'move' ? Rights.moveFiles : Rights.copyFiles);
    }

    /** The session may move or copy into the folder listed (`folder` of it, when given). */
    canInto(kind: FileJobKind, folder?: LibraryItem | null): boolean {
        if (this.store.route().q && !folder)
            return false;
        const right = kind === 'move' ? Rights.moveFiles : Rights.copyFiles;
        return folder ? this.store.can(right, folder) && this.config.can(right) : this.store.can(right) && !this.store.folderAccess()?.pathOnly;
    }

    /** Asks where to move or copy `items` (those the session may), then does it. False when none may. */
    pick(kind: FileJobKind, items: LibraryItem[]): boolean {
        const allowed = items.filter(item => kind === 'move' ? this.canMove(item) : this.canCopy(item));
        if (!allowed.length)
            return false;
        this.dialogs.transfer()?.pick(kind, allowed);
        return true;
    }

    /** Ctrl+X, Ctrl+C: keeps the items for a paste. False when none may be moved or copied. */
    keep(kind: FileJobKind, items: LibraryItem[]): boolean {
        const allowed = items.filter(item => kind === 'move' ? this.canMove(item) : this.canCopy(item));
        if (!allowed.length)
            return false;
        this.clipboard.set({ kind, items: allowed });
        this.toasts.show(`${plural(allowed.length, 'item')} ${kind === 'move' ? 'cut' : 'copied'}: paste them into a folder with Ctrl+V or its menu.`);
        return true;
    }

    /** Ctrl+V, Paste: what was cut or copied goes into `target` (a cut, once). False when there is nothing to paste. */
    paste(target: string): boolean {
        const clipboard = this.clipboard();
        if (!clipboard)
            return false;
        if (clipboard.kind === 'move')
            this.clipboard.set(null);
        void this.transfer(clipboard.kind, clipboard.items.map(item => item.path), target);
        return true;
    }

    /**
     * Moves or copies `paths` into `target` without picking a folder (paste, drop): a copy asks about the tags, a move
     * whether to keep the access of folders that would be seen by more people there.
     */
    async transfer(kind: FileJobKind, paths: string[], target: string): Promise<void> {
        if (kind === 'copy') {
            this.dialogs.transfer()?.ask({ kind, paths, target }, []);
            return;
        }
        const gains = await this.gains(paths, target);
        if (gains === null)
            return;
        if (gains.length)
            this.dialogs.transfer()?.ask({ kind, paths, target }, gains);
        else
            await this.start({ kind, paths, target });
    }

    /** Who would gain access to folders among `paths` moved into `target`; null when it could not be asked. */
    async gains(paths: string[], target: string) {
        try {
            return (await this.api.checkMove(paths, target)).gains;
        } catch (error) {
            this.toasts.error(`Could not move: ${(error as Error).message}`);
            return null;
        }
    }

    /** Starts a job and follows it. */
    start(request: FileJobRequest): Promise<FileJobView | null> {
        return this.follow(() => this.api.startFileJob(request), request.kind);
    }

    private async follow(started: () => Promise<FileJobView>, kind: FileJobKind): Promise<FileJobView | null> {
        let job: FileJobView;
        try {
            job = await started();
        } catch (error) {
            this.toasts.error(`Could not ${kind}: ${(error as Error).message}`);
            return null;
        }
        this.following.add(job.id);
        this.jobs.update(jobs => [job, ...jobs.filter(j => j.id !== job.id)]);
        this.poll(0);
        return job;
    }

    async answer(job: FileJobView, choice: ConflictChoice, forAll: boolean): Promise<void> {
        try {
            await this.api.answerFileJob(job.id, choice, forAll);
        } catch (error) {
            this.toasts.error((error as Error).message);
        }
        this.replace({ ...job, conflict: null, state: 'running' });
        this.poll(0);
    }

    async cancel(job: FileJobView): Promise<void> {
        try {
            await this.api.cancelFileJob(job.id);
        } catch (error) {
            this.toasts.error((error as Error).message);
        }
        this.poll(0);
    }

    /** Closes a finished job in the panel. */
    dismiss(job: FileJobView): void {
        this.dismissed.add(job.id);
        this.jobs.update(jobs => jobs.filter(j => j.id !== job.id));
    }

    private replace(job: FileJobView): void {
        this.jobs.update(jobs => jobs.map(j => j.id === job.id ? job : j));
    }

    /** Asks for the jobs while some of those followed run. */
    private poll(delay = POLL): void {
        clearTimeout(this.timer);
        this.timer = setTimeout(() => void this.refresh(), delay);
    }

    private async refresh(): Promise<void> {
        let jobs: FileJobView[];
        try {
            jobs = await this.api.fileJobs();
        } catch {
            this.poll(POLL * 4);
            return;
        }
        this.jobs.set(jobs.filter(job => !this.dismissed.has(job.id) && (this.following.has(job.id) || !finished(job))));
        for (const job of jobs) {
            if (!finished(job))
                this.following.add(job.id);
            else if (this.following.delete(job.id))
                this.ended(job);
        }
        if (jobs.some(job => !finished(job)))
            this.poll();
    }

    /** Undo of a move: the items go back into the folders they came from. */
    private undoMove(done: FileJobItem[]): void {
        for (const [origin, paths] of groupByOrigin(done))
            void this.transfer('move', paths, origin);
    }

    /** A compression has ended: its toast says the ratio and shows the zip in its folder. */
    private compressed(job: FileJobView, where: string): void {
        const result = job.results[0];
        if (job.state === 'cancelled') {
            this.toasts.show(`Stopped: no zip was made in “${where}”.`);
            return;
        }
        if (!result || result.status !== 'done' || !result.newPath) {
            this.toasts.error(result?.message ?? 'The zip could not be made.', 8000);
            return;
        }
        const zip = result.newPath;
        this.toasts.show(`“${baseName(zip)}” made in “${where}”. ${result.message ?? ''}`.trim(), {
            timeout: 8000,
            actions: [{ label: 'Show', title: `Show ${baseName(zip)} in its folder`, run: () => this.store.reveal(zip) }],
        });
    }

    /** An extraction has ended: its toast opens what came out (the new folder, or the folder it went into). */
    private extracted(job: FileJobView, where: string): void {
        const result = job.results[0];
        if (job.state === 'cancelled') {
            this.toasts.show(`Stopped: nothing was extracted to “${where}”.`);
            return;
        }
        if (this.canRetryExtraction(job)) {
            this.toasts.show(result.message ?? 'It is protected by a password.', {
                error: true, timeout: 12000,
                actions: [{ label: 'Enter the password', title: 'Extract it again, with its password', run: () => this.retryExtraction(job) }],
            });
            return;
        }
        if (!result || result.status !== 'done') {
            this.toasts.error(result?.message ?? `“${baseName(job.target)}” could not be extracted.`, 8000);
            return;
        }
        const opened = result.newPath && result.newPath !== job.target ? result.newPath : job.target;
        const actions = opened !== this.store.route().path
            ? [{ label: 'Open', title: `Open ${baseName(opened) || where}`, run: () => this.store.navigate({ path: opened }) }]
            : [];
        this.toasts.show(`“${baseName(result.path)}” extracted to “${baseName(opened) || where}”. ${result.message ?? ''}`.trim(), { timeout: 8000, actions });
    }

    /** A job followed has ended: the listing shows its result, a toast says it. */
    private ended(job: FileJobView): void {
        const done = job.results.filter(result => result.status === 'done');
        const route = this.store.route();
        const listed = route.path;
        if (job.kind === 'move' && !route.q)
            this.store.applyRemoved(done.filter(result => parentPath(result.path) === listed && parentPath(result.newPath ?? '') !== listed).map(result => result.path));
        if (done.length && (route.q || job.target === listed))
            void this.store.load({ ...currentPosition(), loaded: this.store.items().length + done.length });

        const verb = pastOf(job.kind);
        const where = baseName(job.target) || this.config.config()?.rootName || 'the library';
        if (job.kind === 'extract') {
            this.extracted(job, where);
            return;
        }
        if (job.kind === 'compress') {
            this.compressed(job, where);
            return;
        }
        if (job.state === 'cancelled')
            this.toasts.show(`Stopped: ${plural(done.length, 'item')} ${verb} to “${where}” before.`);
        else if (done.length) {
            const actions = [];
            if (job.kind === 'move')
                actions.push({ label: 'Undo', title: 'Move them back where they were', run: () => this.undoMove(done) });
            if (job.target !== listed)
                actions.push({ label: 'Open', title: `Open ${where}`, run: () => this.store.navigate({ path: job.target }) });
            this.toasts.show(`${plural(done.length, 'item')} ${verb} to “${where}”.`, { timeout: 8000, actions });
        }
        const failed = job.results.filter(result => result.status !== 'done' && result.status !== 'skipped');
        if (failed.length)
            this.toasts.error(failed.length === 1
                ? failed[0].message ?? `“${baseName(failed[0].path)}” could not be ${verb}.`
                : `${plural(failed.length, 'item')} could not be ${verb}: ${failed[0].message ?? baseName(failed[0].path)}`, 8000);
    }
}

/** What a job did, in the past: moved, copied, extracted. */
export function pastOf(kind: FileJobKind): string {
    return kind === 'move' ? 'moved' : kind === 'copy' ? 'copied' : kind === 'extract' ? 'extracted' : 'compressed';
}

/** The moves `done` undone: each item back into the folder it came from (one job per folder), asking about names taken there. */
function groupByOrigin(done: FileJobItem[]): Map<string, string[]> {
    const groups = new Map<string, string[]>();
    for (const result of done) {
        if (!result.newPath)
            continue;
        const origin = parentPath(result.path);
        groups.set(origin, [...groups.get(origin) ?? [], result.newPath]);
    }
    return groups;
}

export function finished(job: FileJobView): boolean {
    return job.state === 'completed' || job.state === 'cancelled';
}
