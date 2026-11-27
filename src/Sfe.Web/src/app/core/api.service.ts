import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { inject, Injectable, Injector } from '@angular/core';
import { Router } from '@angular/router';
import { Observable } from 'rxjs';
import { ApiError, problemType, toApiError } from './api-error';
import { chunkHeaders, params, QueryValue, UploadChunk, withLevel, withQuery } from './api-query';
import { redirectAfterRefusal } from './api-redirect';
import {
    FolderAccessReport,
    FolderRuleView,
    GroupInfo,
    RightsCatalog,
    TenantDetails,
    UserInfo,
} from '../access/access.models';
import { InstallationInput, InstallationSaved, SetupInfo } from '../account/account.models';
import { KeepAliveView, StreamingStatus, SystemInfo } from '../admin/admin.models';
import { EditedImageAccess } from '../editor/editor.models';
import {
    AccessGain,
    ChecksumAlgorithm,
    FolderTotals,
    PropertyJob,
    ConflictChoice,
    CompressRequest,
    ExtractRequest,
    FileJobRequest,
    FileJobView,
    TransferRequest,
    UploadInfo,
    UploadStart,
    TrashPage,
    FileVersions,
    VersionRestore,
} from '../files/file.models';
import { ArchiveAnswer, TextFile, TextPart, TextToSave } from '../formats/format.models';
import {
    TrashCount,
    StorageUsage,
    ExcludedFolder,
    ExclusionKind,
    FolderCover,
    FolderSiblings,
    IndexedFolder,
    QuickLink,
    RemovedFromIndex,
    ScanStatus,
    SearchAsk,
    BrowseAsk,
    SearchResults,
    SortField,
    TagSidebar,
    ConnectionSearchResults,
} from '../library/library.models';
import { LogQuery, LogSearchResult } from '../logs/log.models';
import { BackupLink, MigrationInfo } from '../migration/migration.models';
import { SettingChanges } from '../settings/settings.models';
import { FileTagChange, FolderDefaultTagsResult, ScopeTagChangeResult, ScopeTags, TagInput } from '../tags/tag.models';
import { TaskOverview, TaskRunDetails, TaskSchedule, TaskScheduleInput } from '../tasks/task.models';
import { DuplicateResults, MissingFileResults, SimilarVideoResults } from '../tracking/tracking.models';
import { TrickplayActivity, TrickplayStatus } from '../trickplay/trickplay.models';
import {
    FolderRuleChange,
    GroupInput,
    InstallationGrant,
    InstallationView,
    SessionInfo,
    SetupInput,
    TenantAccess,
    TenantInput,
    UserInput,
} from './access.models';
import { AccessTokenDraft, AccessTokenInfo, CreatedAccessToken } from './access-token.models';
import { ArchiveSearchAnswer, ArchiveUnlockAnswer } from './archive.models';
import {
    TextVersion,
    SubtitleVideo,
    SaveTextResult,
    ClientConfig,
    EditedImage,
    SaveImageResult,
    StreamProbe,
} from './config.models';
import {
    ArchiveLink,
    ItemProperties,
    DeleteResults,
    UploadCheck,
    FileOperationResult,
    DirectoryListing,
    FileLink,
    FolderExclusions,
    FolderPicker,
} from './models';
import { placeRoute, tagsRoute, uploadsRoute } from './connection-place';
import { SettingLevel, SettingsValues, SettingsView } from './setting.models';
import {
    FileTagChangeResult,
    FileTags,
    FolderDefaults,
    FolderDefaultTagsChange,
    ScopeTagChange,
    TagDetails,
    TagScope,
    TagSearchResults,
} from './tag.models';
import {
    RelinkPassResult,
    RemovedDuplicate,
    TaskActionResult,
    TaskInfo,
    TaskKind,
    TaskRunPage,
    TrickplayManifest,
    TrickplayStates,
} from './task.models';
import {
    ConnectionChange,
    ConnectionIndexStatus,
    ConnectionInfo,
    ConnectionListing,
    ConnectionRule,
    ConnectionRuleView,
    ConnectionsView,
    ConnectRequest,
    FolderLevelName,
    ProviderSetup,
    ProviderTest,
    SignInBegun,
    SignInDone,
    SignInKindName,
} from './connection.models';
import { helpFile } from '../help/help-files';
import { HelpIndex } from '../help/help.models';
import { encodePath } from './paths';
import { takeSetupLink } from './setup-link';

/**
 * The base of every address of the server's API: requests, file addresses, the change feed. Relative, so the app also
 * works under a virtual directory; versioned (agents/docs/development/api-versioning.md): the unversioned `api/` paths
 * are old names the server still answers, never used by the client. `api/docs` (Swagger UI) has no version.
 */
export { ApiError, redirected } from './api-error';
export { editingSession } from './editing-session';

export const API_BASE = 'api/v1/';

/** This tab, for the change feed: its own changes are not told back to it. */
export const TAB_ID = Math.random().toString(36).slice(2, 12);

/** The token of the link SFE opened this browser with at its first start (setup-link.ts); null without it. */
const SETUP_LINK = takeSetupLink();

