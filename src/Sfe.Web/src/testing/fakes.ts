import { ApplicationRef, ChangeDetectionStrategy, Component, EnvironmentProviders, Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Mock } from 'vitest';
import { Api } from '../app/core/api.service';
import { AdminPageComponent, leaveAdminPage } from '../app/admin/admin-page.component';
import { routes } from '../app/app.routes';
import { ConfigService } from '../app/core/config.service';
import { ClientConfig, FileTagChangeResult, FileTags, InstallationView, LibraryItem, SettingsValues, TagDetails, TagSummary } from '../app/core/models';
import { Rights } from '../app/core/rights';
import { TABS } from '../app/settings/settings-schema';

/*
 * Test doubles of the API and builders of its shapes. Components get `mockApi()`, whose methods are mocks that
 * never settle until a test stubs them; the Api class itself is tested against HttpTestingController.
 */

type AnyFunction = (...args: never[]) => unknown;
export type ApiMock = { [K in keyof Api]: Api[K] extends AnyFunction ? Mock<Api[K]> & Api[K] : Api[K] };

export function mockApi(): ApiMock {
    const mocks = new Map<string, Mock>();
    return new Proxy({} as ApiMock, {
        get(target, key) {
            // Properties set on it (the Api's `tenantId`) read back as set.
            if (Reflect.has(target, key))
                return Reflect.get(target, key);
            // Not API methods: what promises, injectors and test matchers look for on any object.
            if (typeof key !== 'string' || key === 'then' || key.startsWith('ng') || key === 'toJSON' || key.startsWith('$$') || key === 'asymmetricMatch' || key === 'nodeType')
                return undefined;
            let mock = mocks.get(key);
            if (!mock)
                mocks.set(key, mock = vi.fn(() => new Promise(() => { })));
            return mock;
        },
    });
}

/** The providers of a component test: the mocked API and a router (ListingStore navigates). */
export function testProviders(api: ApiMock, ...more: (Provider | EnvironmentProviders)[]): (Provider | EnvironmentProviders)[] {
    return [{ provide: Api, useValue: api }, provideRouter([]), ...more];
}

@Component({ selector: 'sfe-blank', template: '', changeDetection: ChangeDetectionStrategy.OnPush })
class BlankPageComponent {}

/**
 * The providers of a test of the administration page, through `RouterTestingHarness`: the mocked API, the page at
 * `admin` (with its guard and its earlier address) and an empty page at the root, standing for the listing page.
 */
export function adminPageProviders(api: ApiMock): (Provider | EnvironmentProviders)[] {
    return [{ provide: Api, useValue: api }, provideRouter([
        { path: 'admin', component: AdminPageComponent, canDeactivate: [leaveAdminPage] },
        routes.find(route => route.path === 'config')!,
        { path: '**', component: BlankPageComponent },
    ])];
}

/** The button of `root` whose text is `label`. */
export function buttonNamed(root: ParentNode, label: string): HTMLButtonElement | undefined {
    return [...root.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent!.trim() === label);
}

export const TAG_COLORS = [
    { slug: 'red', name: 'Red', hex: '#e22c3c' },
    { slug: 'yellow', name: 'Yellow', hex: '#ffd63d' },
    { slug: 'blue', name: 'Blue', hex: '#3b87f0' },
];

/** Every right: the default configuration is a server administrator's. */
export const ALL_RIGHTS: string[] = Object.values(Rights);

/** The library of the default configuration. */
export const LIBRARY = { id: 1, slug: 'library', name: 'Library' };

/** A configuration whose session has only `rights` (a visitor without an account when `user` is null). */
export function limitedConfig(rights: string[], overrides: Partial<ClientConfig> = {}): ClientConfig {
    return fakeConfig({ rights, user: { id: 2, userName: 'alice', displayName: null, kind: 'user', isServerAdmin: false, local: true }, ...overrides });
}

export function fakeConfig(overrides: Partial<ClientConfig> = {}): ClientConfig {
    return {
        rootName: 'Root',
        dataServer: null,
        pageSize: 100,
        searchPageSize: 50,
        features: { thumbnails: true, trickplay: false },
        tagColors: TAG_COLORS,
        maxFilesPerTagChange: 1000,
        largeImageSize: 1024 * 1024,
        seekSeconds: 10,
        loop: false,
        trickplayHover: true,
        folderPictures: 4,
        searchExplain: true,
        searchHistory: 10,
        singleViewOriginals: false,
        animatedHover: true,
        showResolution: true,
        showDuration: true,
        resolutionMark: null,
        rootPath: null,
        externalPlayer: null,
        theme: 'system',
        version: '1.0.0',
        commit: null,
        rights: ALL_RIGHTS,
        user: { id: 1, userName: 'admin', displayName: null, kind: 'user', isServerAdmin: true, local: true },
        tenant: LIBRARY,
        tenants: [LIBRARY],
        editor: { outputFolder: 'Edited', quality: 92 },
        streaming: null,
        ...overrides,
    };
}

/** Loads `config` into the ConfigService of the TestBed (through the mocked API). */
export async function loadConfig(api: ApiMock, config: ClientConfig = fakeConfig()): Promise<ConfigService> {
    api.config.mockResolvedValue(config);
    const service = TestBed.inject(ConfigService);
    await service.load();
    return service;
}

let nextTagId = 100;

export function tag(name: string, overrides: Partial<TagDetails> = {}): TagDetails {
    return {
        id: ++nextTagId,
        name,
        displayName: overrides.displayName ?? name,
        color: null,
        isCategory: false,
        isHidden: false,
        shorthand: null,
        isBuiltIn: false,
        disambiguationId: null,
        aliases: [],
        parents: [],
        fileCount: 0,
        ...overrides,
    };
}

