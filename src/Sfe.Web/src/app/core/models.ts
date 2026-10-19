/*
 * The JSON shapes of the SFE API (see the records in Sfe.Core and Sfe.Api). Enums come as camelCase strings, dates
 * as ISO 8601 strings.
 */

export type ItemKind = 'directory' | 'image' | 'video' | 'audio' | 'file';
export type SortField = 'name' | 'modified' | 'size' | 'type' | 'path' | 'duration' | 'resolution';
export type Theme = 'system' | 'light' | 'dark';

export interface TagSummary {
    id: number;
    name: string;
    displayName: string;
    color: string | null;
    isCategory: boolean;
    isHidden: boolean;
}

export interface TagDetails extends TagSummary {
    shorthand: string | null;
    isBuiltIn: boolean;
    disambiguationId: number | null;
    aliases: string[];
    parents: TagSummary[];
    fileCount: number;
}

export interface TagInput {
    name: string;
    shorthand?: string | null;
    aliases?: string[];
    parentIds?: number[];
    disambiguationId?: number | null;
    color?: string | null;
    isCategory?: boolean;
    isHidden?: boolean;
}

export interface TagSearchResults {
    matches: TagDetails[];
    descendants: TagDetails[];
}

export interface TagGroup {
    category: TagSummary | null;
    tags: TagSummary[];
}

export interface FileTags {
    path: string;
    tags: TagSummary[];
    groups: TagGroup[];
}

export interface FileTagChange {
    paths: string[];
    add?: number[];
    remove?: number[];
}

export interface FileTagChangeResult {
    added: number;
    removed: number;
    files: FileTags[];
}

export interface TagUsage {
    tag: TagSummary;
    fileCount: number;
}

/** Files tagged together without listing them: those of a folder (`recursive`: with its sub-folders), or a search's matches (`query`). */
export interface TagScope {
    path: string;
    recursive: boolean;
    query: string | null;
}

/** The indexed files of a scope and the tags on them, most common first. */
export interface ScopeTags {
    scope: TagScope;
    files: number;
    tags: TagUsage[];
}

export interface ScopeTagChange extends TagScope {
    add?: number[];
    remove?: number[];
}

export interface ScopeTagChangeResult {
    scope: TagScope;
    files: number;
    added: number;
    removed: number;
}

export interface TagSidebar {
    popular: TagUsage[];
    searched: TagUsage[];
    viewed: TagUsage[];
    lucky: TagUsage[];
    historyEnabled: boolean;
}

export interface TagColor {
    slug: string;
    name: string;
    hex: string;
}

/** A folder or file kept in the sidebar's quick links (`api/me/quick-links`). */
export interface QuickLink {
    path: string;
    name: string;
    /** `directory`, or the kind of file by its extension, as it was when added. */
    kind: ItemKind;
    extension: string | null;
    /** False once it was moved, renamed or deleted. */
    exists: boolean;
}

export interface LibraryItem {
    name: string;
    path: string;
    kind: ItemKind;
    extension: string | null;
    size: number | null;
    modified: string | null;
    /** Search results only: the folder of the file ('' = the root). */
    directory?: string | null;
    tags?: TagSummary[] | null;
    /** Pixels of an image or video, when the index knows them. */
    width?: number | null;
    height?: number | null;
    /** Seconds of a video or audio file, when the index knows them. */
    duration?: number | null;
    /** Browsing only: false for a file or folder no scan has indexed yet; null/absent when unknown (search results). */
    indexed?: boolean | null;
    /** Browsing only, for an indexed folder: how many files and sub-folders directly inside it the index has. */
    indexedItems?: number | null;
}

/**
 * Why a file operation was not done although the request was fine (409): something of that name is there
 * (`file-exists`), a scan runs over the folder (`scan-running`), folder rules are set where a folder would go
 * (`folder-rules`).
 */
export interface FileConflict {
    type: 'file-exists' | 'scan-running' | 'folder-rules' | string;
    detail?: string | null;
}

/** The answer of a file operation: the new or renamed item, or why it was not done. */
export type FileOperationResult = LibraryItem | FileConflict;

export function isFileConflict(result: FileOperationResult): result is FileConflict {
    return !('path' in result);
}

export type DeleteStatus = 'done' | 'notFound' | 'forbidden' | 'scanRunning' | 'trashUnavailable' | 'failed' | string;

/** What became of one path of a delete; `trashId` when it went to the trash (for an Undo). */
export interface DeletedItem {
    path: string;
    status: DeleteStatus;
    message?: string | null;
    trashId?: number | null;
}

