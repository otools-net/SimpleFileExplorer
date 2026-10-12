import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, ElementRef, inject, input, linkedSignal, output, signal, viewChild } from '@angular/core';
import { formatDuration as formatTime } from '../core/format';
import { TrickplayManifest } from '../core/models';
import { IconComponent } from '../shared/icon.component';

const DOUBLE_TAP_MS = 300;      // max delay between the two taps of a double tap
const STREAK_MS = 700;          // further taps within this delay add another step
const HIDE_CONTROLS_MS = 2500;
const HINT_MS = 650;
const HOLD_FAST_MS = 1500;      // holding a finger on the right third this long plays at FAST_RATE until it lifts
const FAST_RATE = 2;
const HOLD_SLOP_PX = 20;        // a finger moving further than this is not holding
const SPEEDS = [0.5, 0.75, 1, 1.25, 1.5, 2];

/**
 * Video player with custom controls: seek bar, back/forward buttons (`seekStep` seconds, Sfe:Player:SeekSeconds),
 * speed (its button opens the list of rates), loop (`loop` = its first state, Sfe:Player:Loop), mute, fullscreen and
 * screenshot (saves the current frame as a PNG named after `name`).
 * Touch: double-tap the left/right third to jump back/forward (keep tapping to jump further),
 * double-tap the middle to play/pause, single tap to show/hide the controls, hold the right third (1.5 s) to play at 2×
 * until the finger lifts (the speed button keeps the chosen rate, which comes back).
 * Mouse: click plays/pauses, double-click toggles fullscreen.
 * Hovering or dragging the seek bar shows the time there, and the frame once `setTrickplay` provided the trickplay.
 * `startAt`: position (seconds) to start from, e.g. where another player left off.
 */
