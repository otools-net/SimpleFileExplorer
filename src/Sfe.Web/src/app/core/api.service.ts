import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable, Injector } from '@angular/core';
import { Router } from '@angular/router';
import { Observable } from 'rxjs';
import {
    AccessGain, ArchiveAnswer, ArchiveSearchAnswer, ArchiveLink, ArchiveUnlockAnswer, TextFile, TextPart, TextVersion, SubtitleVideo, SaveTextResult, TextToSave, BackupLink, ChecksumAlgorithm, FolderTotals, ItemProperties, PropertyJob, ClientConfig, ConflictChoice, DeleteResults, CompressRequest, ExtractRequest, FileJobRequest, FileJobView, UploadCheck, UploadInfo, FileOperationResult, TrashPage, TrashCount, KeepAliveView, DirectoryListing, EditedImage, EditedImageAccess, SaveImageResult, DuplicateResults, FileLink, FileTagChange, FileTagChangeResult, FileTags,
    ExcludedFolder, ExclusionKind, FolderAccessReport, FolderDefaults, FolderDefaultTagsChange, FolderDefaultTagsResult, FolderCover, FolderExclusions, FolderPicker, FolderRuleChange, FolderRuleView, FolderSiblings, GroupInfo, GroupInput, IndexedFolder, InstallationGrant, InstallationInput, InstallationSaved,
    InstallationView, LogQuery, LogSearchResult, MigrationInfo, MissingFileResults, QuickLink, RelinkPassResult, RemovedDuplicate,
    RemovedFromIndex, RightsCatalog, ScanStatus, ScopeTagChange, ScopeTagChangeResult, ScopeTags, SearchResults, SessionInfo, SettingChanges, SettingLevel,
    SettingsValues, SettingsView, SetupInfo, SetupInput, SimilarVideoResults, SortField, TagDetails, TagInput, TagScope, TagSearchResults, TagSidebar,
    TaskActionResult, TaskInfo, TaskKind, TaskOverview, TaskRunDetails, TaskRunPage, TaskSchedule, TaskScheduleInput, TenantAccess, TenantDetails,
    TenantInput, StreamingStatus, StreamProbe, SystemInfo, TrickplayActivity, TrickplayManifest, TrickplayStates, TrickplayStatus, UserInfo, UserInput,
} from './models';
import { encodePath } from './paths';

/** A failed request: the message of the problem response, and its validation messages by field. */
/** This tab, for the change feed: its own changes are not told back to it. */
export const TAB_ID = Math.random().toString(36).slice(2, 12);

/**
 * An editing session id, as the server wants it (letters, digits): the previous version of a file saved over goes to
 * the trash at the first save of a session. crypto.randomUUID needs a secure context.
 */
export function editingSession(): string {
    const bytes = crypto.getRandomValues(new Uint8Array(16));
    return Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
}

export class ApiError extends Error {
    /** @param type The problem's `type` (`setup-required`, `tenant-changed`...), when it has one. */
    constructor(readonly status: number, message: string, readonly errors: Record<string, string[]> | null = null, readonly type: string | null = null) {
        super(message);
    }
}

type QueryValue = string | number | boolean | null | undefined;

/** Leaves out empty values (and `false`), as the API's defaults apply then. */
function params(values: Record<string, QueryValue>): HttpParams {
    let result = new HttpParams();
    for (const [key, value] of Object.entries(values)) {
        if (value !== undefined && value !== null && value !== '' && value !== false)
            result = result.set(key, String(value));
    }
    return result;
}

/** `url` with the query of `values` (for requests with a body), nothing added when they are all empty. */
function withQuery(url: string, values: Record<string, QueryValue>): string {
    const query = params(values).toString();
    return query ? `${url}?${query}` : url;
}

/**
 * The JSON API of the server. URLs are relative, so the app also works under a virtual directory. Requests resolve
 * with the body; `signal` (AbortSignal) cancels a request, and the work on the server, e.g. when the user navigates
 * on. A 409 (conflict) resolves with its body: the API answers with the current state then.
 */
@Injectable({ providedIn: 'root' })
export class Api {
    private readonly http = inject(HttpClient);
    /** The router, asked for when a redirect is due (it needs the Api itself, through the pages). */
    private readonly injector = inject(Injector);

    config = () => this.get<ClientConfig>('api/config');