export interface DeleteResults {
    items: DeletedItem[];
    /** It skipped the trash (asked, or the space always does). */
    permanent: boolean;
}

export type FileJobKind = 'move' | 'copy';
export type FileJobState = 'running' | 'waiting' | 'completed' | 'cancelled';
/** What a job does when the target holds an item of the same name; `ask`: it waits for the answer. */
export type ConflictChoice = 'ask' | 'replace' | 'skip' | 'keepBoth';

export interface FileJobRequest {
    kind: FileJobKind;
    paths: string[];
    target: string;
    conflicts?: ConflictChoice;
    /** Moved folders keep the access each group and account had. */
    keepAccess?: boolean;
    /** Copies get the tags of their originals. */
    keepTags?: boolean;
}

/** An item on one side of a name clash. */
export interface ClashSide {
    name: string;
    isFolder: boolean;
    size: number | null;
    modified: string | null;
}

export interface FileJobConflict {
    source: ClashSide;
    existing: ClashSide;
    /** The session may replace it (it would go to the trash). */
    canReplace: boolean;
}

/** What became of one item of a job; `newPath`: where it is now (moved, or the copy). */
export interface FileJobItem {
    path: string;
    newPath: string | null;
    status: DeleteStatus | 'skipped' | 'invalid' | 'conflict';
    message?: string | null;
}

/** A move or copy running in the background, or done. */
export interface FileJobView {
    id: number;
    kind: FileJobKind;
    state: FileJobState;
    target: string;
    total: number;
    done: number;
    bytesTotal: number;
    bytesDone: number;
    current: string | null;
    conflict: FileJobConflict | null;
    results: FileJobItem[];
    started: string;
    finished: string | null;
}

/** An upload not finished yet, as the server has it (`received` bytes so far). */
export interface UploadInfo {
    id: string;
    target: string;
    /** The file's name, or its path below `target` for a folder upload. */
    name: string;
    size: number;
    received: number;
    modified: string | null;
    ownerId: number | null;
    ownerName: string;
    started: string;
    lastActivity: string;
}

/** The names already taken in a folder, and whether the session may replace them. */
export interface UploadCheck {
    existing: string[];
    canReplace: boolean;
}

/** Someone who would see or change more of a moved folder than before. */
export interface AccessGain {
    folder: string;
    who: string;
    isGroup: boolean;
    before: FolderLevel;
    after: FolderLevel;
}

/** A file or folder in the trash. `path`: where it was. */
export interface TrashItem {
    id: number;
    path: string;
    name: string;
    isFolder: boolean;
    size: number | null;
    files: number | null;
    deletedAt: string;
    deletedBy: string | null;
    deletedById: number | null;
    hasRules?: boolean;
    /** The content of a file before a save over it (text editor); the file itself is still there. */
    previousVersion?: boolean;
}

export interface TrashPage {
    items: TrashItem[];
    hasMore: boolean;
    /** The `before` of the next page. */
    next: number | null;
    /** Days in the trash before the purge; 0 = until emptied. */
    retentionDays: number;
}

export interface DirectoryListing {
    path: string;
    name: string;
    parentPath: string | null;
    items: LibraryItem[];
    offset: number;
    limit: number;
    total: number;
    directoryCount: number;
    fileCount: number;
    /** What folder rules let the session do here; absent or null when none apply (every right of the space). */
    access?: FolderAccessView | null;
}

/** What folder rules let the session do in a folder. */
export interface FolderAccessView {
    rights: string[];
    /** The session doesn't see the folder: it only leads to folders below that it sees. */
    pathOnly: boolean;
    /** The entries whose access differs from the folder's, by name; the others have the folder's. */
    children?: Record<string, FolderAccessView> | null;
}

export interface SearchResults {
    query: string;
    path: string;
    items: LibraryItem[];
    limit: number;
    hasMore: boolean;
    next: string | null;
    /** Matches counted on the first page (null on the next ones), at most 10,000: `totalCapped` when there are more. */
    total?: number | null;
    totalCapped?: boolean;
}

export type ListingPage = DirectoryListing | SearchResults;

export interface FolderSiblings {
    path: string;
    previous: LibraryItem | null;
    next: LibraryItem | null;
    position: number;
    count: number;
}

export interface FolderCover {
    path: string;
    items: LibraryItem[];
}