@Component({
    selector: 'sfe-video-player',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'player',
        tabindex: '-1',
        '[class.paused]': 'paused()',
        '[class.controls-visible]': 'controlsVisible()',
        '[class.waiting]': 'waiting()',
        '[class.fullscreen]': 'fullscreen()',
        '[class.fast]': 'fast()',
        '(pointermove)': '$event.pointerType === "mouse" && showControls()',
        '(document:fullscreenchange)': 'onFullscreenChange()',
        '(document:pointerdown)': 'closeSpeedMenu($event)',
    },
    template: `
        <video #video [src]="src()" [poster]="poster() ?? ''" [loop]="looping()" preload="metadata" playsinline
               (play)="updatePlayState()" (pause)="updatePlayState(); waiting.set(false)" (ended)="updatePlayState()"
               (timeupdate)="updateTime()" (durationchange)="updateTime()" (progress)="updateTime()" (volumechange)="updateVolume()"
               (waiting)="waiting.set(true)" (playing)="waiting.set(false)" (canplay)="waiting.set(false)" (seeked)="waiting.set(false)"
               (error)="failed()" (loadedmetadata)="startFrom()"></video>
        <div class="player-surface" (pointerdown)="onPointerDown($event)" (pointermove)="onPointerMove($event)" (pointerup)="onTap($event)"
             (pointercancel)="endHold()" (contextmenu)="onContextMenu($event)" (click)="onClick()" (dblclick)="onDoubleClick()"></div>
        <div #flash class="player-flash" aria-hidden="true"></div>
        <div #hintBack class="player-hint back" aria-hidden="true"></div>
        <div #hintForward class="player-hint forward" aria-hidden="true"></div>
        <div class="player-fast" aria-hidden="true">{{ fastRate }}× <svg sfeIcon="fast"></svg></div>
        <div class="player-spinner" aria-hidden="true"></div>
        <button type="button" class="player-big-play" aria-label="Play" title="Play (K)" (click)="togglePlay()"><svg sfeIcon="play"></svg></button>
        <div class="player-controls" (pointerdown)="showControls()">
            <div class="player-seek-wrap">
                <input #seek type="range" class="player-seek" min="0" max="0" step="any" value="0" aria-label="Seek"
                       (input)="onSeekInput()" (pointermove)="onSeekHover($event)" (pointerleave)="onSeekLeave()"
                       (change)="endSeekPreview($event)" (pointerup)="endSeekPreview($event)" (pointercancel)="endSeekPreview($event)" (blur)="endSeekPreview($event)">
                <div #preview class="player-preview" aria-hidden="true">
                    <div #frame class="player-preview-frame"></div>
                    <span #previewTime class="player-preview-time"></span>
                </div>
            </div>
            <div class="player-row">
                <button type="button" class="player-button player-seek-button" [title]="'Back ' + seekStep() + ' s (J)'" [attr.aria-label]="'Back ' + seekStep() + ' s (J)'" (click)="stepSeek(-1); showControls()">
                    <svg sfeIcon="rewind"></svg><span class="player-step" [class.long]="seekStep() >= 100" aria-hidden="true">{{ seekStep() }}</span>
                </button>
                <button type="button" class="player-button" [title]="playLabel()" [attr.aria-label]="playLabel()" (click)="togglePlay()">
                    <svg [sfeIcon]="paused() ? 'play' : 'pause'"></svg>
                </button>
                <button type="button" class="player-button player-seek-button" [title]="'Forward ' + seekStep() + ' s (L)'" [attr.aria-label]="'Forward ' + seekStep() + ' s (L)'" (click)="stepSeek(1); showControls()">
                    <svg sfeIcon="forward"></svg><span class="player-step" [class.long]="seekStep() >= 100" aria-hidden="true">{{ seekStep() }}</span>
                </button>
                <span class="player-time">{{ timeText() }}</span>
                <span class="player-spacer"></span>
                <div class="player-speed-wrap">
                    <button type="button" class="player-button player-speed" title="Playback speed" aria-haspopup="true" [attr.aria-expanded]="speedMenu()" (click)="toggleSpeedMenu()">{{ speed() }}×</button>
                    @if (speedMenu()) {
                        <div class="player-speed-menu" role="menu" aria-label="Playback speed" [class.wide]="speedMenuHeight < 200" [style.max-height.px]="speedMenuHeight">
                            @for (rate of speeds; track rate) {
                                <button type="button" role="menuitemradio" [class.on]="rate === speed()" [attr.aria-checked]="rate === speed()" [title]="'Play at ' + rate + '×'" (click)="setSpeed(rate)">{{ rate }}×</button>
                            }
                        </div>
                    }
                </div>
                <button type="button" class="player-button player-toggle" [class.on]="looping()" [title]="loopLabel()" [attr.aria-label]="loopLabel()" [attr.aria-pressed]="looping()" (click)="toggleLoop(); showControls()">
                    <svg sfeIcon="loop"></svg>
                </button>
                <button type="button" class="player-button" title="Save this frame as a picture (S)" aria-label="Save this frame as a picture (S)" (click)="saveFrame(); showControls()">
                    <svg sfeIcon="camera"></svg>
                </button>
                <button type="button" class="player-button" [title]="muteLabel()" [attr.aria-label]="muteLabel()" (click)="toggleMute()">
                    <svg [sfeIcon]="muted() ? 'muted' : 'volume'"></svg>
                </button>
                <button type="button" class="player-button" [title]="fullscreen() ? 'Exit fullscreen (F)' : 'Fullscreen (F)'" [attr.aria-label]="fullscreen() ? 'Exit fullscreen (F)' : 'Fullscreen (F)'" (click)="toggleFullscreen()">
                    <svg [sfeIcon]="fullscreen() ? 'exit-fullscreen' : 'fullscreen'"></svg>
                </button>
            </div>
        </div>
    `,
})
export class VideoPlayerComponent {
    readonly src = input.required<string>();
    readonly name = input('video');
    readonly poster = input<string | null>(null);
    readonly seekStep = input(10);
    readonly startAt = input(0);
    /** Whether the video starts over at its end, until the loop button is pressed. */
    readonly loop = input(false);
    /** The browser can't play the video. */
    readonly unsupported = output<void>();

    protected readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    private readonly videoRef = viewChild.required<ElementRef<HTMLVideoElement>>('video');
    private readonly seekRef = viewChild.required<ElementRef<HTMLInputElement>>('seek');
    private readonly previewRef = viewChild.required<ElementRef<HTMLElement>>('preview');
    private readonly frameRef = viewChild.required<ElementRef<HTMLElement>>('frame');
    private readonly previewTimeRef = viewChild.required<ElementRef<HTMLElement>>('previewTime');
    private readonly flashRef = viewChild.required<ElementRef<HTMLElement>>('flash');
    private readonly hintBackRef = viewChild.required<ElementRef<HTMLElement>>('hintBack');
    private readonly hintForwardRef = viewChild.required<ElementRef<HTMLElement>>('hintForward');

