import { computed, inject, Injectable, signal } from '@angular/core';
import { Api } from './api.service';
import { formatDuration, formatResolution } from './format';
import { ClientConfig, LibraryItem } from './models';
import { encodePath } from './paths';

/** The links of the files, built from the configuration (data server, external player). */
/** The mark on the preview of an image or video saying whether it is high resolution. */
export interface ResolutionMark {
    label: 'HR' | 'LR';
    high: boolean;
    /** The hint: the resolution and the threshold. */
    title: string;
}

export interface Urls {
    /** Direct link to the file: the data server when configured, the API otherwise. */
    file(path: string): string;
    download(path: string): string;
    /** Not cached by the browser (the server keeps them on disk), so the URL needs no version. */
    thumbnail(path: string): string;
    /** Versioned (the trickplay's `version`), so the browser may cache the sheets. */
    trickplaySheet(path: string, index: number, version: number): string;
    /** The video player alone, in a page of its own; `start`: position in seconds. */
    player(path: string, start?: number): string;
    /**
     * Link opening a file in the external player app (`config.externalPlayer`, e.g. `vlc://{url}`), with `fileUrl` (the
     * file's full address) in place of `{url}` (`{encodedUrl}`: percent-encoded); null when there is none. The address
     * is a link of its own (ConfigService.openExternal): the player fetches it without this browser's session.
     */
    external(fileUrl: string): string | null;
    /** Name of the external player, for its buttons. */
    externalName: string | null;
}

const PLACEHOLDERS = /\{(url|encodedUrl)\}/g;

/**
 * The file's address on the data server: `{url}` = the path, each segment encoded (slashes kept), `{encodedUrl}` =
 * the whole path encoded (slashes too, for a query string); a server address without either gets `/` + the path.
 */
export function dataServerUrl(dataServer: string, path: string): string {
    return dataServer.search(PLACEHOLDERS) >= 0
        ? dataServer.replace(PLACEHOLDERS, (_, name) => name === 'url' ? encodePath(path) : encodeURIComponent(path))
        : `${dataServer}/${encodePath(path)}`;
}

export function createUrls(config: ClientConfig): Urls {
    const dataServer = config.dataServer;
    const file = (path: string) => dataServer ? dataServerUrl(dataServer, path) : `api/files/raw/${encodePath(path)}`;
    return {
        file,
        download: path => `api/files/raw/${encodePath(path)}?download=true`,
        thumbnail: path => `api/thumbnails/${encodePath(path)}`,
        trickplaySheet: (path, index, version) => `api/trickplay/files/${encodePath(path)}?sheet=${index}&v=${version}`,
        player: (path, start = 0) => {
            const query = new URLSearchParams({ path });
            if (start >= 1)
                query.set('t', String(Math.floor(start)));
            return `player?${query}`;
        },
        external: url => {
            const template = config.externalPlayer?.url;
            if (!template)
                return null;
            return /\{(url|encodedUrl)\}/.test(template)
                ? template.replace(/\{(url|encodedUrl)\}/g, (_, name) => name === 'url' ? url : encodeURIComponent(url))
                : template + url;
        },
        externalName: config.externalPlayer?.name ?? null,
    };
}

/**
 * The client configuration (/api/config): loaded once at start, again after the settings are saved. Everything
 * derived from it (links, feature switches) follows the `config` signal.
 */
@Injectable({ providedIn: 'root' })
export class ConfigService {
    private readonly api = inject(Api);
    private readonly current = signal<ClientConfig | null>(null);

    /** Null until loaded. */
    readonly config = this.current.asReadonly();
    readonly urls = computed(() => {
        const config = this.current();
        return config ? createUrls(config) : null;
    });

    /** What the session may do in the library: the rights of /api/config. */
    readonly rights = computed(() => new Set(this.current()?.rights ?? []));
    /** The signed-in account; null for a visitor without one (or before loading). */
    readonly user = computed(() => this.current()?.user ?? null);
    readonly tenant = computed(() => this.current()?.tenant ?? null);

