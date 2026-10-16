import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiMock, mockApi, settle, testProviders } from '../../testing/fakes';
import { StreamingStatus } from '../core/models';
import { VideoStreamsComponent } from './video-streams.component';

describe('Video streams', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<VideoStreamsComponent>;
    const text = () => (fixture.nativeElement as HTMLElement).textContent!.replace(/\s+/g, ' ');

    function status(overrides: Partial<StreamingStatus> = {}): StreamingStatus {
        return { available: true, mode: 'fallback', maxStreams: 2, streams: [], ...overrides };
    }

    async function create(): Promise<void> {
        fixture = TestBed.createComponent(VideoStreamsComponent);
        await settle();
    }

    beforeEach(() => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
    });

    afterEach(() => TestBed.resetTestingModule());

    it('lists the videos being converted, with who watches them', async () => {
        api.streamingStatus.mockResolvedValue(status({
            streams: [{
                id: 'a', path: 'Films/Old/film.avi', viewer: { library: 'Home', user: 'alice', address: '10.0.0.2' },
                start: 754, transcoded: true, started: new Date(Date.now() - 125_000).toISOString(), bytes: 3 * 1024 * 1024,
            }],
        }));
        await create();
        const row = fixture.nativeElement.querySelector('.stream-row') as HTMLElement;
        expect(row.querySelector('.trickplay-name')!.textContent).toBe('film.avi');
        expect(row.querySelector('.trickplay-folder')!.textContent).toBe('Films/Old');
        expect(row.querySelector('.stream-who')!.textContent).toBe('alice · Home · 10.0.0.2');
        expect(row.textContent).toContain('from 12:34 · converted · 3 MB · for 2m 05s');
        expect(text()).toContain('At most 2 videos at once');
    });

    it('says why nothing is converted', async () => {
        api.streamingStatus.mockResolvedValue(status({ available: false }));
        await create();
        expect(text()).toContain('No video is being converted.');
        expect(text()).toContain('ffmpeg can\'t be found');

        api.streamingStatus.mockResolvedValue(status({ available: false, mode: 'off' }));
        await create();
        expect(text()).toContain('Turned off in this space');
        expect(text()).toContain('(Playback › Streaming)');
    });
});