/**
 * The JSON API of the server. Routes are written below `API_BASE` (`config` is `api/v1/config`), relative, so the app
 * also works under a virtual directory. Requests resolve with the body; `signal` (AbortSignal) cancels a request, and
 * the work on the server, e.g. when the user navigates on. A 409 (conflict) resolves with its body: the API answers
 * with the current state then.
 */
@Injectable({ providedIn: 'root' })
export class Api {
    private readonly http = inject(HttpClient);
    /** The router, asked for when a redirect is due (it needs the Api itself, through the pages). */
    private readonly injector = inject(Injector);

    config = () => this.get<ClientConfig>('config');
    /** The help (no redirect: its page tells a 401 itself); a page of it as Markdown: `helpPage`. */
    helpIndex = () => this.get<HelpIndex>('help', {}, undefined, false);
    helpPage = (path: string, signal?: AbortSignal) =>
        this.send(this.http.get(API_BASE + helpFile(path), { responseType: 'text' }), signal, false);

    browse = (query: BrowseAsk, signal?: AbortSignal) => this.get<DirectoryListing>('browse', query, signal);
    /** The folders around `path` in its parent, in the given sort. */
    siblings = (query: { path: string; sort: SortField; desc: boolean }, signal?: AbortSignal) =>
        this.get<FolderSiblings>('browse/siblings', query, signal);
    /** The first pictures of each folder of `paths` that exists (folder cards). */
    folderCovers = (paths: string[], signal?: AbortSignal) =>
        this.post<FolderCover[]>('browse/covers', { paths }, signal);
    /** `after`: the `next` value of the previous page; a connection's files: `connectionSearch` (its index). */
    search = (query: SearchAsk, signal?: AbortSignal) => this.get<SearchResults>('search', query, signal);
    connectionSearch = (id: number, query: SearchAsk, signal?: AbortSignal) =>
        this.getOrRefused<ConnectionSearchResults>(`connections/${id}/search`, query, signal);

    scanStatus = () => this.get<ScanStatus>('scan');
    startScan = (path: string) => this.post<ScanStatus>('scan', { path });
    cancelScan = () => this.delete<ScanStatus>('scan');
    /** What the index holds of a folder (counts), before removing it. */
    indexedFolder = (path: string) => this.get<IndexedFolder>('index/folder', { path });
    /** Removes a folder from the index (not the disk); `exclude` keeps scans out too. A scan running: its status. */
    removeFromIndex = (path: string, exclude: boolean) =>
        this.delete<RemovedFromIndex | ScanStatus>(`index/folder?${params({ path, exclude }).toString()}`);

    /** Every folder kept out of scans, trickplay or thumbnails in the space (those the session sees). */
    folderExclusions = () => this.get<ExcludedFolder[]>('index/exclusions');
    /** Keeps a folder out of scans, trickplay or thumbnails (true) or lets it in again (false); absent kinds stay. */
    setFolderExclusions = (path: string, changes: Partial<Record<ExclusionKind, boolean>>) =>
        this.put<FolderExclusions>('index/exclusions', { path, ...changes });

    /** The background tasks, their schedules and the server's clock. */
    tasks = () => this.get<TaskOverview>('tasks');
    /** Resolves with the result also when the task could not start (409). */
    startTask = (task: TaskKind, path: string | null = null) =>
        this.post<TaskActionResult>(`tasks/${task}/start?${params({ path }).toString()}`);
    /** Resolves with the task also when it wasn't running (409). */
    cancelTask = (task: TaskKind) => this.post<TaskInfo>(`tasks/${task}/cancel`);
    createSchedule = (schedule: TaskScheduleInput) => this.post<TaskSchedule>('tasks/schedules', schedule);
    updateSchedule = (id: number, schedule: TaskScheduleInput) =>
        this.put<TaskSchedule>(`tasks/schedules/${id}`, schedule);
    deleteSchedule = (id: number) => this.delete<void>(`tasks/schedules/${id}`);
    /** The next runs of a schedule being edited; rejects with its validation errors. */
    previewSchedule = (schedule: TaskScheduleInput) =>
        this.post<{ runs: string[] }>('tasks/schedules/preview', schedule);
    /** Runs newest first; `before`: the `next` value of the previous page. */
    taskRuns = (query: { task: TaskKind | null; before: number | null }) => this.get<TaskRunPage>('tasks/runs', query);
    taskRun = (id: number) => this.get<TaskRunDetails>(`tasks/runs/${id}`);
    clearTaskRuns = () => this.delete<void>('tasks/runs');

    logs = (query: LogQuery, signal?: AbortSignal) => this.get<LogSearchResult>('logs', { ...query }, signal);