    browse = (query: { path: string; offset: number; limit: number; sort: SortField; desc: boolean }, signal?: AbortSignal) =>
        this.get<DirectoryListing>('api/browse', query, signal);
    /** The folders around `path` in its parent, in the given sort. */
    siblings = (query: { path: string; sort: SortField; desc: boolean }, signal?: AbortSignal) =>
        this.get<FolderSiblings>('api/browse/siblings', query, signal);
    /** The first pictures of each folder of `paths` that exists (folder cards). */
    folderCovers = (paths: string[], signal?: AbortSignal) => this.post<FolderCover[]>('api/browse/covers', { paths }, signal);
    /** `after`: the `next` value of the previous page. */
    search = (query: { q: string; path: string; after: string | null; limit: number; sort: SortField; desc: boolean }, signal?: AbortSignal) =>
        this.get<SearchResults>('api/search', query, signal);

    scanStatus = () => this.get<ScanStatus>('api/scan');
    startScan = (path: string) => this.post<ScanStatus>('api/scan', { path });
    cancelScan = () => this.delete<ScanStatus>('api/scan');
    /** What the index holds of a folder (counts), before removing it. */
    indexedFolder = (path: string) => this.get<IndexedFolder>('api/index/folder', { path });
    /** Removes a folder from the index (not from the disk); `exclude` also keeps future scans out of it. Resolves with the scan status (409) while a scan runs. */
    removeFromIndex = (path: string, exclude: boolean) =>
        this.delete<RemovedFromIndex | ScanStatus>(`api/index/folder?${params({ path, exclude }).toString()}`);

    /** Every folder kept out of scans, trickplay or thumbnails in the space (those the session sees). */
    folderExclusions = () => this.get<ExcludedFolder[]>('api/index/exclusions');
    /** Keeps a folder out of scans, trickplay or thumbnails (true) or lets it in again (false); absent kinds stay as they are. */
    setFolderExclusions = (path: string, changes: Partial<Record<ExclusionKind, boolean>>) =>
        this.put<FolderExclusions>('api/index/exclusions', { path, ...changes });

    /** The background tasks, their schedules and the server's clock. */
    tasks = () => this.get<TaskOverview>('api/tasks');
    /** Resolves with the result also when the task could not start (409). */
    startTask = (task: TaskKind, path: string | null = null) =>
        this.post<TaskActionResult>(`api/tasks/${task}/start?${params({ path }).toString()}`);
    /** Resolves with the task also when it wasn't running (409). */
    cancelTask = (task: TaskKind) => this.post<TaskInfo>(`api/tasks/${task}/cancel`);
    createSchedule = (schedule: TaskScheduleInput) => this.post<TaskSchedule>('api/tasks/schedules', schedule);
    updateSchedule = (id: number, schedule: TaskScheduleInput) => this.put<TaskSchedule>(`api/tasks/schedules/${id}`, schedule);
    deleteSchedule = (id: number) => this.delete<void>(`api/tasks/schedules/${id}`);
    /** The next runs of a schedule being edited; rejects with its validation errors. */
    previewSchedule = (schedule: TaskScheduleInput) => this.post<{ runs: string[] }>('api/tasks/schedules/preview', schedule);
    /** Runs newest first; `before`: the `next` value of the previous page. */
    taskRuns = (query: { task: TaskKind | null; before: number | null }) => this.get<TaskRunPage>('api/tasks/runs', query);
    taskRun = (id: number) => this.get<TaskRunDetails>(`api/tasks/runs/${id}`);
    clearTaskRuns = () => this.delete<void>('api/tasks/runs');

    logs = (query: LogQuery, signal?: AbortSignal) => this.get<LogSearchResult>('api/logs', { ...query }, signal);

    tags = (query: { q: string; limit: number }) => this.get<TagSearchResults>('api/tags', query);
    tag = (id: number) => this.get<TagDetails>(`api/tags/${id}`);
    createTag = (tag: TagInput) => this.post<TagDetails>('api/tags', tag);
    updateTag = (id: number, tag: TagInput) => this.put<TagDetails>(`api/tags/${id}`, tag);
    deleteTag = (id: number) => this.delete<void>(`api/tags/${id}`);
    fileTags = (path: string) => this.get<FileTags>('api/file-tags', { path });
    changeFileTags = (change: FileTagChange) => this.post<FileTagChangeResult>('api/file-tags', change);
    /** The tags of the files of a folder or of a search's matches, counted. */
    scopeTags = (scope: TagScope, signal?: AbortSignal) =>
        this.get<ScopeTags>('api/file-tags/scope', { path: scope.path, recursive: scope.recursive, q: scope.query }, signal);
    /** Tags every file of a folder or every match of a search. Resolves with the problem (409) while a scan runs. */
    changeScopeTags = (change: ScopeTagChange) => this.post<ScopeTagChangeResult | { detail?: string }>('api/file-tags/scope', change);
    fileViewed = (path: string) => this.post<void>('api/file-tags/viewed', { path });
    /** The default tags of a folder, its own and its parents'. */
    folderDefaultTags = (path: string, signal?: AbortSignal) => this.get<FolderDefaults>('api/folder-default-tags', { path }, signal);
    /** Changes a folder's default tags (and the files already there when asked). Resolves with the problem (409) while a scan runs. */
    changeFolderDefaultTags = (change: FolderDefaultTagsChange) =>
        this.put<FolderDefaultTagsResult | { detail?: string }>('api/folder-default-tags', change);
    tagSidebar = () => this.get<TagSidebar>('api/tags/sidebar');
    clearTagHistory = () => this.delete<void>('api/tags/history');