export interface ClientConfig {
    rootName: string;
    dataServer: string | null;
    pageSize: number;
    searchPageSize: number;
    features: { thumbnails: boolean; trickplay: boolean; shutdown: boolean; dismount: boolean };
    tagColors: TagColor[];
    maxFilesPerTagChange: number;
    largeImageSize: number;
    seekSeconds: number;
    /** Videos start over at their end until the player's loop button is switched off (Player:Loop). */
    loop: boolean;
    trickplayHover: boolean;
    folderPictures: number;
    searchExplain: boolean;
    /** Searches each browser keeps and suggests in the search box (Search:HistorySize); 0 = none. */
    searchHistory: number;
    /** The "one per row" view shows images themselves, not their thumbnails (Thumbnails:SingleViewOriginals). */
    singleViewOriginals: boolean;
    /** Hovering an animated image shown through its thumbnail plays the original (Thumbnails:AnimatedHover). */
    animatedHover: boolean;
    /** Listings and the viewer show the resolution of images and videos (Browse:ShowResolution). */
    showResolution: boolean;
    /** Listings and the viewer show the duration of videos and audio files (Browse:ShowDuration). */
    showDuration: boolean;
    /** Smallest shorter side, in pixels, of a high-resolution image and video (HR/LR marks on previews); null = no marks. */
    resolutionMark: { image: number; video: number } | null;
    /** The library root on the server, ending with its separator; null unless Browse:CopyPaths is on. */
    rootPath: string | null;
    externalPlayer: { name: string; url: string } | null;
    theme: Theme;
    version: string;
    commit: string | null;
    /** What the session may do in its space (see Rights on the server): the client hides the rest. */
    rights: string[];
    /** The signed-in account; null for a visitor without one. */
    user: SessionAccount | null;
    /** The space being browsed. */
    tenant: SessionTenant | null;
    /** The spaces the account can switch to (empty for a visitor without one). */
    tenants: SessionTenant[];
    /** The picture editor's settings (Editor:*); null without the right to edit pictures (and from older servers). */
    editor?: EditorConfig | null;
    /**
     * Videos the browser can't play are converted by the server (`always`: every video is); null when streaming is off,
     * ffmpeg is missing or without the right `files.stream` (and from older servers).
     */
    streaming?: { always: boolean } | null;
    /** How deleting works in the space; null without a right to delete (and from older servers). */
    files?: { deletePermanently: boolean; trashRetentionDays: number; maxUploadMegabytes?: number; keepUnfinishedUploadsHours?: number } | null;
    /**
     * The reading settings of text files: the library's, with the account's own over them (`ReadingPreferences` of
     * `formats/reading/reading.ts`, checked there); absent from older servers.
     */
    reading?: unknown;
    /** The library's own extensions shown as text (`Sfe:FileTypes:Text`); absent from older servers. */
    textExtensions?: string[] | null;
}

/** `api/streaming/probe`: a video the server can convert. */
export interface StreamProbe {
    /** Seconds; null when unknown. */
    duration: number | null;
    /** Its pictures are encoded again (false: copied, lighter, but seeking lands on key frames). */
    transcoded: boolean;
}

export type StreamingMode = 'off' | 'fallback' | 'always';

/** A video being converted for a viewer. */
export interface StreamActivity {
    id: string;
    /** Relative to its library's root. */
    path: string;
    viewer: { library: string; user: string | null; address: string | null };
    /** Position (seconds) the conversion started from. */
    start: number;
    transcoded: boolean;
    started: string;
    /** Sent so far. */
    bytes: number;
}

export interface StreamingStatus {
    /** ffmpeg was found and streaming is on in this library. */
    available: boolean;
    mode: StreamingMode;
    /** Videos the whole server converts at once. */
    maxStreams: number;
    streams: StreamActivity[];
}

/** `api/system/info`: what the server administrators see in System › About. */
export interface SystemInfo {
    /** Full path of the library root on the server. */
    root: string;
    /** `path` null: not found where `configured` says. */
    ffmpeg: { configured: string; path: string | null; version: string | null };
}

export interface EditorConfig {
    /** Library folder edited pictures are saved in, under their originals' folders; '' = next to the originals. */
    outputFolder: string;
    /** Quality (1-100) of the JPEG and WebP pictures the editor encodes. */
    quality: number;
}

/** A picture saved by the editor: a new file of the library. */
export interface EditedImage {
    path: string;
    width: number;
    height: number;
    size: number;
}

/** Saving an edited picture over its original: its version, and whether this account may (`api/editor/images/access`). */
export interface EditedImageAccess {
    version: string;
    /** files.edit at its folder and the rule of Replace, and a JPEG, PNG or WebP file. */
    overwrite: boolean;
    /** The space deletes for good: no previous version is kept in the trash. */
    permanent: boolean;
}