    protected readonly paused = signal(true);
    protected readonly controlsVisible = signal(true);
    protected readonly waiting = signal(false);
    protected readonly fullscreen = signal(false);
    protected readonly muted = signal(false);
    protected readonly speed = signal(1);
    protected readonly speedMenu = signal(false);
    /** While a held finger plays the video at FAST_RATE. */
    protected readonly fast = signal(false);
    protected readonly fastRate = FAST_RATE;
    protected readonly speeds = SPEEDS;
    /**
     * Room above the button: the player clips the list. Short players (a wide video on a phone) get the rates in two
     * rows, and a scroll bar when even that doesn't fit.
     */
    protected speedMenuHeight = 0;
    protected readonly looping = linkedSignal(() => this.loop());
    protected readonly timeText = signal('0:00 / 0:00');
    protected readonly playLabel = computed(() => this.paused() ? 'Play (K)' : 'Pause (K)');
    protected readonly muteLabel = computed(() => this.muted() ? 'Unmute (M)' : 'Mute (M)');
    protected readonly loopLabel = computed(() => this.looping() ? 'Stop looping (R)' : 'Loop (R)');

    protected pointerType = 'mouse';
    private hideTimer = 0;
    private singleTapTimer = 0;
    private holdTimer = 0;
    private holdStart = { x: 0, y: 0 };
    private lastTap = { time: 0, x: 0 };
    /** While a run of seek taps is going on. */
    private streak: { direction: number; total: number; until: number } | null = null;
    private readonly hintTimers = new Map<HTMLElement, number>();
    /** Once the video's trickplay is known. */
    private trickplay: { info: TrickplayManifest; sheetUrl: (index: number) => string; sheets: HTMLImageElement[] | null } | null = null;
    private errored = false;
    private started = false;

    private get video(): HTMLVideoElement {
        return this.videoRef().nativeElement;
    }

    constructor() {
        afterNextRender(() => {
            this.updateVolume();
            // Start playing right away; browsers that refuse autoplay leave it paused with the big play button.
            this.video.play().catch(() => this.updatePlayState());
        });
        inject(DestroyRef).onDestroy(() => this.destroy());
    }

    /** Player shortcuts; returns true when the key was handled. */
    handleKey(event: KeyboardEvent): boolean {
        if (event.ctrlKey || event.metaKey || event.altKey)
            return false;
        switch (event.key.toLowerCase()) {
            case 'escape':
                if (!this.speedMenu())
                    return false;
                this.speedMenu.set(false);
                break;
            case ' ':
            case 'k': this.togglePlay(); break;
            case 'j': this.stepSeek(-1); break;
            case 'l': this.stepSeek(1); break;
            case 'f': void this.toggleFullscreen(); break;
            case 'm': this.toggleMute(); break;
            case 's': this.saveFrame(); break;
            case 'r': this.toggleLoop(); break;
            default: return false;
        }
        this.showControls();
        return true;
    }

    setTrickplay(info: TrickplayManifest | null, sheetUrl: (index: number) => string): void {
        this.trickplay = info && info.count > 0 ? { info, sheetUrl, sheets: null } : null;
    }

    currentTime(): number {
        return this.video.currentTime;
    }

    focus(): void {
        this.host.focus();
    }

    /* ---------- Playback ---------- */

    protected togglePlay(): void {
        if (this.video.paused || this.video.ended)
            this.video.play().catch(() => { });
        else
            this.video.pause();
    }

    protected toggleLoop(): void {
        this.looping.update(on => !on);
    }

    protected toggleMute(): void {
        this.video.muted = !this.video.muted;
    }

    protected toggleSpeedMenu(): void {
        this.speedMenuHeight = Math.max(80, this.host.clientHeight - 64);
        this.speedMenu.update(open => !open);
        this.showControls();
    }

    protected setSpeed(rate: number): void {
        this.video.playbackRate = rate;
        this.speed.set(rate);
        this.speedMenu.set(false);
        this.showControls();
    }