    /** The settings of a level: the server's (server administrators) or the space's; without one, the caller's default. */
    settings = (level?: SettingLevel) => this.get<SettingsView>('api/settings', { level });
    saveSettings = (values: SettingsValues, level?: SettingLevel) => this.put<SettingsView>(withLevel('api/settings', level), values);
    /** Saves only the settings (and locks) in `changes` at `level`: what the level has for the others stays. */
    saveSettingChanges = (changes: SettingChanges, level?: SettingLevel) => this.patch<SettingsView>(withLevel('api/settings', level), changes);
    /** Locks `keys` at `level` for the levels below; the other settings it may lock are unlocked. */
    saveSettingLocks = (keys: string[], level: SettingLevel) => this.put<SettingsView>(withLevel('api/settings/locks', level), { keys });
    resetSettings = (level?: SettingLevel) => this.delete<SettingsView>(withLevel('api/settings', level));
    /** The signed-in user's own preferences (the user level). */
    preferences = () => this.get<SettingsView>('api/me/preferences');
    savePreferences = (values: SettingsValues) => this.put<SettingsView>('api/me/preferences', values);
    /** Saves only the preferences in `changes.values`; the others keep following the space. */
    savePreferenceChanges = (changes: SettingChanges) => this.patch<SettingsView>('api/me/preferences', changes);
    resetPreferences = () => this.delete<SettingsView>('api/me/preferences');
    /** The signed-in user's quick links in the library, in order. */
    quickLinks = () => this.get<QuickLink[]>('api/me/quick-links');
    /** Adds `paths` before the link at `position` (at the end without one); links already there move. Resolves with the whole list. */
    addQuickLinks = (paths: string[], position: number | null = null) => this.post<QuickLink[]>('api/me/quick-links', { paths, position });
    removeQuickLink = (path: string) => this.delete<QuickLink[]>(withQuery('api/me/quick-links', { path }));

    /** Trickplay of a video; rejects (404) when it has none yet. */
    trickplay = (path: string) => this.get<TrickplayManifest>(`api/trickplay/files/${encodePath(path)}`);
    /** Same, without queueing a video that has none (a thumbnail's hover preview). */
    existingTrickplay = (path: string) => this.get<TrickplayManifest>(`api/trickplay/files/${encodePath(path)}`, { queue: 'false' });
    trickplayStatus = () => this.get<TrickplayStatus>('api/trickplay');
    /** Whether a picture has more than one frame (read from its structure on the server). */
    imageAnimation = (path: string) => this.get<{ animated: boolean }>(`api/files/animated/${encodePath(path)}`);
    /** Running videos, the first `limit` waiting ones and the last ones finished. */
    trickplayActivity = (limit: number) => this.get<TrickplayActivity>('api/trickplay/activity', { limit });
    /** Those of `paths` waiting or being processed. */
    trickplayStates = (paths: string[]) => this.post<TrickplayStates>('api/trickplay/states', { paths });
    /** Resolves with the status, also when trickplay is off (409). */
    checkTrickplay = () => this.post<TrickplayStatus>('api/trickplay');

    /** Groups of identical files under `path`, biggest first; `after`: the `next` value of the previous page. */
    duplicates = (query: { path: string; after: string | null }) => this.get<DuplicateResults>('api/duplicates', query);
    /** Deletes the copy at `path` after adding its tags to the identical copy `keep`. */
    removeDuplicate = (path: string, keep: string) => this.post<RemovedDuplicate>('api/duplicates/remove', { path, keep });
    /** Groups of similar videos of the last comparison, with its status. */
    similarVideos = (query: { path: string; after: string | null }) => this.get<SimilarVideoResults>('api/similar-videos', query);
    /** Starts comparing the videos (nothing while a comparison runs). */
    compareVideos = () => this.post<SimilarVideoResults['status']>('api/similar-videos');
    /** Tagged files scans no longer find, with the files that may be them. */
    missingFiles = (query: { after: number | null }) => this.get<MissingFileResults>('api/missing-files', query);
    /** Relinks the missing files that have exactly one likely new place. */
    relinkMissingFiles = () => this.post<RelinkPassResult>('api/missing-files/relink');
    relinkMissingFile = (id: number, path: string) => this.post<void>(`api/missing-files/${id}/relink`, { path });
    /** Forgets one missing file, or all of them when `id` is omitted. */
    forgetMissingFiles = (id?: number) => this.delete<void>(id === undefined ? 'api/missing-files' : `api/missing-files/${id}`);