/** A picture saved over: its new size, version and time; `trashId`: the version before, kept in the trash. */
export interface SavedImage extends EditedImage {
    version: string;
    modified: string;
    trashId: number | null;
}

/** The picture saved over, or why not (a 409: a link, a read-only file, a scan). */
export type SaveImageResult = SavedImage | FileConflict;

export function isSavedImage(result: SaveImageResult): result is SavedImage {
    return 'width' in result;
}

export type ScanState = 'idle' | 'running' | 'completed' | 'cancelled' | 'failed';

export interface ScanStatus {
    state: ScanState;
    path: string | null;
    startedAt: string | null;
    finishedAt: string | null;
    directoriesScanned: number;
    directoriesQueued: number;
    filesIndexed: number;
    removed: number;
    errors: number;
    currentDirectory: string | null;
    message: string | null;
    relinked: number;
    missing: number | null;
}

export type TrickplayState = 'disabled' | 'unavailable' | 'idle' | 'running';
export type TrickplayFileState = 'pending' | 'running';

export interface TrickplayStatus {
    state: TrickplayState;
    checked: number;
    generated: number;
    failed: number;
    queued: number;
    pending: number;
    discovering: boolean;
    current: string[];
    lastPassFinished: string | null;
    passStarted?: string | null;
    nextPass?: string | null;
}

export interface TrickplayActivity {
    status: TrickplayStatus;
    running: { path: string; started: string }[];
    queued: string[];
    pending: string[];
    recent: { path: string; succeeded: boolean; error: string | null; finished: string; seconds: number }[];
}

export interface TrickplayStates {
    running: boolean;
    files: Record<string, TrickplayFileState>;
}

export interface TrickplayManifest {
    interval: number;
    count: number;
    columns: number;
    rows: number;
    tileWidth: number;
    tileHeight: number;
    sheets: number;
    version: number;
}

export interface DuplicateGroup {
    size: number;
    fingerprint: string;
    count: number;
    wastedBytes: number;
    files: LibraryItem[];
}

export interface DuplicateSummary {
    groups: number;
    files: number;
    wastedBytes: number;
    unfingerprinted: number;
}

export interface DuplicateResults {
    path: string;
    groups: DuplicateGroup[];
    hasMore: boolean;
    next: string | null;
    summary: DuplicateSummary | null;
    fingerprinting: boolean;
    minimumSize: number;
    canRemove: boolean;
    /** A copy removed is deleted for good, not moved to the trash (`Files:DeletePermanently`). */
    removesPermanently?: boolean;
}

export interface RemovedDuplicate {
    removed: string;
    kept: FileTags;
    /** Its entry in the trash, for an Undo; null when it was deleted for good. */
    trashId?: number | null;
}

export type SimilarVideoState = 'idle' | 'running' | 'completed' | 'cancelled' | 'failed';

export interface SimilarVideoResults {
    path: string;
    status: { state: SimilarVideoState; checked: number; total: number; started: string | null; finished: string | null; message: string | null };
    summary: { groups: number; files: number; wastedBytes: number; videos: number; compared: number; minimumPercent: number } | null;
    groups: { count: number; wastedBytes: number; videos: { file: LibraryItem; match: number | null; duration: number }[] }[];
    hasMore: boolean;
    next: string | null;
    trickplayEnabled: boolean;
}

export interface MissingFileInfo {
    id: number;
    path: string;
    name: string;
    size: number;
    modified: string;
    missingSince: string;
    tags: TagSummary[];
    candidates: LibraryItem[];
}

export interface MissingFileResults {
    items: MissingFileInfo[];
    total: number | null;
    hasMore: boolean;
    next: number | null;
    enabled: boolean;
}

export interface RelinkPassResult {
    relinked: number;
    forgotten: number;
    remaining: number;
}

/** The editable settings, nested as in EditableSettings ("browse.defaultPageSize"...). */
export type SettingsValues = Record<string, unknown>;

/** Where a setting's value comes from: the configuration, the server's settings page, a space, a user. */
export type SettingLevel = 'environment' | 'server' | 'tenant' | 'user';

/** How a setting shows on the settings page of a level. */
export interface SettingState {
    /** The most specific level that may set it. */
    scope: SettingLevel;
    source: SettingLevel;
    editable: boolean;
    /** This level may lock it for the levels below. */
    lockable: boolean;
    /** This level locked it. */
    locked: boolean;
    /** The level above that locked it. */
    lockedBy: SettingLevel | null;
    /** The value of the levels above. */
    inherited: unknown;
}

