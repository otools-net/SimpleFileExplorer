import { ChangeDetectionStrategy, Component, computed, effect, inject, OnInit, signal, untracked, viewChild } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute } from '@angular/router';
import { Api, redirected } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { ToastService } from '../core/toast.service';
import { copyText, CopyPathService } from '../core/copy-path.service';
import { MenuEntry, usePageMenu } from '../menus/context-menu';
import { baseName } from '../core/paths';
import { IconComponent } from '../shared/icon.component';
import { waitForTrickplay } from '../trickplay/trickplay';
import { VideoPlayerComponent } from './video-player.component';

/**
 * `player?path=…&t=…`: one video in the player of the viewer, filling its own window (opened from the viewer, W).
 * `t` is the position to start from (where the viewer left off). Always dark.
 */
@Component({
    selector: 'sfe-player-page',
    imports: [IconComponent, VideoPlayerComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'player-page', '(document:keydown)': 'onKeydown($event)' },
    template: `
        <main class="player-page-stage" aria-live="polite">
            @switch (state()) {
                @case ('playing') {
                    <sfe-video-player [src]="urls()!.file(path)" [name]="name" [poster]="poster()" [seekStep]="config.config()!.seekSeconds"
                                      [loop]="config.config()!.loop"
                                      [startAt]="startAt" [stream]="stream()" (unsupported)="state.set('unsupported'); converted.set($event)" />
                }
                @case ('unsupported') {
                    <div class="viewer-message">
                        <p>This browser cannot play this video{{ converted() ? ', and the server could not convert it (it may be converting as many videos as it can).' : '.' }}</p>
                        <p class="player-page-links">
                            @if (urls()!.externalName; as name) {
                                <button type="button" class="button" [title]="'Open the video in ' + name + ' (a link working without signing in, for a while)'"
                                        (click)="openExternal()"><svg sfeIcon="play"></svg>Open in {{ name }}</button>
                            }
                            @if (config.can('files.download')) {
                                <a class="button" [href]="urls()!.download(path)" title="Save the video to this device"><svg sfeIcon="download"></svg>Download</a>
                            }
                        </p>
                    </div>
                }
                @case ('no-video') {
                    <div class="viewer-message"><p>No video given.</p></div>
                }
                @case ('unreachable') {
                    <div class="viewer-message"><p>The server is not reachable.</p><p>{{ error() }}</p></div>
                }
            }
        </main>
    `,
})
export class PlayerPageComponent implements OnInit {
    protected readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    private readonly api = inject(Api);
    private readonly title = inject(Title);
    private readonly params = inject(ActivatedRoute).snapshot.queryParamMap;
    private readonly player = viewChild(VideoPlayerComponent);

    protected readonly path = this.params.get('path') || '';
    protected readonly name = baseName(this.path);
    protected readonly startAt = Math.max(0, Number(this.params.get('t')) || 0);
    protected readonly state = signal<'loading' | 'playing' | 'unsupported' | 'no-video' | 'unreachable'>('loading');
    protected readonly error = signal('');
    protected readonly urls = this.config.urls;
    /** The server's conversion, once the configuration says it converts videos. */
    protected readonly stream = computed(() => this.config.config() ? this.config.videoStream(this.path) : null);
    /** The server's conversion failed too. */
    protected readonly converted = signal(false);
    protected readonly poster = computed(() => this.config.config()?.features.thumbnails ? this.urls()!.thumbnail(this.path) : null);

    constructor() {
        // A right click anywhere: the player's controls, then the video's links.
        const copyPaths = inject(CopyPathService);
        usePageMenu({
            kind: 'page', key: 'player-page', name: this.name, entries: () => {
                const entries: MenuEntry[] = [...this.player()?.menuEntries() ?? []];
                if (this.player())
                    entries.push({ id: 'copyTimeLink', label: 'Copy link at this time', icon: 'clock', title: 'Copy a link playing it from where it is now', run: () => void this.copyTimeLink() });
                if (this.urls()?.externalName)
                    entries.push({ id: 'external', label: `Open in ${this.urls()!.externalName}`, icon: 'play', title: 'A link working without signing in, for a while', run: () => this.openExternal() });
                if (this.state() !== 'loading' && this.config.can('files.download'))
                    entries.push({ id: 'download', label: 'Download', icon: 'download', title: 'Save the video to this device', href: this.urls()!.download(this.path) });
                if (copyPaths.enabled())
                    entries.push({ id: 'copyPath', label: 'Copy full path', icon: 'copy', title: 'Copy where it is on the server', run: () => void copyPaths.copy(this.path) });
                return entries;
            },
        });
        // Scrub previews, waiting for them while the background job has the video queued.
        effect(() => {
            const player = this.player();
            if (!player)
                return;
            untracked(() => {
                player.focus();
                if (!this.config.value.features.trickplay)
                    return;
                void waitForTrickplay(this.api, this.path, () => this.player() === player).then(trickplay => {
                    if (trickplay && this.player() === player)
                        player.setTrickplay(trickplay, index => this.urls()!.trickplaySheet(this.path, index, trickplay.version));
                });
            });
        });
    }

    async ngOnInit(): Promise<void> {
        if (!this.name) {
            this.state.set('no-video');
            return;
        }
        this.title.setTitle(`${this.name} – SFE`);
        try {
            await this.config.load();
        } catch (error) {
            // Not signed in, or the server is not set up: the sign-in or setup page opens instead.
            if (redirected(error))
                return;
            this.error.set((error as Error).message);
            this.state.set('unreachable');
            return;
        }
        this.state.set('playing');
    }

    /** This page's address, playing from where the video is now. */
    private async copyTimeLink(): Promise<void> {
        try {
            await copyText(new URL(this.urls()!.player(this.path, Math.floor(this.player()?.currentTime() ?? 0)), document.baseURI).href);
            this.toasts.show('Link copied.');
        } catch {
            this.toasts.show('The link could not be copied.', { error: true });
        }
    }

    protected openExternal(): void {
        this.config.openExternal(this.path).catch((error: Error) => this.toasts.error(`Could not open the video: ${error.message}`));
    }

    /** Same shortcuts as in the viewer (Space/K, J/L, F, M, S); focused buttons and the seek bar keep their own keys. */
    protected onKeydown(event: KeyboardEvent): void {
        const target = event.target as Element;
        if ((event.key === ' ' || event.key === 'Enter') && target.closest?.('button, a'))
            return;
        if (target.matches?.('input[type="range"]') && event.key.startsWith('Arrow'))
            return;
        if (this.player()?.handleKey(event))
            event.preventDefault();
    }
}