    tags = (query: { q: string; limit: number }) => this.get<TagSearchResults>('tags', query);
    tag = (id: number) => this.get<TagDetails>(`tags/${id}`);
    createTag = (tag: TagInput) => this.post<TagDetails>('tags', tag);
    updateTag = (id: number, tag: TagInput) => this.put<TagDetails>(`tags/${id}`, tag);
    deleteTag = (id: number) => this.delete<void>(`tags/${id}`);
    fileTags = (path: string, connection?: number | null) => this.get<FileTags>(tagsRoute(connection), { path });
    changeFileTags = (change: FileTagChange, c?: number | null) => this.post<FileTagChangeResult>(tagsRoute(c), change);
    /** The tags of the files of a folder or of a search's matches (in the connection `c`), counted. */
    scopeTags = (scope: TagScope, signal?: AbortSignal, c?: number | null) =>
        this.get<ScopeTags>(
            `${tagsRoute(c)}/scope`,
            { path: scope.path, recursive: scope.recursive, q: scope.query },
            signal,
        );
    /** Tags every file of a folder or every match of a search. Resolves with the problem (409) while a scan runs. */
    changeScopeTags = (change: ScopeTagChange, c?: number | null) =>
        this.post<ScopeTagChangeResult | { detail?: string }>(`${tagsRoute(c)}/scope`, change);
    fileViewed = (path: string) => this.post<void>('file-tags/viewed', { path });
    /** The default tags of a folder, its own and its parents'. */
    folderDefaultTags = (path: string, signal?: AbortSignal) =>
        this.get<FolderDefaults>('folder-default-tags', { path }, signal);
    /** Changes a folder's default tags (and its files' when asked); a scan running: its problem (409). */
    changeFolderDefaultTags = (change: FolderDefaultTagsChange) =>
        this.put<FolderDefaultTagsResult | { detail?: string }>('folder-default-tags', change);
    tagSidebar = () => this.get<TagSidebar>('tags/sidebar');
    clearTagHistory = () => this.delete<void>('tags/history');

    /** The settings of a level: the server's (server administrators) or the space's; else the caller's default. */
    settings = (level?: SettingLevel) => this.get<SettingsView>('settings', { level });
    saveSettings = (values: SettingsValues, level?: SettingLevel) =>
        this.put<SettingsView>(withLevel('settings', level), values);
    /** Saves only the settings (and locks) in `changes` at `level`: what the level has for the others stays. */
    saveSettingChanges = (changes: SettingChanges, level?: SettingLevel) =>
        this.patch<SettingsView>(withLevel('settings', level), changes);
    /** Locks `keys` at `level` for the levels below; the other settings it may lock are unlocked. */
    saveSettingLocks = (keys: string[], level: SettingLevel) =>
        this.put<SettingsView>(withLevel('settings/locks', level), { keys });
    resetSettings = (level?: SettingLevel) => this.delete<SettingsView>(withLevel('settings', level));
    /** The signed-in user's own preferences (the user level). */
    preferences = () => this.get<SettingsView>('me/preferences');
    savePreferences = (values: SettingsValues) => this.put<SettingsView>('me/preferences', values);
    /** Saves only the preferences in `changes.values`; the others keep following the space. */
    savePreferenceChanges = (changes: SettingChanges) => this.patch<SettingsView>('me/preferences', changes);
    resetPreferences = () => this.delete<SettingsView>('me/preferences');
    /** The signed-in user's quick links in the library, in order. */
    quickLinks = () => this.get<QuickLink[]>('me/quick-links');
    /** Adds `paths` before the link at `position` (else at the end), moving those there; answers the whole list. */
    addQuickLinks = (paths: string[], position: number | null = null) =>
        this.post<QuickLink[]>('me/quick-links', { paths, position });
    removeQuickLink = (path: string) => this.delete<QuickLink[]>(withQuery('me/quick-links', { path }));

    /** Trickplay of a video; rejects (404) when it has none yet. */
    trickplay = (path: string) => this.get<TrickplayManifest>(`trickplay/files/${encodePath(path)}`);
    /** Same, without queueing a video that has none (a thumbnail's hover preview). */
    existingTrickplay = (path: string) =>
        this.get<TrickplayManifest>(`trickplay/files/${encodePath(path)}`, { queue: 'false' });
    trickplayStatus = () => this.get<TrickplayStatus>('trickplay');
    /** Whether a picture has more than one frame (read from its structure on the server). */
    imageAnimation = (path: string) => this.get<{ animated: boolean }>(`files/animated/${encodePath(path)}`);
    /** Running videos, the first `limit` waiting ones and the last ones finished. */
    trickplayActivity = (limit: number) => this.get<TrickplayActivity>('trickplay/activity', { limit });
    /** Those of `paths` waiting or being processed. */
    trickplayStates = (paths: string[]) => this.post<TrickplayStates>('trickplay/states', { paths });
    /** Resolves with the status, also when trickplay is off (409). */
    checkTrickplay = () => this.post<TrickplayStatus>('trickplay');

    /** Groups of identical files under `path`, biggest first; `after`: the `next` value of the previous page. */
    duplicates = (query: { path: string; after: string | null }) => this.get<DuplicateResults>('duplicates', query);
    /** Deletes the copy at `path` after adding its tags to the identical copy `keep`. */
    removeDuplicate = (path: string, keep: string) => this.post<RemovedDuplicate>('duplicates/remove', { path, keep });
    /** Groups of similar videos of the last comparison, with its status. */
    similarVideos = (query: { path: string; after: string | null }) =>
        this.get<SimilarVideoResults>('similar-videos', query);
    /** Starts comparing the videos (nothing while a comparison runs). */
    compareVideos = () => this.post<SimilarVideoResults['status']>('similar-videos');
    /** Tagged files scans no longer find, with the files that may be them. */
    missingFiles = (query: { after: number | null }) => this.get<MissingFileResults>('missing-files', query);
    /** Relinks the missing files that have exactly one likely new place. */
    relinkMissingFiles = () => this.post<RelinkPassResult>('missing-files/relink');
    relinkMissingFile = (id: number, path: string) => this.post<void>(`missing-files/${id}/relink`, { path });
    /** Forgets one missing file, or all of them when `id` is omitted. */
    forgetMissingFiles = (id?: number) => this.delete<void>(id === undefined ? 'missing-files' : `missing-files/${id}`);

