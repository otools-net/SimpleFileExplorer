import type { KeepAliveMethod, KeepAliveMode } from '../core/models';
import type { StreamingMode } from '../core/config.models';

/** A folder kept alive (api/keep-alive). */
export interface KeepAliveFolder {
    /** Relative to the library root; empty for the root itself. */
    path: string;
    /** The library folder itself; the others are the more folders of the settings. */
    isRoot: boolean;
    /** Null until the first ping looked. */
    isNetwork: boolean | null;
    lastPing: string | null;
    durationMilliseconds: number | null;
    /** Why the last ping failed; null when it worked. */
    error: string | null;
    failingSince: string | null;
    /** A ping is still blocked on the folder. */
    waiting: boolean;
    /** How the last ping was made; null: none yet. */
    method: KeepAliveMethod | null;
}

export interface KeepAliveView {
    mode: KeepAliveMode;
    intervalSeconds: number;
    /** How the library folder is to be pinged (more folders are always read). */
    method: KeepAliveMethod;
    folders: KeepAliveFolder[];
}

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
