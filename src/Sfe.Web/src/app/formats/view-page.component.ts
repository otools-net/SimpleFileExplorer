import { NgComponentOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, Injector, OnInit, signal, Type } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute } from '@angular/router';
import { Api, ApiError, redirected } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { formatDate, formatSize, joinParts } from '../core/format';
import { LibraryItem } from '../core/models';
import { baseName, parentPath } from '../core/paths';
import { Rights } from '../core/rights';
import { CopyPathService } from '../core/copy-path.service';
import { MenuEntry, usePageMenu } from '../menus/context-menu';
import { IconComponent } from '../shared/icon.component';
import { FILE_VIEW_HOST, FileFormat, FileViewHost, formatOf } from './formats';

/**
 * `view?path=…`: one file in its format's viewer, filling a page of its own (a text file opened in a new tab, from a
 * listing or from the viewer). `asText=1` shows any file as text, `edit=1` opens it ready to be changed (a copy just
 * saved). Formats without a page (media) go to the file itself. Unsaved changes are asked about before leaving.
 */
@Component({
    selector: 'sfe-view-page',
    imports: [NgComponentOutlet, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'view-page' },
    template: `
        <header class="view-page-bar">
            <a class="icon-button" [href]="folderUrl()" [title]="'Open the folder ' + folderName()" aria-label="Open its folder"><svg sfeIcon="folder-up"></svg></a>
            <div class="view-page-title">
                <strong>{{ name }}</strong>
                <span>{{ meta() }}</span>
            </div>
            @if (item() && config.can('files.download')) {
                <a class="icon-button" [href]="config.urls()!.download(path)" title="Download" aria-label="Download"><svg sfeIcon="download"></svg></a>
            }
        </header>
        <main class="view-page-stage" aria-live="polite">
            @switch (state()) {
                @case ('ready') {
                    @if (component(); as shown) {
                        <ng-container *ngComponentOutlet="shown; inputs: { item: item(), format: format(), edit: edit }; injector: viewInjector" />
                    }
                }
                @case ('missing') {
                    <div class="viewer-message"><p>{{ path ? 'This file is not there, or you may not see it.' : 'No file given.' }}</p></div>
                }
                @case ('unviewable') {
                    <div class="viewer-message"><p>This file has no viewer: download it, or open it from its folder.</p></div>
                }
                @case ('unreachable') {
                    <div class="viewer-message"><p>The server is not reachable.</p><p>{{ error() }}</p></div>
                }
            }
        </main>
    `,
})
export class ViewPageComponent implements OnInit {
    protected readonly config = inject(ConfigService);
    private readonly api = inject(Api);
    private readonly title = inject(Title);
    private readonly params = inject(ActivatedRoute).snapshot.queryParamMap;

    protected readonly path = this.params.get('path') || '';
    private readonly asText = this.params.get('asText') === '1';
    protected readonly edit = this.params.get('edit') === '1';
    protected readonly name = baseName(this.path);
    protected readonly state = signal<'loading' | 'ready' | 'missing' | 'unviewable' | 'unreachable'>('loading');
    protected readonly error = signal('');
    protected readonly item = signal<LibraryItem | null>(null);
    protected readonly format = signal<FileFormat | null>(null);
    protected readonly component = signal<Type<unknown> | null>(null);
    protected readonly folderUrl = computed(() => `./?${new URLSearchParams({ path: parentPath(this.path) })}`);
    protected readonly folderName = computed(() => baseName(parentPath(this.path)) || this.config.config()?.rootName || 'the root');
    protected readonly meta = computed(() => {
        const item = this.item();
        return item ? joinParts([formatSize(item.size), formatDate(item.modified), parentPath(this.path)], '  ·  ') : '';
    });

    private readonly guards = new Set<() => boolean>();
    private readonly host: FileViewHost = {
        place: 'page',
        can: right => this.config.can(right),
        guard: canLeave => {
            this.guards.add(canLeave);
            return () => this.guards.delete(canLeave);
        },
    };
    protected readonly viewInjector = Injector.create({ providers: [{ provide: FILE_VIEW_HOST, useValue: this.host }], parent: inject(Injector) });

    constructor() {
        // A right click on the page (not on its text, which keeps the browser's menu when selected or edited).
        const copyPaths = inject(CopyPathService);
        usePageMenu({
            kind: 'page', key: 'view-page', name: this.name, entries: () => {
                if (!this.name)
                    return [];
                const entries: MenuEntry[] = [
                    { id: 'openFolder', label: 'Open its folder', icon: 'folder-up', title: `List the folder ${this.folderName()}`, href: this.folderUrl() },
                    { id: 'folderNewTab', label: 'Open its folder in new tab', icon: 'external', title: `List the folder ${this.folderName()} in a tab of its own`, href: this.folderUrl(), newTab: true },
                ];
                if (this.item() && this.config.can('files.download'))
                    entries.push({ id: 'download', label: 'Download', icon: 'download', title: 'Save this file to this device', href: this.config.urls()!.download(this.path) });
                if (copyPaths.enabled())
                    entries.push({ id: 'copyPath', label: 'Copy full path', icon: 'copy', title: 'Copy where it is on the server', run: () => void copyPaths.copy(this.path) });
                return entries;
            },
        });
    }

    /** The route's question: the file shown may be left (nothing unsaved, or the person agrees). */
    canLeave(): boolean {
        for (const canLeave of this.guards) {
            if (!canLeave())
                return false;
        }
        return true;
    }

    async ngOnInit(): Promise<void> {
        if (!this.name) {
            this.state.set('missing');
            return;
        }
        this.title.setTitle(`${this.name} – SFE`);
        let item: LibraryItem;
        try {
            await this.config.load();
            item = (await this.api.properties(this.path)).item;
        } catch (error) {
            // Not signed in, or the server is not set up: the sign-in or setup page opens instead.
            if (redirected(error))
                return;
            if (error instanceof ApiError && (error.status === 404 || error.status === 403)) {
                this.state.set('missing');
                return;
            }
            this.error.set((error as Error).message);
            this.state.set('unreachable');
            return;
        }
        const format = this.config.can(Rights.viewFiles) ? formatOf(item, { asText: this.asText }) : null;
        if (!format) {
            this.state.set('unviewable');
            return;
        }
        if ('builtin' in format.view) {
            // Media are best in the browser itself (or the player page).
            location.replace(this.config.urls()!.file(this.path));
            return;
        }
        this.item.set(item);
        this.format.set(format);
        this.component.set(await format.view.load());
        this.state.set('ready');
    }
}