    /** Whether `path` (of a connection when given) plays converted by the server, and its length; 404 if not. */
    streamProbe = (path: string, connection?: number | null) =>
        this.get<StreamProbe>(`${placeRoute(connection)}streaming/probe`, { path });
    /** The videos the server converts now, in every library. */
    streamingStatus = () => this.get<StreamingStatus>('streaming/sessions');

    /** Where the library is on the server's disk, and the ffmpeg found. */
    systemInfo = () => this.get<SystemInfo>('system/info');

    /** Whether the space's library folder is kept alive on its file server, and what the last pings found. */
    keepAlive = () => this.get<KeepAliveView>('keep-alive');
    /** Pings the space's folders now (also while keep alive is off). */
    pingKeepAlive = () => this.post<KeepAliveView>('keep-alive/ping');

    /** Who is signed in; works before signing in and before the setup (never redirects). */
    session = () => this.get<SessionInfo>('session', {}, undefined, false);
    signIn = (userName: string, password: string, remember: boolean) =>
        this.post<SessionInfo>('session/login', { userName, password, remember }, undefined, false);
    signOut = () => this.post<SessionInfo>('session/logout', null, undefined, false);
    /** Browses `tenant` (a slug) as a visitor without an account. */
    browseAsGuest = (tenant: string) => this.post<SessionInfo>('session/guest', { tenant }, undefined, false);
    /** Has the server show a one-time code for `userName` where it runs (console, recovery file); same for any name. */
    requestRecovery = (userName: string) =>
        this.post<{ minutes: number }>('session/recovery', { userName }, undefined, false);
    /** A new password with the code: unlocks the account and signs it in. */
    completeRecovery = (userName: string, code: string, password: string, remember: boolean) =>
        this.post<SessionInfo>('session/recovery/complete', { userName, code, password, remember }, undefined, false);
    switchTenant = (tenant: string) => this.post<SessionInfo>('session/tenant', { tenant }, undefined, false);
    changePassword = (current: string, password: string) =>
        this.put<SessionInfo>('session/password', { current, password });
    setupInfo = () => this.get<SetupInfo>('setup', {}, undefined, false);
    completeSetup = (input: SetupInput) => this.post<SessionInfo>('setup', input, undefined, false);
    /** Checks the setup code; the token answered opens the other installation routes for an hour. */
    checkInstallation = (code: string) =>
        this.post<InstallationGrant>('installation/check', { code }, undefined, false);
    installation = (token: string) =>
        this.send(
            this.http.get<InstallationView>(API_BASE + 'installation', {
                headers: this.installationHeaders(token, false),
            }),
            undefined,
            false,
        );
    saveInstallation = (token: string, input: InstallationInput) =>
        this.send(
            this.http.put<InstallationSaved>(API_BASE + 'installation', input, {
                headers: this.installationHeaders(token, true),
            }),
            undefined,
            false,
        );
    /** The folders of the server's disks (the drives and usual places for null). */
    serverFolders = (token: string, path: string | null) =>
        this.send(
            this.http.get<FolderPicker>(API_BASE + 'installation/folders', {
                params: params({ path }),
                headers: this.installationHeaders(token, false),
            }),
            undefined,
            false,
        );
    /** Whether the install waits for its update to this version (works while everything else is closed). */
    /** With the setup code (once typed on the update page), the update's log and its error's reason; else neither. */
    migrationInfo = (code = '') =>
        this.send(
            this.http.get<MigrationInfo>(API_BASE + 'migration', {
                headers: code ? this.headers(false).set('X-SFE-Setup-Code', code) : this.headers(false),
            }),
            undefined,
            false,
        );
    /** Starts the update (in the background: poll `migrationInfo`); rejects with the code's error. */
    startMigration = (code: string) => this.post<MigrationInfo>('migration', { code }, undefined, false);
    /** Makes a backup before the update; the browser downloads it from the link answered, once. */
    migrationBackup = (code: string) => this.post<BackupLink>('migration/backup', { code }, undefined, false);