    /** Whether `path` can be played converted by the server, and its length; 404 when it can't. */
    streamProbe = (path: string) => this.get<StreamProbe>('api/streaming/probe', { path });
    /** The videos the server converts now, in every library. */
    streamingStatus = () => this.get<StreamingStatus>('api/streaming/sessions');

    /** Where the library is on the server's disk, and the ffmpeg found. */
    systemInfo = () => this.get<SystemInfo>('api/system/info');

    /** Whether the space's library folder is kept alive on its file server, and what the last pings found. */
    keepAlive = () => this.get<KeepAliveView>('api/keep-alive');
    /** Pings the space's folders now (also while keep alive is off). */
    pingKeepAlive = () => this.post<KeepAliveView>('api/keep-alive/ping');

    /** Who is signed in; works before signing in and before the setup (never redirects). */
    session = () => this.get<SessionInfo>('api/session', {}, undefined, false);
    signIn = (userName: string, password: string, remember: boolean) =>
        this.post<SessionInfo>('api/session/login', { userName, password, remember }, undefined, false);
    signOut = () => this.post<SessionInfo>('api/session/logout', null, undefined, false);
    /** Browses `tenant` (a slug) as a visitor without an account. */
    browseAsGuest = (tenant: string) => this.post<SessionInfo>('api/session/guest', { tenant }, undefined, false);
    /** Has the server show a one-time code for `userName` where it runs (console, recovery file); same answer for any name. */
    requestRecovery = (userName: string) => this.post<{ minutes: number }>('api/session/recovery', { userName }, undefined, false);
    /** A new password with the code: unlocks the account and signs it in. */
    completeRecovery = (userName: string, code: string, password: string, remember: boolean) =>
        this.post<SessionInfo>('api/session/recovery/complete', { userName, code, password, remember }, undefined, false);
    switchTenant = (tenant: string) => this.post<SessionInfo>('api/session/tenant', { tenant }, undefined, false);
    changePassword = (current: string, password: string) => this.put<SessionInfo>('api/session/password', { current, password });
    setupInfo = () => this.get<SetupInfo>('api/setup', {}, undefined, false);
    completeSetup = (input: SetupInput) => this.post<SessionInfo>('api/setup', input, undefined, false);
    /** Checks the setup code; the token answered opens the other installation routes for an hour. */
    checkInstallation = (code: string) => this.post<InstallationGrant>('api/installation/check', { code }, undefined, false);
    installation = (token: string) => this.send(this.http.get<InstallationView>('api/installation', { headers: this.installationHeaders(token, false) }), undefined, false);
    saveInstallation = (token: string, input: InstallationInput) =>
        this.send(this.http.put<InstallationSaved>('api/installation', input, { headers: this.installationHeaders(token, true) }), undefined, false);
    /** The folders of the server's disks (the drives and usual places for null). */
    serverFolders = (token: string, path: string | null) =>
        this.send(this.http.get<FolderPicker>('api/installation/folders', { params: params({ path }), headers: this.installationHeaders(token, false) }), undefined, false);
    /** Whether the install waits for its update to this version (works while everything else is closed). */
    migrationInfo = () => this.get<MigrationInfo>('api/migration', {}, undefined, false);
    /** Starts the update (in the background: poll `migrationInfo`); rejects with the code's error. */
    startMigration = (code: string) => this.post<MigrationInfo>('api/migration', { code }, undefined, false);
    /** Makes a backup before the update; the browser downloads it from the link answered, once. */
    migrationBackup = (code: string) => this.post<BackupLink>('api/migration/backup', { code }, undefined, false);

