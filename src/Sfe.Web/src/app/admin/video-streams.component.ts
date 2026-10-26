import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { formatDuration, formatElapsed, formatSize, joinParts, plural } from '../core/format';
import { StreamActivity, StreamingStatus } from '../core/models';
import { visibleInterval } from '../core/visible-interval';

const REFRESH_MS = 2000;

const MODES: Record<StreamingStatus['mode'], string> = {
    off: 'Turned off in this space: videos the browser can\'t play are not converted (Playback › Streaming).',
    fallback: 'Videos the browser can\'t play are converted while they play.',
    always: 'Every video is converted while it plays.',
};

/** A stream's line: the video, its folder below; who watches it and how far it got on the right. */
interface Row {
    id: string;
    name: string;
    folder: string | null;
    path: string;
    who: string;
    how: string;
}

/**
 * Playback › Video streams on the administration page: the videos the server converts with ffmpeg at the moment, in every
 * space, with who watches them (`server.streams`). Refreshed while shown.
 */
@Component({
    selector: 'sfe-video-streams',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'video-streams' },
    template: `
        <div class="settings-status" aria-live="polite" [attr.data-state]="state()">
            <div class="settings-status-line"><span class="settings-status-dot"></span><span class="settings-status-text">{{ stateText() }}</span></div>
            @if (status()) {
                <span class="field-hint">{{ limitText() }}</span>
            }
        </div>
        <section class="trickplay-section">
            <header><h3>Converting now</h3><span class="trickplay-count">{{ rows().length || '' }}</span></header>
            <ul class="trickplay-list">
                @for (row of rows(); track row.id) {
                    <li class="trickplay-row stream-row" [title]="row.path">
                        <span class="trickplay-spinner"></span>
                        <span class="trickplay-file">
                            <span class="trickplay-name">{{ row.name }}</span>
                            @if (row.folder) {
                                <span class="trickplay-folder">{{ row.folder }}</span>
                            }
                        </span>
                        <span class="trickplay-detail stream-detail">
                            <span class="stream-who">{{ row.who }}</span>
                            <span>{{ row.how }}</span>
                        </span>
                    </li>
                }
            </ul>
            <p class="muted" [hidden]="rows().length > 0">No video is being converted.</p>
        </section>
    `,
})
export class VideoStreamsComponent {
    private readonly api = inject(Api);

    protected readonly status = signal<StreamingStatus | null>(null);
    protected readonly error = signal<string | null>(null);
    /** Moves the "for 3m" of each line on between two answers. */
    private readonly now = signal(Date.now());

    protected readonly state = computed(() => {
        const status = this.status();
        if (this.error())
            return 'unavailable';
        if (!status)
            return null;
        if (status.streams.length)
            return 'running';
        return status.available ? 'idle' : status.mode === 'off' ? 'disabled' : 'unavailable';
    });
    protected readonly stateText = computed(() => {
        const error = this.error();
        if (error)
            return `Could not read the streams: ${error}`;
        const status = this.status();
        if (!status)
            return '…';
        if (!status.available && status.mode !== 'off')
            return 'ffmpeg can\'t be found: set its path (Thumbnails:FfmpegPath) in the server\'s configuration file. System › About tells where it looks.';
        return MODES[status.mode];
    });
    protected readonly limitText = computed(() => {
        const status = this.status();
        return status ? `At most ${plural(status.maxStreams, 'video')} at once on the whole server; more viewers wait for one to end.` : '';
    });
    protected readonly rows = computed(() => (this.status()?.streams ?? []).map(stream => this.row(stream)));

    constructor() {
        void this.refresh();
        inject(DestroyRef).onDestroy(visibleInterval(() => void this.refresh(), REFRESH_MS));
    }

    private async refresh(): Promise<void> {
        try {
            this.status.set(await this.api.streamingStatus());
            this.now.set(Date.now());
            this.error.set(null);
        } catch (error) {
            this.error.set((error as Error).message);
        }
    }

    private row(stream: StreamActivity): Row {
        const slash = stream.path.lastIndexOf('/');
        const { library, user, address } = stream.viewer;
        const seconds = (this.now() - Date.parse(stream.started)) / 1000;
        return {
            id: stream.id,
            name: stream.path.slice(slash + 1),
            folder: slash > 0 ? stream.path.slice(0, slash) : null,
            path: stream.path,
            who: joinParts([user ?? 'Visitor', library, address]),
            how: joinParts([
                stream.start > 0 ? `from ${formatDuration(stream.start)}` : 'from the start',
                stream.transcoded ? 'converted' : 'repackaged',
                formatSize(stream.bytes),
                `for ${formatElapsed(seconds)}`,
            ]),
        };
    }
}