    /** A link to `path` working without signing in, for a while: for external players. */
    fileLink = (path: string) => this.post<FileLink>('files/links', { path });
    /** A link playing the Flash movie `path` in the player's frame, opening its folder's files to it, read only. */
    flashLink = (path: string) => this.post<FileLink>('files/flash-links', { path });
    /** Creates the folder `name` in the folder `path`. Resolves with the conflict (409: name taken, scan running). */
    createFolder = (path: string, name: string) => this.post<FileOperationResult>('files/folders', { path, name });
    /** Creates the empty file `name` in the folder `path`. Resolves with the conflict (409) as `createFolder`. */
    createFile = (path: string, name: string) => this.post<FileOperationResult>('files/new-file', { path, name });
    /** Renames `path` to `name`, in its folder. Resolves with the conflict (409) as `createFolder`. */
    renameItem = (path: string, name: string) => this.post<FileOperationResult>('files/rename', { path, name });
    /** Deletes `paths`, to the trash or for good; a result per path. */
    deleteItems = (paths: string[], permanent: boolean) =>
        this.post<DeleteResults>('files/delete', { paths, permanent });
    /** The trash, newest first (`before`: the `next` of the previous page); how many items of it the session sees. */
    trash = (before: number | null = null) => this.get<TrashPage>('files/trash', { before });
    trashCount = () => this.get<TrashCount>('files/trash/count');
    /** The folder this tab shows (null: none; a connection's: `connection`), followed for outside changes. */
    watchFolder = (path: string | null, connection: number | null = null) =>
        this.put<{ watching: boolean }>('files/changes/watch', { path, connection });
    /** The space used and free on the disk holding a library folder (`path`), or in a connection's account (`id`). */
    storage = (id: number | null, path = '') =>
        id === null
            ? this.get<StorageUsage>('storage', path ? { path } : {})
            : this.getOrRefused<StorageUsage>(`connections/${id}/storage`, {});
    /** Puts an item of the trash back. Resolves with the conflict (409: a scan runs there...). */
    restoreFromTrash = (id: number) => this.post<FileOperationResult>(`files/trash/${id}/restore`);
    purgeFromTrash = (id: number) => this.delete<void>(`files/trash/${id}`);
    /** The previous versions of a file, kept in the trash when it was saved over. */
    fileVersions = (path: string) => this.get<FileVersions>('files/versions', { path });
    /** Copies a version next to its file, or puts it in place of the file. Resolves with the conflict (409). */
    restoreVersionCopy = (id: number) => this.post<FileOperationResult>(`files/versions/${id}/copy`);
    restoreVersion = (id: number) => this.post<VersionRestore>(`files/versions/${id}/restore`);
    emptyTrash = () => this.delete<{ purged: number }>('files/trash');
    /** Who would see or change more of the folders among `paths` once moved into `target`. */
    checkMove = (paths: string[], target: string) =>
        this.post<{ gains: AccessGain[] }>('files/jobs/check', { paths, target });
    /** Starts a move or copy in the background; its first state. */
    startFileJob = (request: FileJobRequest) => this.post<FileJobView>('files/jobs', request);
    /** Extracts an archive, compresses into a zip, moves or copies between places: jobs followed as moves are. */
    startExtract = (request: ExtractRequest) => this.post<FileJobView>('files/jobs/extract', request);
    startCompress = (request: CompressRequest) => this.post<FileJobView>('files/jobs/compress', request);
    startTransfer = (request: TransferRequest) => this.post<FileJobView>('files/jobs/transfer', request);
    /** The session's moves and copies, running and recent, newest first. */
    fileJobs = () => this.get<FileJobView[]>('files/jobs');
    answerFileJob = (id: number, choice: ConflictChoice, forAll: boolean) =>
        this.post<void>(`files/jobs/${id}/answer`, { choice, forAll });
    cancelFileJob = (id: number) => this.post<void>(`files/jobs/${id}/cancel`);
    retryFileJob = (id: number) => this.post<FileJobView>(`files/jobs/${id}/retry`);
    /** Which of `names` (paths below `target` for a folder upload) are taken; `connection`: in its files. */
    checkUploads = (target: string, names: string[], connection?: number | null) =>
        this.post<UploadCheck>(`${uploadsRoute(connection)}/check`, { target, names });
    startUpload = (start: UploadStart, connection?: number | null) =>
        this.post<UploadInfo>(uploadsRoute(connection), start);
    /** The session's unfinished uploads into the library (not a connection's); `all`: everyone's (`settings.files`). */
    uploads = (all = false, connection?: number | null) =>
        this.get<UploadInfo[]>(uploadsRoute(connection), { all: all || null });
    /** Sends `chunk` at offset `at`; the upload also on a 409 (the server has another offset): go on from there. */
    sendUploadChunk = (id: string, at: number, chunk: UploadChunk, signal?: AbortSignal, connection?: number | null) =>
        this.send(
            this.http.put<UploadInfo>(API_BASE + `${uploadsRoute(connection)}/${id}`, chunk.bytes, {
                params: params({ offset: at }),
                headers: chunkHeaders(this.headers(true), chunk.digest),
            }),
            signal,
        );
    /** Puts a sent upload in its folder: the new file, the conflict (409: the name is taken), or null when skipped. */
    finishUpload = (id: string, conflicts: ConflictChoice, connection?: number | null) =>
        this.post<FileOperationResult | null>(`${uploadsRoute(connection)}/${id}/finish`, { conflicts });
    cancelUpload = (id: string, connection?: number | null) => this.delete<void>(`${uploadsRoute(connection)}/${id}`);
    /** Changes a connection's files (`folders`, `rename`, `delete`, `transfer`: see `PlaceFiles`), or reads (`get`). */
    connectionFiles = <T>(id: number, action: string, body: unknown) =>
        this.post<T>(`connections/${id}/files/${action}`, body);
    connectionGet = <T>(id: number, action: string, query: Record<string, QueryValue>) =>
        this.getOrRefused<T>(`connections/${id}/files/${action}`, query);
    /** Names the items of a zip (of a connection's files with `connection`); the browser downloads it once. */
    createArchive = (paths: string[], connection?: number) =>
        this.post<ArchiveLink>(connection ? `connections/${connection}/archive` : 'files/archive', { paths });
    properties = (path: string) => this.get<ItemProperties>('files/properties', { path });
    /** What the index knows below a folder. */
    folderTotals = (path: string, signal?: AbortSignal) =>
        this.get<FolderTotals>('files/properties/totals', { path }, signal);
    countFolder = (path: string) => this.post<PropertyJob>('files/properties/count', { path });
    startChecksum = (path: string, algorithm: ChecksumAlgorithm) =>
        this.post<PropertyJob>('files/properties/checksum', { path, algorithm });
    /**
     * A folder of an archive (`folder`: its id; `at`: its path inside the archive; the root without them): a page, the
     * reading of its list so far (ask again), or 409 `not-explorable`.
     */
    exploreArchive = (
        path: string,
        where: {
            folder?: number | null;
            at?: string | null;
            after?: string | null;
            limit?: number | null;
            unlock?: string | null;
        } = {},
        signal?: AbortSignal,
    ) =>
        this.send(
            this.http.get<ArchiveAnswer>(API_BASE + 'files/explore', {
                params: params({ path, folder: where.folder, at: where.at, after: where.after, limit: where.limit }),
                headers: where.unlock
                    ? this.headers(false).set('X-SFE-Archive-Unlock', where.unlock)
                    : this.headers(false),
            }),
            signal,
        );
    /**
     * Folders and files of an archive whose name holds `q` (2 characters or more), in pages that go on with `after`;
     * with `folder`, only those right inside that folder, in its order.
     */
    searchArchive = (
        path: string,
        query: { q: string; folder?: number | null; after?: string | null; unlock?: string | null },
        signal?: AbortSignal,
    ) =>
        this.send(
            this.http.get<ArchiveSearchAnswer>(API_BASE + 'files/explore/search', {
                params: params({ path, q: query.q, folder: query.folder, after: query.after }),
                headers: query.unlock
                    ? this.headers(false).set('X-SFE-Archive-Unlock', query.unlock)
                    : this.headers(false),
            }),
            signal,
        );
    /**
     * A text file inside an archive (its listing's id, or its path in the archive), decoded as `textFile` decodes a
     * library file; read only.
     */
    archiveText = (
        path: string,
        entry: number | string,
        query: { encoding?: string | null; unlock?: string | null } = {},
        signal?: AbortSignal,
    ) =>
        this.send(
            this.http.get<TextFile>(API_BASE + 'files/explore/text', {
                params: params({
                    path,
                    ...(typeof entry === 'number' ? { id: entry } : { at: entry }),
                    encoding: query.encoding,
                }),
                headers: query.unlock
                    ? this.headers(false).set('X-SFE-Archive-Unlock', query.unlock)
                    : this.headers(false),
            }),
            signal,
        );
    /**
     * Tries `password` on an archive: an unlock for the requests about it (kept by the page only), the list still being
     * read, or 409 `not-protected`; a wrong password rejects (422 `wrong-password`), too many tries too (429).
     */
    unlockArchive = (path: string, password: string) =>
        this.post<ArchiveUnlockAnswer>('files/explore/unlock', { path, password });
    /** A text file (413 `too-large`, 415 `not-text`), in `encoding` if given; a connection's (read only) if given. */
    textFile = (path: string, signal?: AbortSignal, encoding?: string | null, connection?: number | null) =>
        this.get<TextFile>(`${placeRoute(connection)}files/text`, { path, encoding }, signal);
    /** About `length` bytes of a text file of any size from `offset` (-1: its end), cut at whole lines. */
    textPart = (path: string, offset: number, length?: number | null, encoding?: string | null, signal?: AbortSignal) =>
        this.get<TextPart>('files/text/part', { path, offset, length, encoding }, signal);
    /** Its version now (read again only when its size or time are not those of `known`); null over 10 MB. */
    textVersion = (path: string, known: string | null) => this.get<TextVersion>('files/text/version', { path, known });
    /** The video next to a subtitle file, named as it is (`Film.en.srt` → `Film.mkv`). */
    subtitleVideo = (path: string, signal?: AbortSignal) =>
        this.get<SubtitleVideo>('files/text/video', { path }, signal);
    /**
     * Saves text over the file `path`, if it is still `version` (`*`: whatever it is): 412 `file-changed` when it
     * changed meanwhile, 422 `not-encodable`; a 409 (link, read-only, scan) answers its problem.
     */
    saveText = (path: string, text: TextToSave, session: string, version: string) =>
        this.send(
            this.http.put<SaveTextResult>(
                API_BASE + withQuery('files/text', { path }),
                { ...text, session },
                { headers: this.headers(true).set('If-Match', version === '*' ? '*' : `"${version}"`) },
            ),
        );
    /** Saves text as the new file `name` in the folder `path` (409 `file-exists` when the name is taken). */
    saveTextCopy = (path: string, name: string, text: TextToSave) =>
        this.post<FileOperationResult>('files/text', { path, name, ...text });
    propertyJob = (id: string) => this.get<PropertyJob>(`files/properties/jobs/${id}`);
    cancelPropertyJob = (id: string) => this.delete<void>(`files/properties/jobs/${id}`);