export function summary(details: TagSummary): TagSummary {
    const { id, name, displayName, color, isCategory, isHidden } = details;
    return { id, name, displayName, color, isCategory, isHidden };
}

export function item(path: string, overrides: Partial<LibraryItem> = {}): LibraryItem {
    const name = path.split('/').pop()!;
    const dot = name.lastIndexOf('.');
    const extension = dot > 0 ? name.slice(dot) : null;
    const kind = overrides.kind ?? (/\.(jpe?g|png|webp)$/i.test(name) ? 'image' : /\.(mp4|mkv|webm)$/i.test(name) ? 'video' : extension ? 'file' : 'directory');
    return { name, path, kind, extension, size: kind === 'directory' ? null : 10, modified: '2026-01-01T10:00:00Z', tags: kind === 'directory' ? null : [], ...overrides };
}

export function fileTags(path: string, tags: TagSummary[] = []): FileTags {
    return { path, tags, groups: tags.length ? [{ category: null, tags }] : [] };
}

/**
 * What the server answers to a tag change, computed from the files' current tags: `current` maps each path to its
 * tags and is updated, so several changes can follow each other.
 */
export function applyChange(current: Map<string, TagSummary[]>, known: TagSummary[], change: { paths: string[]; add?: number[]; remove?: number[] }): FileTagChangeResult {
    let added = 0;
    let removed = 0;
    const files = change.paths.map(path => {
        let tags = current.get(path) ?? [];
        for (const id of change.add ?? []) {
            if (!tags.some(t => t.id === id)) {
                tags = [...tags, summary(known.find(t => t.id === id)!)];
                added++;
            }
        }
        for (const id of (change.remove ?? []).filter(id => !change.add?.includes(id))) {
            if (tags.some(t => t.id === id)) {
                tags = tags.filter(t => t.id !== id);
                removed++;
            }
        }
        current.set(path, tags);
        return fileTags(path, tags);
    });
    return { added, removed, files };
}

/** Lets pending promises and timers of `ms` run, then the change detection. */
export async function settle(ms = 0): Promise<void> {
    await new Promise(resolve => setTimeout(resolve, ms));
    await TestBed.inject(ApplicationRef).whenStable();
}

/** Sets what a media query matches (see setup.ts); its listeners are called. */
export function setMediaQuery(query: string, matches: boolean): void {
    (globalThis as unknown as { setMediaQuery(query: string, matches: boolean): void }).setMediaQuery(query, matches);
}

/** Types into an input as a user would: sets the value and sends the `input` event. */
export function type(input: HTMLInputElement | HTMLTextAreaElement, text: string): void {
    input.value = text;
    input.dispatchEvent(new Event('input', { bubbles: true }));
}

export interface Deferred<T> {
    promise: Promise<T>;
    resolve(value: T): void;
    reject(error: unknown): void;
}

/** A promise settled by the test, to control the order responses arrive in. */
export function deferred<T>(): Deferred<T> {
    let resolve!: (value: T) => void;
    let reject!: (error: unknown) => void;
    const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
    return { promise, resolve, reject };
}

/** Reports elements as scrolled into view to the IntersectionObservers watching them (see setup.ts). */
export function intersect(...elements: Element[]): void {
    (globalThis as unknown as { intersect(...elements: Element[]): void }).intersect(...elements);
}

/** The installation page's settings: the home folder, this computer only on 5080, ffmpeg found; nothing locked. */
export function installationView(overrides: Partial<InstallationView> = {}): InstallationView {
    const free = (value: string | null) => ({ value, locked: false, lockedBy: null });
    return {
        root: free('C:\\Users\\me\\'), rootExists: true,
        urls: free('http://localhost:5080'), port: 5080, network: false,
        ffmpegPath: free('ffmpeg'), ffmpegFound: 'C:\\ffmpeg\\bin\\ffmpeg.exe', ffmpegVersion: '7.1',
        restartRequired: false,
        mode: free('Local'), modeInEffect: 'local', onThisComputer: true,
        ...overrides,
    };
}

/** A valid value for every field of the settings pages (the lowest numbers, empty lists...), as the server sends them all. */
export function settingsValues(): SettingsValues {
    const result: Record<string, unknown> = {};
    for (const field of TABS.flatMap(tab => tab.groups.flatMap(group => group.fields))) {
        if (field.type === 'preset')
            continue;
        const parts = field.key.split('.');
        let target = result;
        for (const part of parts.slice(0, -1))
            target = (target[part] ??= {}) as Record<string, unknown>;
        target[parts.at(-1)!] = field.type === 'number' ? field.min : field.type === 'checkbox' ? false
            : field.type === 'list' || field.type === 'extensions' ? [] : field.type === 'select' ? field.options[0][0] : null;
    }
    return result;
}

/**
 * Canvases whose 2D context records what is drawn ([canvas, ...drawImage arguments]) and whose `toBlob` gives a blob of
 * the type asked for: jsdom draws nothing. Undone by `vi.restoreAllMocks()`.
 */
export function fakeCanvases(): { drawn: unknown[][] } {
    const drawn: unknown[][] = [];
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(function (this: HTMLCanvasElement) {
        return { drawImage: (...args: unknown[]) => drawn.push([this, ...args]) } as unknown as CanvasRenderingContext2D;
    } as never);
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(function (this: HTMLCanvasElement, callback: BlobCallback, type?: string) {
        callback(new Blob([`${this.width}x${this.height}`], { type }));
    });
    return { drawn };
}
