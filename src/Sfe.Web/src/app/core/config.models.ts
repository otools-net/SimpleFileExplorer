import type { FileConflict } from './models';
import type { ServerMode, SessionAccount, SessionTenant } from './access.models';
import type { TagColor } from './tag.models';

export type Theme = 'system' | 'light' | 'dark';

export interface ClientConfig {
    /** How the server is used (Sfe:Mode); missing from older servers. */
    mode?: ServerMode;
    rootName: string;
    dataServer: string | null;
    pageSize: number;
    searchPageSize: number;
    features: { thumbnails: boolean; trickplay: boolean };
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
    /**
     * Smallest shorter side, in pixels, of a high-resolution image and video (HR/LR marks on previews); null = no
     * marks.
     */
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
    files?: {
        deletePermanently: boolean;
        trashRetentionDays: number;
        maxUploadMegabytes?: number;
        keepUnfinishedUploadsHours?: number;
    } | null;
    /**
     * The reading settings of text files: the library's, with the account's own over them (`ReadingPreferences` of
     * `formats/reading/reading.ts`, checked there); absent from older servers.
     */
    reading?: unknown;
    /** The library's own extensions shown as text (`Sfe:FileTypes:Text`); absent from older servers. */
    textExtensions?: string[] | null;
    /**
     * Flash movies (`.swf`) play in the viewer (`Player:Flash` and the right to view files); absent from older servers.
     */
    flash?: boolean;
    /** Which archives the viewer explores; null without the right to view files (and from older servers: none). */
    archives?: { maxCompressedTarMegabytes: number } | null;
}

/** `api/streaming/probe`: a video the server can convert. */
export interface StreamProbe {
    /** Seconds; null when unknown. */
    duration: number | null;
    /** Its pictures are encoded again (false: copied, lighter, but seeking lands on key frames). */
    transcoded: boolean;
}

export type StreamingMode = 'off' | 'fallback' | 'always';

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

/** A picture saved over: its new size, version and time; `trashId`: the version before, kept in the trash. */
export interface SavedImage extends EditedImage {
    version: string;
    modified: string;
    trashId: number | null;
}

/** The picture saved over, or why not (a 409: a link, a read-only file, a scan). */
export type SaveImageResult = SavedImage | FileConflict;

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

/** A text saved over its file: its new version; `trashId` when this save kept the previous version in the trash. */
export interface SavedText {
    version: string;
    size: number;
    modified: string;
    trashId?: number | null;
}

/**
 * Why a save over a file was not done although the request was fine (409): `file-is-link`, `read-only`, `scan-running`.
 */
export type SaveTextResult = SavedText | FileConflict;