    /** Saves `image` (JPEG, PNG or WebP) as the edited version of the picture `path`: a new file, the original kept. */
    saveEditedImage = (path: string, image: Blob) =>
        this.post<EditedImage>(withQuery('editor/images', { path }), image);
    /** The picture's version, and whether this account may save over it. */
    editedImageAccess = (path: string) => this.get<EditedImageAccess>('editor/images/access', { path });
    /**
     * Saves `image` over the picture `path`, if it is still `version` (`*`: whatever it is): 412 `file-changed`
     * when it changed meanwhile; a 409 (link, read-only, scan) answers its problem.
     */
    saveEditedImageOver = (path: string, image: Blob, session: string, version: string) =>
        this.send(
            this.http.put<SaveImageResult>(API_BASE + withQuery('editor/images', { path, session }), image, {
                headers: this.headers(true).set('If-Match', version === '*' ? '*' : `"${version}"`),
            }),
        );

    /* Accounts, groups and rights; `tenant`: another space than the session's (server administrators manage all). */
    rights = (tenant?: number | null) => this.get<RightsCatalog>('access/rights', { tenant });
    /** `all`: every account of the server (server administrators), else those of the space. */
    users = (all = false, tenant?: number | null) => this.get<UserInfo[]>('access/users', { all, tenant });
    createUser = (user: UserInput) => this.post<UserInfo>('access/users', user);
    updateUser = (id: number, user: UserInput) => this.put<UserInfo>(`access/users/${id}`, user);
    deleteUser = (id: number) => this.delete<void>(`access/users/${id}`);
    /** `all`: the groups of every space (server administrators), else those of the space. */
    groups = (all = false, tenant?: number | null) => this.get<GroupInfo[]>('access/groups', { all, tenant });
    createGroup = (group: GroupInput, tenant?: number | null) =>
        this.post<GroupInfo>(withQuery('access/groups', { tenant }), group);
    updateGroup = (id: number, group: GroupInput, tenant?: number | null) =>
        this.put<GroupInfo>(withQuery(`access/groups/${id}`, { tenant }), group);
    deleteGroup = (id: number, tenant?: number | null) =>
        this.delete<void>(withQuery(`access/groups/${id}`, { tenant }));
    tenantAccess = () => this.get<TenantAccess>('access/tenant');
    /** The space's folder rules. */
    folderRules = (tenant?: number | null) => this.get<FolderRuleView[]>('access/folder-rules', { tenant });
    /** Sets (or with `level` null removes) a rule; answers every rule. */
    setFolderRule = (change: FolderRuleChange, tenant?: number | null) =>
        this.put<FolderRuleView[]>(withQuery('access/folder-rules', { tenant }), change);
    /** Moves the rules of a folder gone outside SFE to `to`; answers every rule. */
    repointFolderRules = (from: string, to: string, tenant?: number | null) =>
        this.post<FolderRuleView[]>(withQuery('access/folder-rules/repoint', { tenant }), { from, to });
    /** Who may see and change the folder `path`. */
    folderAccess = (path: string, tenant?: number | null) =>
        this.get<FolderAccessReport>('access/folder-access', { path, tenant });
    /** Every sub-folder of `path` in the space (for choosing where a rule goes). */
    spaceFolders = (path: string | null, tenant?: number | null) =>
        this.get<FolderPicker>('access/folders', { path, tenant });
    setGuestAccess = (guestAccess: boolean) => this.put<TenantAccess>('access/tenant/guest-access', { guestAccess });
    /** Personal access tokens: the account's own (`me/tokens`), everyone's for administrators (`access/tokens`). */
    myTokens = () => this.get<AccessTokenInfo[]>('me/tokens');
    createToken = (draft: AccessTokenDraft) => this.post<CreatedAccessToken>('me/tokens', draft);
    revokeToken = (id: number) => this.delete<void>(`me/tokens/${id}`);
    revokeAllTokens = () => this.post<{ revoked: number }>('me/tokens/revoke-all');
    allTokens = () => this.get<AccessTokenInfo[]>('access/tokens');
    revokeAnyToken = (id: number) => this.delete<void>(`access/tokens/${id}`);

