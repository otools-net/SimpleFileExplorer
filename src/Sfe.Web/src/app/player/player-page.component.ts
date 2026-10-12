import { ChangeDetectionStrategy, Component, computed, effect, inject, OnInit, signal, untracked, viewChild } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute } from '@angular/router';
import { Api, redirected } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { ToastService } from '../core/toast.service';
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
                                      [startAt]="startAt" (unsupported)="state.set('unsupported')" />
                }
                @case ('unsupported') {
                    <div class="viewer-message">
                        <p>This browser cannot play this video.</p>
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
    protected readonly poster = computed(() => this.config.config()?.features.thumbnails ? this.urls()!.thumbnail(this.path) : null);

    constructor() {
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