    /** A press anywhere but on the list or its button closes it. */
    protected closeSpeedMenu(event: Event): void {
        if (this.speedMenu() && !(event.target as Element).closest?.('.player-speed-wrap'))
            this.speedMenu.set(false);
    }

    private seekBy(seconds: number): void {
        const duration = Number.isFinite(this.video.duration) ? this.video.duration : Infinity;
        this.video.currentTime = Math.min(Math.max(0, this.video.currentTime + seconds), duration);
        this.updateTime();
    }

    private showHint(target: HTMLElement, text: string): void {
        target.textContent = text;
        target.classList.remove('show');
        void target.offsetWidth;    // restart the fade-out animation
        target.classList.add('show');
        clearTimeout(this.hintTimers.get(target));
        this.hintTimers.set(target, setTimeout(() => target.classList.remove('show'), HINT_MS));
    }

    /** Jumps one step; repeated taps on the same side within STREAK_MS add up ("−30 s"). */
    protected stepSeek(direction: number, now = performance.now()): void {
        const step = this.seekStep();
        this.streak = this.streak && this.streak.direction === direction && now < this.streak.until
            ? { direction, total: this.streak.total + step, until: now + STREAK_MS }
            : { direction, total: step, until: now + STREAK_MS };
        this.seekBy(direction * step);
        this.showHint((direction < 0 ? this.hintBackRef() : this.hintForwardRef()).nativeElement, `${direction < 0 ? '−' : '+'}${this.streak.total} s`);
    }

    /**
     * Downloads the frame on screen at the video's own resolution. The video comes from this server, so the canvas
     * isn't tainted; a blob link with `download` works on plain-http LAN pages, unlike the image clipboard.
     */
    protected saveFrame(): void {
        const video = this.video;
        if (!video.videoWidth || video.readyState < 2)
            return;     // no frame decoded yet
        const canvas = Object.assign(document.createElement('canvas'), { width: video.videoWidth, height: video.videoHeight });
        canvas.getContext('2d')!.drawImage(video, 0, 0);
        const fileName = `${this.name().replace(/\.[^./]+$/, '')} ${formatTime(video.currentTime).replaceAll(':', '-')}.png`;
        canvas.toBlob(blob => {
            if (!blob)
                return;
            const url = URL.createObjectURL(blob);
            const link = Object.assign(document.createElement('a'), { href: url, download: fileName });
            this.host.append(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 10000);
        }, 'image/png');
        const flash = this.flashRef().nativeElement;
        flash.classList.remove('show');
        void flash.offsetWidth;     // restart the animation
        flash.classList.add('show');
    }

    protected async toggleFullscreen(): Promise<void> {
        const video = this.video as HTMLVideoElement & { webkitEnterFullscreen?: () => void };
        try {
            if (document.fullscreenElement)
                await document.exitFullscreen();
            else if (this.host.requestFullscreen)
                await this.host.requestFullscreen({ navigationUI: 'hide' });
            else
                video.webkitEnterFullscreen?.();      // iPhone: only the video element itself can go fullscreen
        } catch {
            // Refused (e.g. not triggered by a user gesture): stay as is.
        }
    }

    protected onFullscreenChange(): void {
        this.fullscreen.set(document.fullscreenElement === this.host);
    }

    protected failed(): void {
        if (this.errored)
            return;
        this.errored = true;
        this.unsupported.emit();
    }

    protected startFrom(): void {
        const startAt = this.startAt();
        if (startAt > 0 && !this.started) {
            this.started = true;
            this.video.currentTime = Math.min(startAt, this.video.duration || startAt);
            this.updateTime();
        }
    }

    /* ---------- Controls visibility ---------- */

    protected showControls(): void {
        this.controlsVisible.set(true);
        this.scheduleHide();
    }

    private hideControls(): void {
        if (!this.video.paused && !this.speedMenu())
            this.controlsVisible.set(false);
    }

    private scheduleHide(): void {
        clearTimeout(this.hideTimer);
        if (!this.video.paused)
            this.hideTimer = setTimeout(() => this.hideControls(), HIDE_CONTROLS_MS);
    }

    private toggleControls(): void {
        if (this.controlsVisible() && !this.video.paused) {
            clearTimeout(this.hideTimer);
            this.controlsVisible.set(false);
        } else {
            this.showControls();
        }
    }

