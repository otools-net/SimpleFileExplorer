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
}

export interface RemovedDuplicate {
    removed: string;
    kept: FileTags;
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
export type TaskKind = 'scan' | 'trickplay' | 'similarVideos' | 'thumbnails';
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
}

/** A backup made on the update page: downloaded once from `url`. */
export interface BackupLink {
    url: string;
    fileName: string;
    size: number;
}

export interface SetupInput {
    code: string;
    userName: string;
    password: string;
    displayName?: string | null;
    libraryName?: string | null;
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
}

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
}

/** A link to a file working without signing in, for a while (external players). */
export interface FileLink {
    /** Relative to the app. */
    url: string;
    expires: string;
}