    /** A link to `path` working without signing in, for a while: for external players. */
    fileLink = (path: string) => this.post<FileLink>('api/files/links', { path });
    /** A link playing the Flash movie `path` in the player's frame, opening the files of its folder to it, read only. */
    flashLink = (path: string) => this.post<FileLink>('api/files/flash-links', { path });
    /** Creates the folder `name` in the folder `path`. Resolves with the conflict (409: the name is taken, a scan runs...). */
    createFolder = (path: string, name: string) => this.post<FileOperationResult>('api/files/folders', { path, name });
    /** Creates the empty file `name` in the folder `path`. Resolves with the conflict (409) as `createFolder`. */
    createFile = (path: string, name: string) => this.post<FileOperationResult>('api/files/new-file', { path, name });
    /** Renames the file or folder `path` to `name`, in its folder. Resolves with the conflict (409) as `createFolder`. */
    renameItem = (path: string, name: string) => this.post<FileOperationResult>('api/files/rename', { path, name });
    /** Deletes `paths`, to the trash or for good; a result per path. */
    deleteItems = (paths: string[], permanent: boolean) => this.post<DeleteResults>('api/files/delete', { paths, permanent });
    /** The trash, newest first; `before`: the `next` of the previous page. */
    trash = (before: number | null = null) => this.get<TrashPage>('api/files/trash', { before });
    /** How many items of the trash the session sees, for the sidebar's Trash link. */
    trashCount = () => this.get<TrashCount>('api/files/trash/count');
    /** Puts an item of the trash back. Resolves with the conflict (409: a scan runs there...). */
    restoreFromTrash = (id: number) => this.post<FileOperationResult>(`api/files/trash/${id}/restore`);
    purgeFromTrash = (id: number) => this.delete<void>(`api/files/trash/${id}`);
    emptyTrash = () => this.delete<{ purged: number }>('api/files/trash');
    /** Who would see or change more of the folders among `paths` once moved into `target`. */
    checkMove = (paths: string[], target: string) => this.post<{ gains: AccessGain[] }>('api/files/jobs/check', { paths, target });
    /** Starts a move or copy in the background; its first state. */
    startFileJob = (request: FileJobRequest) => this.post<FileJobView>('api/files/jobs', request);
    /** Extracts an archive: a job followed as moves and copies are. */
    startExtract = (request: ExtractRequest) => this.post<FileJobView>('api/files/jobs/extract', request);
    /** Compresses files and folders into a zip in the library: a job followed as moves and copies are. */
    startCompress = (request: CompressRequest) => this.post<FileJobView>('api/files/jobs/compress', request);
    /** The session's moves and copies, running and recent, newest first. */
    fileJobs = () => this.get<FileJobView[]>('api/files/jobs');
    answerFileJob = (id: number, choice: ConflictChoice, forAll: boolean) => this.post<void>(`api/files/jobs/${id}/answer`, { choice, forAll });
    cancelFileJob = (id: number) => this.post<void>(`api/files/jobs/${id}/cancel`);
    /** Which of `names` (paths below `target` for a folder upload) are taken in `target`. */
    checkUploads = (target: string, names: string[]) => this.post<UploadCheck>('api/files/uploads/check', { target, names });
    startUpload = (start: { target: string; name: string; size: number; modified: string | null }) => this.post<UploadInfo>('api/files/uploads', start);
    /** The session's unfinished uploads; `all`: everyone's (right `settings.files`). */
    uploads = (all = false) => this.get<UploadInfo[]>('api/files/uploads', { all: all || null });
    /** Sends `chunk` at `offset`; resolves with the upload also when the server has another offset (409): go on from there. */
    sendUploadChunk = (id: string, offset: number, chunk: Blob, signal?: AbortSignal) =>
        this.send(this.http.put<UploadInfo>(`api/files/uploads/${id}`, chunk, {
            params: params({ offset }),
            headers: this.headers(true).set('Content-Type', 'application/octet-stream'),
        }), signal);
    /** Puts a sent upload in its folder: the new file, the conflict (409: the name is taken), or null when skipped. */
    finishUpload = (id: string, conflicts: ConflictChoice) => this.post<FileOperationResult | null>(`api/files/uploads/${id}/finish`, { conflicts });
    cancelUpload = (id: string) => this.delete<void>(`api/files/uploads/${id}`);
    /** Names the items of a zip; the browser downloads it from the link answered, once. */
    createArchive = (paths: string[]) => this.post<ArchiveLink>('api/files/archive', { paths });
    properties = (path: string) => this.get<ItemProperties>('api/files/properties', { path });
    /** What the index knows below a folder. */
    folderTotals = (path: string, signal?: AbortSignal) => this.get<FolderTotals>('api/files/properties/totals', { path }, signal);
    countFolder = (path: string) => this.post<PropertyJob>('api/files/properties/count', { path });
    startChecksum = (path: string, algorithm: ChecksumAlgorithm) => this.post<PropertyJob>('api/files/properties/checksum', { path, algorithm });
    /**
     * A folder of an archive (`folder`: its id; `at`: its path inside the archive; the root without them): a page, the
     * reading of its list so far (ask again), or 409 `not-explorable`.
     */
    exploreArchive = (path: string, where: { folder?: number | null; at?: string | null; after?: string | null; limit?: number | null; unlock?: string | null } = {}, signal?: AbortSignal) =>
        this.send(this.http.get<ArchiveAnswer>('api/files/explore', {
            params: params({ path, folder: where.folder, at: where.at, after: where.after, limit: where.limit }),
            headers: where.unlock ? this.headers(false).set('X-SFE-Archive-Unlock', where.unlock) : this.headers(false),
        }), signal);
    /**
     * Folders and files of an archive whose name holds `q` (2 characters or more), in pages that go on with `after`;
     * with `folder`, only those right inside that folder, in its order.
     */
    searchArchive = (path: string, query: { q: string; folder?: number | null; after?: string | null; unlock?: string | null }, signal?: AbortSignal) =>
        this.send(this.http.get<ArchiveSearchAnswer>('api/files/explore/search', {
            params: params({ path, q: query.q, folder: query.folder, after: query.after }),
            headers: query.unlock ? this.headers(false).set('X-SFE-Archive-Unlock', query.unlock) : this.headers(false),
        }), signal);
    /** A text file inside an archive (its listing's `id`), decoded as `textFile` decodes a library file; read only. */
    archiveText = (path: string, id: number, query: { encoding?: string | null; unlock?: string | null } = {}, signal?: AbortSignal) =>
        this.send(this.http.get<TextFile>('api/files/explore/text', {
            params: params({ path, id, encoding: query.encoding }),
            headers: query.unlock ? this.headers(false).set('X-SFE-Archive-Unlock', query.unlock) : this.headers(false),
        }), signal);
    /**
     * Tries `password` on an archive: an unlock for the requests about it (kept by the page only), the list still being
     * read, or 409 `not-protected`; a wrong password rejects (422 `wrong-password`), too many tries too (429).
     */
    unlockArchive = (path: string, password: string) => this.post<ArchiveUnlockAnswer>('api/files/explore/unlock', { path, password });
    /** A text file's content (413 `too-large`, 415 `not-text`). */
    /** `encoding`: read it in that encoding rather than the one it seems to have (Reopen with encoding). */
    textFile = (path: string, signal?: AbortSignal, encoding?: string | null) => this.get<TextFile>('api/files/text', { path, encoding }, signal);
    /** About `length` bytes of a text file of any size from `offset` (-1: its end), cut at whole lines. */
    textPart = (path: string, offset: number, length?: number | null, encoding?: string | null, signal?: AbortSignal) =>
        this.get<TextPart>('api/files/text/part', { path, offset, length, encoding }, signal);
    /** Its version now (read again only when its size or time are not those of `known`); null over 10 MB. */
    textVersion = (path: string, known: string | null) => this.get<TextVersion>('api/files/text/version', { path, known });
    /** The video next to a subtitle file, named as it is (`Film.en.srt` → `Film.mkv`). */
    subtitleVideo = (path: string, signal?: AbortSignal) => this.get<SubtitleVideo>('api/files/text/video', { path }, signal);
    /**
     * Saves text over the file `path`, if it is still `version` (`*`: whatever it is): 412 `file-changed` when it
     * changed meanwhile, 422 `not-encodable`; a 409 (link, read-only, scan) answers its problem.
     */
    saveText = (path: string, text: TextToSave, session: string, version: string) =>
        this.send(this.http.put<SaveTextResult>(withQuery('api/files/text', { path }), { ...text, session },
            { headers: this.headers(true).set('If-Match', version === '*' ? '*' : `"${version}"`) }));
    /** Saves text as the new file `name` in the folder `path` (409 `file-exists` when the name is taken). */
    saveTextCopy = (path: string, name: string, text: TextToSave) => this.post<FileOperationResult>('api/files/text', { path, name, ...text });
    propertyJob = (id: string) => this.get<PropertyJob>(`api/files/properties/jobs/${id}`);
    cancelPropertyJob = (id: string) => this.delete<void>(`api/files/properties/jobs/${id}`);