    /* ---------- Rendering ---------- */

    protected updateTime(): void {
        const video = this.video;
        const seek = this.seekRef().nativeElement;
        const duration = Number.isFinite(video.duration) ? video.duration : 0;
        this.timeText.set(`${formatTime(video.currentTime)} / ${formatTime(duration)}`);
        if (!seek.matches(':active')) {
            seek.max = String(duration);
            seek.value = String(video.currentTime);
        }
        const buffered = video.buffered.length ? video.buffered.end(video.buffered.length - 1) : 0;
        seek.style.setProperty('--played', duration ? `${(video.currentTime / duration) * 100}%` : '0%');
        seek.style.setProperty('--buffered', duration ? `${(buffered / duration) * 100}%` : '0%');
    }

    protected updatePlayState(): void {
        const paused = this.video.paused || this.video.ended;
        this.paused.set(paused);
        if (paused)
            this.showControls();
        else
            this.scheduleHide();
    }

    protected updateVolume(): void {
        this.muted.set(this.video.muted || this.video.volume === 0);
    }

    /* ---------- Taps and clicks on the picture ---------- */

    /** Touch handles single and double taps itself; the mouse uses click and dblclick. */
    protected onTap(event: PointerEvent): void {
        if (event.pointerType === 'mouse')
            return;
        if (this.endHold())
            return;     // lifting the finger after a hold is not a tap

        const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
        const x = event.clientX - rect.left;
        const direction = x < rect.width * 0.35 ? -1 : x > rect.width * 0.65 ? 1 : 0;
        const now = performance.now();

        // Already in a run of seek taps on this side: every tap jumps again.
        if (direction && this.streak && this.streak.direction === direction && now < this.streak.until) {
            clearTimeout(this.singleTapTimer);
            this.stepSeek(direction, now);
            return;
        }

        if (now - this.lastTap.time < DOUBLE_TAP_MS && Math.abs(x - this.lastTap.x) < 80) {
            clearTimeout(this.singleTapTimer);
            this.lastTap = { time: 0, x: 0 };
            if (direction)
                this.stepSeek(direction, now);
            else
                this.togglePlay();
            return;
        }

        this.lastTap = { time: now, x };
        clearTimeout(this.singleTapTimer);
        this.singleTapTimer = setTimeout(() => this.toggleControls(), DOUBLE_TAP_MS);
    }

    /** A finger held on the right third of a playing video plays it at FAST_RATE once HOLD_FAST_MS have passed. */
    protected onPointerDown(event: PointerEvent): void {
        this.pointerType = event.pointerType;
        this.endHold();
        if (event.pointerType === 'mouse')
            return;
        const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
        if (event.clientX - rect.left <= rect.width * 0.65)
            return;
        this.holdStart = { x: event.clientX, y: event.clientY };
        this.holdTimer = setTimeout(() => {
            if (this.video.paused || this.video.ended)
                return;
            clearTimeout(this.singleTapTimer);
            this.video.playbackRate = FAST_RATE;
            this.fast.set(true);
        }, HOLD_FAST_MS);
    }

    protected onPointerMove(event: PointerEvent): void {
        if (this.holdTimer && !this.fast()
            && Math.hypot(event.clientX - this.holdStart.x, event.clientY - this.holdStart.y) > HOLD_SLOP_PX) {
            clearTimeout(this.holdTimer);
            this.holdTimer = 0;
        }
    }

    /** Stops waiting for a hold, or ends one: back to the rate of the speed button. Returns whether one was on. */
    protected endHold(): boolean {
        clearTimeout(this.holdTimer);
        this.holdTimer = 0;
        if (!this.fast())
            return false;
        this.fast.set(false);
        this.video.playbackRate = this.speed();
        return true;
    }

    /** Phones open a menu on a long press, which would interrupt the hold. */
    protected onContextMenu(event: Event): void {
        if (this.pointerType !== 'mouse')
            event.preventDefault();
    }

    // Taps also produce click/dblclick events, and older Safari doesn't say they came from a touch: the pointer type
    // is remembered from pointerdown instead.
    protected onClick(): void {
        if (this.pointerType !== 'mouse')
            return;
        this.togglePlay();
        this.showControls();
    }