/** Some settings of a level to save (`PATCH api/settings`): the others keep what the level has. */
export interface SettingChanges {
    /** New values by setting key (`player.seekSeconds`). */
    values?: Record<string, unknown>;
    /** Settings the level starts locking for the levels below. */
    lock?: string[];
    /** Settings the level stops locking. */
    unlock?: string[];
}

export interface SettingsView {
    values: SettingsValues;
    customized: boolean;
    level?: SettingLevel;
    /** Per setting key; absent from older servers. */
    keys?: Record<string, SettingState> | null;
}

export interface CommandResult {
    output: string;
}

/** The background jobs of the "Background tasks" dialog. */
export type TaskKind = 'scan' | 'trickplay' | 'similarVideos' | 'thumbnails' | 'trashPurge';
export type TaskState = 'idle' | 'running' | 'disabled' | 'unavailable';
export type TaskOutcome = 'running' | 'completed' | 'cancelled' | 'failed' | 'interrupted' | 'skipped';
export type TaskTrigger = 'manual' | 'schedule' | 'automatic';
/** `weekly` with every day is shown as "every day". */
export type ScheduleKind = 'once' | 'interval' | 'weekly';

export interface TaskRunSummary {
    id: number;
    task: TaskKind;
    /** Folder a scan covered ('' for the library). */
    path: string | null;
    trigger: TaskTrigger;
    scheduleId: number | null;
    started: string;
    finished: string | null;
    outcome: TaskOutcome;
    summary: string | null;
}

export interface TaskLogLine {
    time: string;
    level: string;
    message: string;
}

export interface TaskRunDetails extends TaskRunSummary {
    log: TaskLogLine[];
    /** Lines left out in the middle of a long log. */
    omitted: number;
}

/** The severities of the server's log, from the least to the most severe. */
export type LogLevelName = 'trace' | 'debug' | 'information' | 'warning' | 'securityWarning' | 'error' | 'critical';

export interface LogEntry {
    id: number;
    timestamp: string;
    level: LogLevelName;
    /** The logger's name, e.g. Sfe.Core.Indexing.ScanCoordinator. */
    category: string;
    message: string;
    exception: string | null;
    /** The space whose services logged it; null for the server's own entries. */
    tenantId: number | null;
}

export interface LogQuery {
    q: string | null;
    /** Comma-separated LogLevelName values; null = every level. */
    levels: string | null;
    /** ISO moments; `to` excluded. */
    from: string | null;
    to: string | null;
    /** A space's id; 0 = the server's own entries (server administrators only). */
    tenant: number | null;
    before: number | null;
}

export interface LogSearchResult {
    /** Newest first. */
    entries: LogEntry[];
    /** `before` of the next (older) page; null on the last one. */
    next: number | null;
    retentionDays: number;
    writeFiles: boolean;
    /** Where the daily files are (server administrators only). */
    filesDirectory: string | null;
    /** The session sees the whole server's log; otherwise only its space's. */
    allSpaces: boolean;
}

export interface TaskRunPage {
    runs: TaskRunSummary[];
    /** `before` of the next (older) page; null on the last one. */
    next: number | null;
}

export interface TaskInfo {
    task: TaskKind;
    state: TaskState;
    path: string | null;
    startedAt: string | null;
    /** 0 to 1 when the total is known. */
    progress: number | null;
    detail: string;
    canStart: boolean;
    canCancel: boolean;
    canSchedule: boolean;
    /** When it runs by itself next (trickplay's periodic check). */
    automaticNextRun: string | null;
    /** Hours between those runs. */
    automaticEvery: number | null;
    lastRun: TaskRunSummary | null;
}

export interface TaskScheduleInput {
    task: TaskKind;
    path: string | null;
    kind: ScheduleKind;
    /** ISO date: the first run, or the time of day (server clock) of a weekly schedule. */
    start: string;
    intervalMinutes: number;
    /** Weekly days: bit 0 = Sunday … bit 6 = Saturday. */
    days: number;
    enabled: boolean;
}

export interface TaskSchedule extends TaskScheduleInput {
    id: number;
    nextRun: string | null;
    lastRun: string | null;
}

export interface TaskOverview {
    tasks: TaskInfo[];
    schedules: TaskSchedule[];
    serverTime: string;
    /** The server's time zone (IANA id when known): weekly schedules follow its clock. */
    timeZone: string;
    logRetentionDays: number;
}

export type TaskStartResult = 'started' | 'alreadyRunning' | 'unavailable';