    /** Saves `image` (JPEG, PNG or WebP) as the edited version of the picture `path`: a new file, the original stays. */
    saveEditedImage = (path: string, image: Blob) => this.post<EditedImage>(withQuery('api/editor/images', { path }), image);
    /** The picture's version, and whether this account may save over it. */
    editedImageAccess = (path: string) => this.get<EditedImageAccess>('api/editor/images/access', { path });
    /**
     * Saves `image` over the picture `path`, if it is still `version` (`*`: whatever it is): 412 `file-changed`
     * when it changed meanwhile; a 409 (link, read-only, scan) answers its problem.
     */
    saveEditedImageOver = (path: string, image: Blob, session: string, version: string) =>
        this.send(this.http.put<SaveImageResult>(withQuery('api/editor/images', { path, session }), image,
            { headers: this.headers(true).set('If-Match', version === '*' ? '*' : `"${version}"`) }));

    /*
     * Accounts, groups and rights. `tenant` names another space than the session's (server administrators manage every
     * space from wherever they are); left out, the session's space.
     */
    rights = (tenant?: number | null) => this.get<RightsCatalog>('api/access/rights', { tenant });
    /** `all`: every account of the server (server administrators), else those of the space. */
    users = (all = false, tenant?: number | null) => this.get<UserInfo[]>('api/access/users', { all, tenant });
    createUser = (user: UserInput) => this.post<UserInfo>('api/access/users', user);
    updateUser = (id: number, user: UserInput) => this.put<UserInfo>(`api/access/users/${id}`, user);
    deleteUser = (id: number) => this.delete<void>(`api/access/users/${id}`);
    /** `all`: the groups of every space (server administrators), else those of the space. */
    groups = (all = false, tenant?: number | null) => this.get<GroupInfo[]>('api/access/groups', { all, tenant });
    createGroup = (group: GroupInput, tenant?: number | null) => this.post<GroupInfo>(withQuery('api/access/groups', { tenant }), group);
    updateGroup = (id: number, group: GroupInput, tenant?: number | null) => this.put<GroupInfo>(withQuery(`api/access/groups/${id}`, { tenant }), group);
    deleteGroup = (id: number, tenant?: number | null) => this.delete<void>(withQuery(`api/access/groups/${id}`, { tenant }));
    tenantAccess = () => this.get<TenantAccess>('api/access/tenant');
    /** The space's folder rules. */
    folderRules = (tenant?: number | null) => this.get<FolderRuleView[]>('api/access/folder-rules', { tenant });
    /** Sets (or with `level` null removes) a rule; answers every rule. */
    setFolderRule = (change: FolderRuleChange, tenant?: number | null) => this.put<FolderRuleView[]>(withQuery('api/access/folder-rules', { tenant }), change);
    /** Moves the rules of a folder gone outside SFE to `to`; answers every rule. */
    repointFolderRules = (from: string, to: string, tenant?: number | null) =>
        this.post<FolderRuleView[]>(withQuery('api/access/folder-rules/repoint', { tenant }), { from, to });
    /** Who may see and change the folder `path`. */
    folderAccess = (path: string, tenant?: number | null) => this.get<FolderAccessReport>('api/access/folder-access', { path, tenant });
    /** Every sub-folder of `path` in the space (for choosing where a rule goes). */
    spaceFolders = (path: string | null, tenant?: number | null) => this.get<FolderPicker>('api/access/folders', { path, tenant });
    setGuestAccess = (guestAccess: boolean) => this.put<TenantAccess>('api/access/tenant/guest-access', { guestAccess });