    /** Every space (server administrators). */
    tenants = () => this.get<TenantDetails[]>('tenants');
    /** Where a space's library folder can be chosen: the allowed roots without `path`, else their sub-folders. */
    tenantFolders = (path: string | null) => this.get<FolderPicker>('tenants/folders', { path });
    createTenant = (tenant: TenantInput) => this.post<TenantDetails>('tenants', tenant);
    updateTenant = (id: number, tenant: TenantInput) => this.put<TenantDetails>(`tenants/${id}`, tenant);
    deleteTenant = (id: number) => this.delete<void>(`tenants/${id}`);

    /** Connections to other storage (agents/docs/features/connections.md); `kind` of a provider (`dropbox`). */
    connections = () => this.get<ConnectionsView>('connections');
    /** A page of a connection's folder (`path` relative to its root); a 409 rejects (`getOrRefused`). */
    connectionBrowse = (id: number, query: BrowseAsk, signal?: AbortSignal) =>
        this.getOrRefused<ConnectionListing>(`connections/${id}/browse`, query, signal);
    updateConnection = (id: number, change: ConnectionChange) =>
        this.patch<ConnectionInfo>(`connections/${id}`, change);
    removeConnection = (id: number) => this.delete<void>(`connections/${id}`);
    setConnectionLibraries = (id: number, allLibraries: boolean, libraryIds: number[]) =>
        this.put<ConnectionInfo>(`connections/${id}/libraries`, { allLibraries, libraryIds });
    setConnectionGroups = (id: number, groups: Record<number, FolderLevelName>) =>
        this.put<ConnectionInfo>(`connections/${id}/groups`, { groups });
    /** The library's folder rules in a connection; setting them replaces them all. Its sub-folders, for the picker. */
    connectionRules = (id: number) => this.get<ConnectionRuleView[]>(`connections/${id}/folder-rules`);
    setConnectionRules = (id: number, rules: ConnectionRule[]) =>
        this.put<ConnectionRuleView[]>(`connections/${id}/folder-rules`, { rules });
    connectionFolders = (id: number, path: string) =>
        this.getOrRefused<FolderPicker>(`connections/${id}/folders`, { path });
    connectionIndex = (id: number) => this.get<ConnectionIndexStatus>(`connections/${id}/index`);
    syncConnectionIndex = (id: number) => this.post<ConnectionIndexStatus>(`connections/${id}/index/sync`);
    authorizeConnection = (request: ConnectRequest) => this.post<SignInBegun>('connections/authorize', request);
    reauthorizeConnection = (id: number, method: SignInKindName, fields: Record<string, string> = {}) =>
        this.post<SignInBegun>(`connections/${id}/reauthorize`, { method, fields });
    completeConnection = (state: string, fields: Record<string, string>) =>
        this.post<SignInDone>('connections/authorize/complete', { state, fields });
    connectionProvider = (kind: string) => this.get<ProviderSetup>(`connections/providers/${kind}`);
    setProviderSecret = (kind: string, secret: string) =>
        this.put<void>(`connections/providers/${kind}/secret`, { secret });
    removeProviderSecret = (kind: string) => this.delete<void>(`connections/providers/${kind}/secret`);
    testProvider = (kind: string) => this.post<ProviderTest>(`connections/providers/${kind}/test`);