export interface TaskActionResult {
    result: TaskStartResult;
    task: TaskInfo;
}

/** What the index holds of a folder and its sub-folders. */
export interface IndexedFolder {
    path: string;
    indexed: boolean;
    directories: number;
    files: number;
    taggedFiles: number;
    /** In the scans' excluded folders. */
    excluded: boolean;
}

export interface RemovedFromIndex {
    path: string;
    directories: number;
    files: number;
    taggedFiles: number;
    excluded: boolean;
}

export type UserKind = 'user' | 'guest';
export type BuiltInGroup = 'administrators' | 'users' | 'guests';

export interface SessionAccount {
    id: number;
    userName: string;
    displayName: string | null;
    kind: UserKind;
    isServerAdmin: boolean;
    /** A local account: its password can be changed here (a directory's is changed in the directory). */
    local: boolean;
}

export interface SessionTenant {
    id: number;
    slug: string;
    name: string;
}

/** /api/session: who is signed in, in which space, and what the sign-in page offers. */
export interface SessionInfo {
    user: SessionAccount | null;
    tenant: SessionTenant | null;
    /** The spaces the account can switch to. */
    tenants: SessionTenant[];
    /** Spaces open to visitors without an account. */
    guestTenants: SessionTenant[];
    setupRequired: boolean;
    setupAvailable: boolean;
    canBrowse: boolean;
}

export type HostingKind = 'windowsService' | 'systemd' | 'iis' | 'docker' | 'appImage' | 'portable' | 'other';

/** /api/setup: whether the server still needs its first administrator, and whether it has a setup code. */
export interface SetupInfo {
    required: boolean;
    available: boolean;
    codeConfigured: boolean;
    codeTooShort: boolean;
    recover: boolean;
    minimumCodeLength: number;
    minimumPasswordLength: number;
    libraryName: string;
    hosting: HostingKind;
    /** The code is the one the server made itself: shown in its console and kept in `codeFile`. */
    codeGenerated?: boolean;
    /** Where that code is kept, as configured (usually relative to SFE's folder). */
    codeFile?: string | null;
    /** The setup asks this visitor for the code; false for a new install set up from the server's own computer (missing from older servers: true). */
    codeRequired?: boolean;
}

/** none = the install belongs to this version; the others keep everything but the update page closed (503 `migration-required`). */
export type MigrationStatus = 'none' | 'required' | 'running' | 'failed' | 'newer' | 'unavailable';

/** A version of CHANGELOG.md: its sections (Added, Changed, Fixed), each item in the changelog's Markdown. */
export interface ChangelogEntry {
    version: string;
    sections: { title: string; items: string[] }[];
}

/** /api/migration: whether the install waits for its update to this version, and what changed since its version. */
export interface MigrationInfo {
    status: MigrationStatus;
    appVersion: string;
    /** null: unknown (installs before 1.37.0 recorded none). */
    databaseVersion: string | null;
    /** The versions after the install's up to this one, newest first. */
    changes: ChangelogEntry[];
    log: string[];
    error: string | null;
    codeConfigured: boolean;
    codeTooShort: boolean;
    minimumCodeLength: number;
    hosting: HostingKind;
    codeGenerated?: boolean;
    codeFile?: string | null;
}

/** What the disk says of a file or folder (Properties). Dates are ISO, null when the file system doesn't keep them. */
export interface ItemDetails {
    path: string;
    name: string;
    isFolder: boolean;
    size: number | null;
    created: string | null;
    modified: string;
    accessed: string | null;
    /** A name starting with a dot, or the system's hidden attribute. */
    hidden: boolean;
    readOnly: boolean;
    /** A symbolic link or junction (where it leads is not told). */
    isLink: boolean;
}

export interface ItemProperties {
    item: LibraryItem;
    details: ItemDetails;
    /** May read the file whole for a checksum (right to download it). */
    canChecksum: boolean;
}

/** A text file as the viewer reads it (`api/files/text`). */
export interface TextFile {
    /** Line endings as they are in the file. */
    text: string;
    encoding: 'utf-8' | 'utf-8-bom' | 'utf-16le' | 'utf-16be' | 'windows-1252' | string;
    lineEnding: 'lf' | 'crlf' | 'cr' | 'mixed' | string;
    /** The content's version, which a save will name. */
    version: string;
    size: number;
    modified: string;
    /** What the session may do with it (from the server). */
    access?: TextAccess | null;
    /** The line ending a save writes: the file's, or the most used one of a file that mixes them. */
    saveLineEnding?: 'lf' | 'crlf' | 'cr' | string | null;
}