    /**
     * Loads (or reloads) the configuration; rejects when the server can't be reached, or (after opening the sign-in
     * page) when the session may not browse.
     */
    async load(): Promise<ClientConfig> {
        const config = await this.api.config();
        this.current.set(config);
        this.api.tenantId = config.tenant?.id ?? null;
        return config;
    }

    /** Forgets the configuration of the session that just ended or changed (signing in or out, another library). */
    clear(): void {
        this.current.set(null);
        this.api.tenantId = null;
    }

    /** The session has `right` in the library (e.g. `tags.assign`): what it doesn't have is hidden. */
    can(right: string): boolean {
        return this.rights().has(right);
    }

    /** The session has one of `rights`. */
    canAny(rights: readonly string[]): boolean {
        const granted = this.rights();
        return rights.some(right => granted.has(right));
    }

    /**
     * Opens a file in the external player app. The player fetches the file itself, without this browser's session: it
     * gets a link of its own, valid for a while (Player:LinkLifetimeMinutes); a data server needs none.
     */
    async openExternal(path: string): Promise<void> {
        const link = await this.externalLink(path);
        if (link)
            window.location.assign(link);
    }

    /** The link opening `path` in the external player app (see `openExternal`); null when there is none. */
    async externalLink(path: string): Promise<string | null> {
        const config = this.value;
        if (!config.externalPlayer)
            return null;
        const fileUrl = config.dataServer
            ? new URL(dataServerUrl(config.dataServer, path), document.baseURI).href
            : new URL((await this.api.fileLink(path)).url, document.baseURI).href;
        return this.links.external(fileUrl);
    }

    /** The loaded configuration, for code that only runs once it is there. */
    get value(): ClientConfig {
        const config = this.current();
        if (!config)
            throw new Error('The configuration is not loaded yet.');
        return config;
    }

    get links(): Urls {
        return this.urls()!;
    }

    /** The resolution of an image or video to show ("1920×1080"); empty when unknown or turned off (Browse:ShowResolution). */
    resolution(item: LibraryItem): string {
        return this.current()?.showResolution ? formatResolution(item.width, item.height) : '';
    }

    /** The duration of a video or audio file to show ("1:23:45"); empty when unknown or turned off (Browse:ShowDuration). */
    duration(item: LibraryItem): string {
        return this.current()?.showDuration && item.duration ? formatDuration(item.duration) : '';
    }

    /**
     * The HR/LR mark of an image or video: high resolution when its shorter side reaches the threshold of its kind
     * (Browse:ImageHighResolutionPixels / VideoHighResolutionPixels), so a portrait photo counts like a landscape one;
     * null when the marks are off (Browse:ResolutionMark), for other kinds, or when the index does not know the
     * resolution.
     */
    resolutionMark(item: LibraryItem): ResolutionMark | null {
        const thresholds = this.current()?.resolutionMark;
        const threshold = item.kind === 'image' ? thresholds?.image : item.kind === 'video' ? thresholds?.video : undefined;
        if (!threshold || !item.width || !item.height)
            return null;
        const high = Math.min(item.width, item.height) >= threshold;
        return {
            label: high ? 'HR' : 'LR',
            high,
            title: `${high ? 'High' : 'Low'} resolution: ${formatResolution(item.width, item.height)} `
                + `(${high ? 'at least' : 'under'} ${threshold} pixels on the shorter side)`,
        };
    }

    /**
     * Images over `Thumbnails:LargeImageKilobytes` are shown through their thumbnail (in the viewer: until the
     * original has loaded). Without thumbnails (disabled, no ffmpeg) the original is all there is.
     */
    isLargeImage(item: LibraryItem): boolean {
        const config = this.value;
        return item.kind === 'image' && config.features.thumbnails && (item.size ?? 0) > config.largeImageSize;
    }
}