    protected onDoubleClick(): void {
        if (this.pointerType === 'mouse')
            void this.toggleFullscreen();
    }

    /* ---------- Seek bar and its preview ---------- */

    protected onSeekInput(): void {
        const video = this.video;
        const value = Number(this.seekRef().nativeElement.value);
        video.currentTime = value;
        this.showPreview(video.duration ? value / video.duration : 0);
        this.seekRef().nativeElement.style.setProperty('--played', video.duration ? `${(value / video.duration) * 100}%` : '0%');
        this.timeText.set(`${formatTime(value)} / ${formatTime(video.duration)}`);
        this.showControls();
    }

    /** Hover with a mouse or pen; touch shows the preview while dragging (the input event). */
    protected onSeekHover(event: PointerEvent): void {
        if (event.pointerType !== 'touch')
            this.showPreview(this.seekFraction(event));
    }

    protected onSeekLeave(): void {
        if (!this.seekRef().nativeElement.matches(':active'))
            this.hidePreview();
    }

    protected endSeekPreview(event: Event): void {
        if ((event as PointerEvent).pointerType !== 'mouse')
            this.hidePreview();
    }

    /** Shows the time at `fraction` of the seek bar above it, with the trickplay frame of that moment if any. */
    private showPreview(fraction: number): void {
        const duration = this.video.duration;
        if (!Number.isFinite(duration) || duration <= 0)
            return;
        fraction = Math.min(Math.max(fraction, 0), 1);
        const seconds = fraction * duration;
        const preview = this.previewRef().nativeElement;
        this.previewTimeRef().nativeElement.textContent = formatTime(seconds);
        preview.classList.toggle('has-frame', Boolean(this.trickplay));
        if (this.trickplay)
            this.showFrame(seconds);

        // Centred on the position, but kept within the bar.
        const barWidth = this.seekRef().nativeElement.clientWidth;
        const boxWidth = preview.offsetWidth;
        const left = Math.min(Math.max(fraction * barWidth - boxWidth / 2, 0), Math.max(0, barWidth - boxWidth));
        preview.style.transform = `translateX(${Math.round(left)}px)`;
        preview.classList.add('visible');
    }

    private showFrame(seconds: number): void {
        const trickplay = this.trickplay!;
        const { info, sheetUrl } = trickplay;
        // Every sheet is requested on the first preview, and the images kept, so moving along the bar doesn't wait.
        trickplay.sheets ??= Array.from({ length: info.sheets }, (_, index) => Object.assign(new Image(), { src: sheetUrl(index) }));
        const frame = Math.min(Math.round(seconds / info.interval), info.count - 1);
        const perSheet = info.columns * info.rows;
        const cell = frame % perSheet;
        // At most a third of the player, so it doesn't cover the picture on a phone.
        const width = Math.round(Math.min(info.tileWidth, 240, this.host.clientWidth / 3));
        const height = Math.round(width * info.tileHeight / info.tileWidth);
        const style = this.frameRef().nativeElement.style;
        style.width = `${width}px`;
        style.height = `${height}px`;
        style.backgroundImage = `url("${trickplay.sheets[Math.floor(frame / perSheet)].src}")`;
        style.backgroundSize = `${info.columns * width}px ${info.rows * height}px`;
        style.backgroundPosition = `${-(cell % info.columns) * width}px ${-Math.floor(cell / info.columns) * height}px`;
    }

    private hidePreview(): void {
        this.previewRef().nativeElement.classList.remove('visible');
    }

    /** Position of the pointer along the bar; the thumb's centre stays half its width inside the ends. */
    private seekFraction(event: PointerEvent): number {
        const rect = this.seekRef().nativeElement.getBoundingClientRect();
        const inset = 7;
        return (event.clientX - rect.left - inset) / Math.max(1, rect.width - 2 * inset);
    }

    private destroy(): void {
        clearTimeout(this.hideTimer);
        clearTimeout(this.singleTapTimer);
        clearTimeout(this.holdTimer);
        this.hintTimers.forEach(timer => clearTimeout(timer));
        if (document.fullscreenElement === this.host)
            document.exitFullscreen().catch(() => { });
        const video = this.video;
        video.pause();
        video.removeAttribute('src');
        video.load();       // stops the download
    }
}
