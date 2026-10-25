import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { formatSize, joinParts } from '../../core/format';
import { LibraryItem } from '../../core/models';
import { readPreference, writePreference } from '../../core/preferences';
import { Rights } from '../../core/rights';
import { MenuEntry } from '../../menus/context-menu';
import { OverflowItemDirective, OverflowMoreComponent, OverflowRowDirective } from '../../menus/overflow-row';
import { IconComponent } from '../../shared/icon.component';
import { FILE_VIEW_HOST, FileFormat } from '../formats';
import { FLASH_LOADER, FlashHandle, FlashMetadata } from './flash-player';

/** The movies' volume in this browser (0–1), and whether they are muted. */
export const FLASH_VOLUME_KEY = 'sfe.flash.volume';
export const FLASH_MUTED_KEY = 'sfe.flash.muted';

function readVolume(): number {
    const value = Number(readPreference(FLASH_VOLUME_KEY) ?? '1');
    return Number.isFinite(value) ? Math.min(1, Math.max(0, value)) : 1;
}

/**
 * A Flash movie or game (agents/docs/features/flash-viewer.md), played by Ruffle (bundled) in a frame of no origin:
 * play / pause, again from the start, volume, full screen; what the movie is (ActionScript, size, frame rate). The
 * movie gets its folder's files through a flash link (`api/files/flash-links`), read only; it never acts as the user.
 */
@Component({
    selector: 'sfe-flash-view',
    imports: [IconComponent, OverflowRowDirective, OverflowItemDirective, OverflowMoreComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'text-view flash-view',
        'data-owns-keys': '',
    },
    template: `
        <div class="text-toolbar" role="toolbar" aria-label="Flash movie" sfeOverflowRow="Flash movie">
            <button type="button" class="icon-button" data-play [sfeOverflowItem]="null" overflowStay [disabled]="state() !== 'ready'"
                    [title]="playing() ? 'Pause' : 'Play'" [attr.aria-label]="playing() ? 'Pause' : 'Play'" (click)="togglePlay()"><svg [sfeIcon]="playing() ? 'pause' : 'play'"></svg></button>
            <button type="button" class="icon-button" data-restart [sfeOverflowItem]="more.restart" [disabled]="state() !== 'ready'"
                    title="Play again from the start" aria-label="Play again from the start" (click)="restart()"><svg sfeIcon="refresh"></svg></button>
            <button type="button" class="icon-button" data-mute [sfeOverflowItem]="null" overflowStay [class.active]="muted()" [attr.aria-pressed]="muted()"
                    [title]="muted() ? 'Sound on' : 'Mute'" [attr.aria-label]="muted() ? 'Sound on' : 'Mute'" (click)="toggleMute()"><svg [sfeIcon]="muted() || volume() === 0 ? 'muted' : 'volume'"></svg></button>
            <input type="range" class="flash-volume" data-volume [sfeOverflowItem]="null" min="0" max="1" step="0.05" aria-label="Volume" title="Volume"
                   [value]="volume()" (input)="setVolume($any($event.target).valueAsNumber)" />
            <button type="button" class="icon-button" data-fullscreen [sfeOverflowItem]="null" overflowStay [disabled]="state() !== 'ready'"
                    title="Full screen" aria-label="Full screen" (click)="fullscreen()"><svg sfeIcon="fullscreen"></svg></button>
            <button class="icon-button" sfeOverflowMore></button>
        </div>
        <!-- Always there: the grid's second row (the text views' editing bar), so that the movie takes the third. -->
        <div class="pdf-messages">
            @switch (state()) {
                @case ('loading') {
                    <p class="text-message">Loading…</p>
                }
                @case ('failed') {
                    <div class="text-message">
                        <p>{{ message() }}</p>
                        @if (downloadUrl(); as url) {
                            <p><a class="button" [href]="url"><svg sfeIcon="download"></svg> Download</a></p>
                        }
                    </div>
                }
            }
        </div>
        <div #stage class="flash-stage" [hidden]="state() === 'failed'" [attr.aria-label]="item().name"></div>
        @if (state() === 'ready') {
            <footer class="text-status" aria-live="polite">
                <span>{{ details() }}</span>
                <span data-playing>{{ playing() ? 'Playing' : 'Paused' }}</span>
            </footer>
        }
    `,
})
export class FlashViewComponent {
    readonly item = input.required<LibraryItem>();
    readonly format = input<FileFormat | null>(null);
    /** Opened ready to be changed: movies are only played. */
    readonly edit = input(false);

    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly fileHost = inject(FILE_VIEW_HOST);
    private readonly load = inject(FLASH_LOADER);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
    private readonly stage = viewChild.required<ElementRef<HTMLDivElement>>('stage');