/** A part of a text file (`api/files/text/part`): from `offset` to `end` bytes of `size`. */
export interface TextPart {
    text: string;
    offset: number;
    end: number;
    size: number;
    encoding: string;
    modified: string;
}

/** A text file's version now (`api/files/text/version`); `version` null for one too large to be read whole. */
export interface TextVersion {
    version: string | null;
    size: number;
    modified: string;
}

/** The video a subtitle file belongs to (`api/files/text/video`): null when none the session sees. */
export interface SubtitleVideo {
    path: string | null;
}

/** What saving a text file may do (`api/files/text`). */
export interface TextAccess {
    /** Save over it: may delete it and create files in its folder (as move/copy's Replace). */
    overwrite: boolean;
    /** Save a copy in its folder. */
    saveCopy: boolean;
    download: boolean;
    /** The space deletes for good: no previous version is kept in the trash. */
    permanent: boolean;
}

/** A text saved over its file: its new version; `trashId` when this save kept the previous version in the trash. */
export interface SavedText {
    version: string;
    size: number;
    modified: string;
    trashId?: number | null;
}

/** Why a save over a file was not done although the request was fine (409): `file-is-link`, `read-only`, `scan-running`. */
export type SaveTextResult = SavedText | FileConflict;

export function isSaved(result: SaveTextResult): result is SavedText {
    return 'version' in result;
}

/** A text to save: in `encoding`, its lines ending with `lineEnding`. */
export interface TextToSave {
    text: string;
    encoding: string;
    lineEnding: string;
}

/** What the index knows below a folder, among what the session sees. */
export interface FolderTotals {
    indexed: boolean;
    files: number;
    folders: number;
    bytes: number;
}

export type ChecksumAlgorithm = 'sha256' | 'sha1' | 'md5';

/** A folder counted on the disk, or a file's checksum, running on the server. */
export interface PropertyJob {
    id: string;
    kind: 'count' | 'checksum';
    path: string;
    algorithm: ChecksumAlgorithm | null;
    done: boolean;
    files: number;
    folders: number;
    /** Counted, or read so far for a checksum. */
    bytes: number;
    /** The file's size, for a checksum. */
    total: number | null;
    /** Links not followed and folders that couldn't be read. */
    leftOut: number;
    /** The count stopped at its limit: the folder holds more. */
    capped: boolean;
    checksum: string | null;
    error: string | null;
}

/** A zip of several files or folders: the browser downloads it once from `url`, soon. */
export interface ArchiveLink {
    url: string;
    fileName: string;
}

/** A backup made on the update page: downloaded once from `url`. */
export interface BackupLink {
    url: string;
    fileName: string;
    size: number;
}

/** A setting of the installation page; `locked` when the server's environment (variables, command line) decides it. */
export interface InstallationField {
    value: string | null;
    locked: boolean;
    lockedBy: string | null;
}

/** /api/installation: the library folder, listening address and ffmpeg of the server. */
export interface InstallationView {
    root: InstallationField;
    rootExists: boolean;
    urls: InstallationField;
    port: number;
    /** Listens for other devices of the network, not only this computer. */
    network: boolean;
    ffmpegPath: InstallationField;
    ffmpegFound: string | null;
    ffmpegVersion: string | null;
    /** The address saved applies once SFE starts again. */
    restartRequired: boolean;
    /** `Sfe:Mode` as configured; a null value = not chosen yet (missing from older servers). */
    mode?: InstallationField;
    /** The mode the server works in: without one configured, Local. */
    modeInEffect?: ServerMode;
    /** The visitor is on the server's own computer: only they may choose Local. */
    onThisComputer?: boolean;
}

/** How the server is used: on this computer by its administrators, or by other people over the network. */
export type ServerMode = 'local' | 'external';

/** What the installation page changes; a field left out stays. `ffmpegPath` empty = look for it on PATH. */
export interface InstallationInput {
    root?: string | null;
    network?: boolean | null;
    port?: number | null;
    ffmpegPath?: string | null;
    mode?: ServerMode | null;
}

export interface InstallationGrant {
    /** Sent back in `X-SFE-Installation` for an hour. */
    token: string;
    settings: InstallationView;
}

export interface InstallationSaved {
    settings: InstallationView;
    restartRequired: boolean;
}

export interface SetupInput {
    code: string;
    userName: string;
    password: string;
    displayName?: string | null;
    libraryName?: string | null;
    /** Sign the administrator in at once in the browser SFE opens when it starts (server setting `fastSignIn`). */
    fastSignIn?: boolean;
}