    /** Every space (server administrators). */
    tenants = () => this.get<TenantDetails[]>('api/tenants');
    /** The folders a space's library folder can be chosen in: the allowed roots without `path`, else its sub-folders. */
    tenantFolders = (path: string | null) => this.get<FolderPicker>('api/tenants/folders', { path });
    createTenant = (tenant: TenantInput) => this.post<TenantDetails>('api/tenants', tenant);
    updateTenant = (id: number, tenant: TenantInput) => this.put<TenantDetails>(`api/tenants/${id}`, tenant);
    deleteTenant = (id: number) => this.delete<void>(`api/tenants/${id}`);

    /**
     * The library the page shows (/api/config): sent with every request, so that a session switched to another
     * library meanwhile (in another tab) is noticed (409 `tenant-changed`: the page reloads).
     */
    tenantId: number | null = null;

    private get<T>(url: string, query: Record<string, QueryValue> = {}, signal?: AbortSignal, redirect = true): Promise<T> {
        return this.send(this.http.get<T>(url, { params: params(query), headers: this.headers(false) }), signal, redirect);
    }

    private post<T>(url: string, body: unknown = null, signal?: AbortSignal, redirect = true): Promise<T> {
        return this.send(this.http.post<T>(url, body, { headers: this.headers(true) }), signal, redirect);
    }

    private put<T>(url: string, body: unknown): Promise<T> {
        return this.send(this.http.put<T>(url, body, { headers: this.headers(true) }));
    }

    private patch<T>(url: string, body: unknown): Promise<T> {
        return this.send(this.http.patch<T>(url, body, { headers: this.headers(true) }));
    }

    private delete<T>(url: string): Promise<T> {
        return this.send(this.http.delete<T>(url, { headers: this.headers(true) }));
    }