    /**
     * The library the page shows (`config`): sent with every request, so that a session switched to another
     * library meanwhile (in another tab) is noticed (409 `tenant-changed`: the page reloads).
     */
    tenantId: number | null = null;

    private get<T>(
        url: string,
        query: Record<string, QueryValue> = {},
        signal?: AbortSignal,
        redirect = true,
    ): Promise<T> {
        return this.send(
            this.http.get<T>(API_BASE + url, { params: params(query), headers: this.headers(false) }),
            signal,
            redirect,
        );
    }

    /**
     * As `get`, for routes whose 409 is a refusal, not a state to show (a connection that can't be used:
     * `connection-needs-reconnecting`, `connection-unusable`...): it rejects as an `ApiError`, as other errors do.
     */
    private getOrRefused<T>(url: string, query: Record<string, QueryValue>, signal?: AbortSignal): Promise<T> {
        const request = this.http.get<T>(API_BASE + url, { params: params(query), headers: this.headers(false) });
        return this.send(request, signal, true, false);
    }

    private post<T>(url: string, body: unknown = null, signal?: AbortSignal, redirect = true): Promise<T> {
        return this.send(this.http.post<T>(API_BASE + url, body, { headers: this.headers(true) }), signal, redirect);
    }

    private put<T>(url: string, body: unknown): Promise<T> {
        return this.send(this.http.put<T>(API_BASE + url, body, { headers: this.headers(true) }));
    }

    private patch<T>(url: string, body: unknown): Promise<T> {
        return this.send(this.http.patch<T>(API_BASE + url, body, { headers: this.headers(true) }));
    }

    private delete<T>(url: string): Promise<T> {
        return this.send(this.http.delete<T>(API_BASE + url, { headers: this.headers(true) }));
    }

    /**
     * Requests changing something carry `X-SFE-Request`: a page of another site cannot add it without the server's
     * consent (CORS), so it cannot act with this browser's session.
     */
    private headers(changes: boolean): HttpHeaders {
        // The change feed tells other tabs of this tab's changes, not this one.
        let headers = new HttpHeaders().set('X-SFE-Tab', TAB_ID);
        if (changes) headers = headers.set('X-SFE-Request', '1');
        if (this.tenantId !== null) headers = headers.set('X-SFE-Tenant', String(this.tenantId));
        // A new install is set up without the setup code only from the browser SFE opened (or its console's link).
        if (SETUP_LINK !== null) headers = headers.set('X-SFE-Setup-Link', SETUP_LINK);
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
     * answers its own computer only (403 `local-only`) always opens the page saying so. A 409 resolves with its body,
     * unless `resolve409` is off.
     */
    private send<T>(request: Observable<T>, signal?: AbortSignal, redirect = true, resolve409 = true): Promise<T> {
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
                    } else if (resolve409 && error instanceof HttpErrorResponse && error.status === 409)
                        resolve(error.error as T);
                    else {
                        if (
                            error instanceof HttpErrorResponse &&
                            (redirect || type === 'migration-required' || type === 'local-only')
                        )
                            redirectAfterRefusal(this.injector.get(Router), error.status, type);
                        reject(toApiError(error));
                    }
                },
            });
            signal?.addEventListener(
                'abort',
                () => {
                    subscription.unsubscribe();
                    reject(new ApiError(0, 'Cancelled'));
                },
                { once: true },
            );
        });
    }
}