export interface RightDefinition {
    id: string;
    scope: 'tenant' | 'server';
    /** Guests may get it. */
    guestAllowed: boolean;
    defaultGroups: BuiltInGroup[];
    area: string;
    title: string;
    description: string;
    /** `write` when one of its functions changes something (files, index, tags, settings, accounts). */
    kind: RightKind;
    /** What a folder must allow for it to apply there; null for a right of the whole space. */
    atFolder: FolderLevel | null;
}

export type RightKind = 'read' | 'write';

/** A folder rule: what a group, or one account, may do in a folder and below it. */
export interface FolderRuleView {
    id: number;
    path: string;
    groupId: number | null;
    userId: number | null;
    /** The group's name, or the account's. */
    name: string;
    level: FolderLevel;
    /** The folder is still there (false once moved or renamed outside SFE). */
    exists: boolean;
    created: string;
}

/** Sets a group's or an account's level at a folder; `level` null removes its rule there. */
export interface FolderRuleChange {
    path: string;
    groupId: number | null;
    userId: number | null;
    level: FolderLevel | null;
}

export interface FolderAccessEntry {
    groupId: number | null;
    userId: number | null;
    name: string;
    /** A group's level there; an account's own rule's (null: its groups decide). */
    level: FolderLevel | null;
    /** From a rule on this very folder. */
    explicit: boolean;
    /** May browse the folder, all things counted. */
    sees: boolean;
    /** May change its files. */
    changesFiles: boolean;
}

/** Who may see and change a folder. */
export interface FolderAccessReport {
    path: string;
    exists: boolean;
    groups: FolderAccessEntry[];
    accounts: FolderAccessEntry[];
    /** Visitors without an account, when the space lets them in. */
    visitors: FolderAccessEntry | null;
}

/** What a folder lets a subject do in it: `write` includes `read`; only `write` lets its files on disk change. */
export type FolderLevel = 'hidden' | 'read' | 'write';

export interface RightsCatalog {
    rights: RightDefinition[];
    /** Switched off for the whole server (Sfe:Rights:Disabled). */
    disabledByServer: string[];
    /** Switched off in the space asked about. */
    disabledInTenant: string[];
}

export interface Membership {
    tenantId: number;
    groupId: number;
    group: string;
}

export interface UserInfo {
    id: number;
    userName: string;
    displayName: string | null;
    source: string;
    kind: UserKind;
    isServerAdmin: boolean;
    disabled: boolean;
    lockedOut: boolean;
    created: string;
    lastSignIn: string | null;
    groups: Membership[];
}

export interface UserInput {
    userName: string;
    displayName?: string | null;
    /** Required for a new account; null keeps the current one. */
    password?: string | null;
    kind: UserKind;
    isServerAdmin: boolean;
    disabled: boolean;
    groupIds?: number[] | null;
    unlock?: boolean;
}

export interface GroupInfo {
    id: number;
    tenantId: number;
    name: string;
    builtIn: BuiltInGroup | null;
    externalName: string | null;
    rights: string[];
    memberIds: number[];
}

export interface GroupInput {
    name: string;
    externalName?: string | null;
    rights?: string[] | null;
    memberIds?: number[] | null;
}

export interface TenantAccess {
    id: number;
    name: string;
    guestAccess: boolean;
    /** The server lets spaces open to visitors without an account (Sfe:Security:AnonymousGuests). */
    anonymousGuestsAllowed: boolean;
    disabledRights: string[];
}

export interface TenantDetails {
    id: number;
    slug: string;
    name: string;
    guestAccess: boolean;
    isDefault: boolean;
    disabled: boolean;
    /** Full path of its library folder on the server. */
    root: string;
    disabledRights: string[];
    /** Accounts in at least one of its groups. */
    members: number;
    /** Its groups, the built-in ones included. */
    groups: number;
}

export interface TenantInput {
    name: string;
    slug?: string | null;
    root?: string | null;
    guestAccess: boolean;
    disabled: boolean;
    disabledRights?: string[] | null;
}

export interface FolderChoice {
    name: string;
    path: string;
}

export interface FolderPicker {
    /** The folder listed; null for the allowed roots. */
    path: string | null;
    parent: string | null;
    folders: FolderChoice[];
    /** At the top of the installation page's picker: the home folder, the library folder now, Pictures... */
    places?: FolderChoice[];
    /** More sub-folders than the server lists. */
    truncated?: boolean;
}

/** A link to a file working without signing in, for a while (external players). */
export interface FileLink {
    /** Relative to the app. */
    url: string;
    expires: string;
}