    /**
     * Requests changing something carry `X-SFE-Request`: a page of another site cannot add it without the server's
     * consent (CORS), so it cannot act with this browser's session.
     */
    private headers(changes: boolean): HttpHeaders {
        // The change feed tells other tabs of this tab's changes, not this one.
        let headers = new HttpHeaders().set('X-SFE-Tab', TAB_ID);
        if (changes)
            headers = headers.set('X-SFE-Request', '1');
        if (this.tenantId !== null)
            headers = headers.set('X-SFE-Tenant', String(this.tenantId));
        return headers;
    }

    /** The installation page's routes: the token of `checkInstallation` instead of a session. */
    private installationHeaders(token: string, changes: boolean): HttpHeaders {
        return this.headers(changes).set('X-SFE-Installation', token);
    }

    /**
     * The response as a promise; aborting `signal` unsubscribes, which cancels the request. Unless `redirect` is
     * off, a request refused for want of a session (401) opens the sign-in page, one refused before the server is
     * set up (503 `setup-required`) the setup page. One refused while the install waits for its update (503
     * `migration-required`) always opens the update page: no other page works then; one refused because a Local server
     * answers its own computer only (403 `local-only`) always opens the page saying so.
     */
    private send<T>(request: Observable<T>, signal?: AbortSignal, redirect = true): Promise<T> {
        return new Promise<T>((resolve, reject) => {
            if (signal?.aborted) {
                reject(new ApiError(0, 'Cancelled'));
                return;
            }
            const subscription = request.subscribe({
                next: resolve,
                error: (error: unknown) => {
                    const type = problemType(error);
                    if (type === 'tenant-changed') {
                        // Another tab switched the session to another library: show that one.
                        location.reload();
                        reject(toApiError(error));
                    } else if (error instanceof HttpErrorResponse && error.status === 409)
                        resolve(error.error as T);
                    else {
                        if (error instanceof HttpErrorResponse && (redirect || type === 'migration-required' || type === 'local-only'))
                            this.redirect(error.status, type);
                        reject(toApiError(error));
                    }
                },
            });
            signal?.addEventListener('abort', () => {
                subscription.unsubscribe();
                reject(new ApiError(0, 'Cancelled'));
            }, { once: true });
        });
    }

    private redirect(status: number, type: string | null): void {
        if (status !== 401 && !(status === 503 && (type === 'setup-required' || type === 'migration-required')) && !(status === 403 && type === 'local-only'))
            return;
        const router = this.injector.get(Router);
        const page = router.url.split('?')[0];
        if (status === 403 && type === 'local-only') {
            if (page !== '/local-only' && page !== '/login')
                void router.navigate(['/local-only']);
        } else if (status === 503 && type === 'migration-required') {
            if (page !== '/migration')
                void router.navigate(['/migration']);
        } else if (status === 503 && type === 'setup-required' && page !== '/setup')
            void router.navigate(['/setup']);
        else if (status === 401 && page !== '/login' && page !== '/setup' && page !== '/migration')
            void router.navigate(['/login'], { queryParams: { return: router.url === '/' ? null : router.url } });
    }
}

/** The `type` of a problem response (`setup-required`, `tenant-changed`...). */
function problemType(error: unknown): string | null {
    return error instanceof HttpErrorResponse && error.error && typeof error.error === 'object' && typeof error.error.type === 'string'
        ? error.error.type
        : null;
}

/** `url` with the settings level asked for, when one is. */
function withLevel(url: string, level: SettingLevel | undefined): string {
    return level ? `${url}?level=${level}` : url;
}

function toApiError(error: unknown): ApiError {
    if (!(error instanceof HttpErrorResponse))
        return new ApiError(0, error instanceof Error ? error.message : String(error));
    const body = error.error && typeof error.error === 'object' ? error.error as { errors?: Record<string, string[]>; detail?: string; title?: string } : null;
    // Validation problems carry the useful message in `errors`, the title is generic.
    const validation = body?.errors ? Object.values(body.errors).flat()[0] : null;
    const message = validation || body?.detail || body?.title
        || (error.status ? `${error.status} ${error.statusText}` : 'The server is not reachable');
    return new ApiError(error.status, message, body?.errors ?? null, problemType(error));
}

/**
 * The request was refused for want of a session, of the setup or of the update, or by a Local server to another
 * computer: the Api has opened the sign-in, setup, update or Local mode page.
 */
export function redirected(error: unknown): boolean {
    return error instanceof ApiError && (error.status === 401 || error.status === 503 && (error.type === 'setup-required' || error.type === 'migration-required')
        || error.status === 403 && error.type === 'local-only');
}