    protected readonly state = signal<'loading' | 'ready' | 'failed'>('loading');
    protected readonly message = signal('');
    protected readonly playing = signal(false);
    protected readonly metadata = signal<FlashMetadata | null>(null);
    protected readonly volume = signal(readVolume());
    protected readonly muted = signal(readPreference(FLASH_MUTED_KEY) === '1');
    private handle: FlashHandle | null = null;

    protected readonly downloadUrl = computed(() => this.fileHost.can(Rights.download) ? this.config.urls()?.download(this.item().path) ?? null : null);
    protected readonly details = computed(() => {
        const metadata = this.metadata();
        const size = this.item().size;
        return joinParts([
            'Flash',
            metadata ? (metadata.isActionScript3 ? 'ActionScript 3' : 'ActionScript 1/2') : null,
            metadata ? `SWF ${metadata.swfVersion}` : null,
            metadata?.width && metadata.height ? `${Math.round(metadata.width)} × ${Math.round(metadata.height)}` : null,
            metadata?.frameRate ? `${Math.round(metadata.frameRate * 10) / 10} fps` : null,
            size ? formatSize(size) : null,
        ], '  ·  ');
    });

    protected readonly more = {
        restart: (): MenuEntry => ({ id: 'restart', label: 'Play again', icon: 'refresh', title: 'Play again from the start', disabled: this.state() !== 'ready', run: () => this.restart() }),
    };

    constructor() {
        let destroyed = false;
        inject(DestroyRef).onDestroy(() => {
            destroyed = true;
            this.handle?.destroy();
        });
        effect(() => {
            const item = this.item();
            untracked(() => void this.open(item, () => destroyed));
        });
    }

    private async open(item: LibraryItem, destroyed: () => boolean): Promise<void> {
        if (this.handle)
            return;
        try {
            const link = await this.api.flashLink(item.path);
            if (destroyed())
                return;
            const handle = await this.load(this.stage().nativeElement, new URL(link.url, document.baseURI).href, this.effectiveVolume(), {
                metadata: metadata => this.metadata.set(metadata),
                state: playing => this.playing.set(playing),
                // As a key pressed inside the view: the viewer takes the focus back, the next Esc closes it.
                escape: () => this.host.nativeElement.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })),
            });
            if (destroyed()) {
                handle.destroy();
                return;
            }
            this.handle = handle;
            this.state.set('ready');
            handle.focus();
        } catch (error) {
            if (destroyed())
                return;
            this.message.set(`This movie could not be played: ${(error as Error)?.message ?? error}. It can only be downloaded.`);
            this.state.set('failed');
        }
    }

    private effectiveVolume(): number {
        return this.muted() ? 0 : this.volume();
    }

    protected togglePlay(): void {
        if (this.playing())
            this.handle?.pause();
        else
            this.handle?.play();
        this.handle?.focus();
    }

    protected restart(): void {
        this.metadata.set(null);
        this.handle?.restart();
        this.handle?.focus();
    }

    protected toggleMute(): void {
        this.muted.update(muted => !muted);
        writePreference(FLASH_MUTED_KEY, this.muted() ? '1' : '0');
        this.handle?.setVolume(this.effectiveVolume());
    }

    protected setVolume(value: number): void {
        if (!Number.isFinite(value))
            return;
        this.volume.set(Math.min(1, Math.max(0, value)));
        writePreference(FLASH_VOLUME_KEY, String(this.volume()));
        if (this.muted()) {
            this.muted.set(false);
            writePreference(FLASH_MUTED_KEY, '0');
        }
        this.handle?.setVolume(this.effectiveVolume());
    }

    /** The movie's frame fills the screen (the frame itself has no say: it may not ask for it). */
    protected fullscreen(): void {
        void this.stage().nativeElement.requestFullscreen?.().then(() => this.handle?.focus(), () => undefined);
    }
}
