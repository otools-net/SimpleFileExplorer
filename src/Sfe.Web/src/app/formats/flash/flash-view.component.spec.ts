import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiMock, item, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { FILE_VIEW_HOST, FLASH_FORMAT } from '../formats';
import { FLASH_LOADER, FlashEvents } from './flash-player';
import { FLASH_MUTED_KEY, FLASH_VOLUME_KEY, FlashViewComponent } from './flash-view.component';

/** A movie as the view sees it, its commands recorded. */
function fakeHandle() {
    return {
        frame: document.createElement('iframe'),
        play: vi.fn<() => void>(),
        pause: vi.fn<() => void>(),
        setVolume: vi.fn<(volume: number) => void>(),
        restart: vi.fn<() => void>(),
        focus: vi.fn<() => void>(),
        destroy: vi.fn<() => void>(),
    };
}

describe('FlashViewComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<FlashViewComponent>;
    let element: HTMLElement;
    let events: FlashEvents;
    let opened: { container: HTMLElement; url: string; volume: number }[];
    let handle: ReturnType<typeof fakeHandle>;
    let failure: Error | null;
    let canDownload: boolean;

    async function stable(): Promise<void> {
        await settle();
        await fixture.whenStable();
    }

    async function show(name = 'Games/Run.swf'): Promise<void> {
        fixture = TestBed.createComponent(FlashViewComponent);
        element = fixture.nativeElement;
        fixture.componentRef.setInput('item', { ...item(name, { kind: 'file' }), size: 48_000 });
        fixture.componentRef.setInput('format', FLASH_FORMAT);
        await stable();
        await stable();
    }

    function button(selector: string): HTMLButtonElement {
        return element.querySelector<HTMLButtonElement>(selector)!;
    }

    beforeEach(async () => {
        localStorage.removeItem(FLASH_VOLUME_KEY);
        localStorage.removeItem(FLASH_MUTED_KEY);
        api = mockApi();
        api.flashLink.mockResolvedValue({ url: 'api/files/flash/TOKEN/Run.swf', expires: '2026-10-26T00:00:00Z' });
        opened = [];
        failure = null;
        canDownload = true;
        handle = fakeHandle();
        const loader = async (container: HTMLElement, url: string, volume: number, given: FlashEvents) => {
            events = given;
            opened.push({ container, url, volume });
            if (failure)
                throw failure;
            return handle;
        };
        TestBed.configureTestingModule({
            providers: testProviders(api,
                { provide: FILE_VIEW_HOST, useValue: { place: 'viewer', can: () => canDownload } },
                { provide: FLASH_LOADER, useValue: loader }),
        });
        await loadConfig(api);
    });

    afterEach(() => {
        localStorage.removeItem(FLASH_VOLUME_KEY);
        localStorage.removeItem(FLASH_MUTED_KEY);
        fixture?.destroy();
        TestBed.resetTestingModule();
    });

    it('plays the movie from a flash link of the app, in its stage, and stops it when closed', async () => {
        await show();

        expect(api.flashLink).toHaveBeenCalledWith('Games/Run.swf');
        expect(opened).toHaveLength(1);
        expect(opened[0].url).toBe(new URL('api/files/flash/TOKEN/Run.swf', document.baseURI).href);
        expect(opened[0].container.classList).toContain('flash-stage');
        expect(opened[0].volume).toBe(1);
        expect(handle.focus).toHaveBeenCalled();
        expect(element.hasAttribute('data-owns-keys')).toBe(true);
        expect(element.querySelector('.text-status')!.textContent).toContain('Flash');

        fixture.destroy();
        expect(handle.destroy).toHaveBeenCalled();
    });

    it('tells what the movie is once Ruffle has read it', async () => {
        await show();

        events.metadata({ width: 550, height: 400, frameRate: 24, numFrames: 1, swfVersion: 10, isActionScript3: true, backgroundColor: '#000000' });
        events.state(true);
        await stable();

        expect(element.querySelector('.text-status')!.textContent).toContain('Flash  ·  ActionScript 3  ·  SWF 10  ·  550 × 400  ·  24 fps');
        expect(element.querySelector('[data-playing]')!.textContent).toBe('Playing');
    });

    it('pauses, plays and starts again', async () => {
        await show();
        events.state(true);
        await stable();

        button('[data-play]').click();
        expect(handle.pause).toHaveBeenCalledTimes(1);
        events.state(false);
        await stable();
        expect(button('[data-play]').getAttribute('aria-label')).toBe('Play');
        button('[data-play]').click();
        expect(handle.play).toHaveBeenCalledTimes(1);
        button('[data-restart]').click();
        expect(handle.restart).toHaveBeenCalledTimes(1);
    });

    it('keeps the volume and the mute of this browser', async () => {
        localStorage.setItem(FLASH_VOLUME_KEY, '0.4');
        await show();
        expect(opened[0].volume).toBe(0.4);

        button('[data-mute]').click();
        await stable();
        expect(handle.setVolume).toHaveBeenLastCalledWith(0);
        expect(localStorage.getItem(FLASH_MUTED_KEY)).toBe('1');

        const slider = element.querySelector<HTMLInputElement>('[data-volume]')!;
        slider.value = '0.7';
        slider.dispatchEvent(new Event('input'));
        await stable();
        expect(handle.setVolume).toHaveBeenLastCalledWith(0.7);
        expect(localStorage.getItem(FLASH_VOLUME_KEY)).toBe('0.7');
        expect(localStorage.getItem(FLASH_MUTED_KEY), 'moving the volume ends the mute').toBe('0');
    });

    it('opens muted when muted before', async () => {
        localStorage.setItem(FLASH_MUTED_KEY, '1');
        await show();
        expect(opened[0].volume).toBe(0);
    });

    it('passes Esc pressed inside the movie on to the viewer', async () => {
        await show();
        const heard = vi.fn();
        element.parentElement!.addEventListener('keydown', event => heard(event.key));

        events.escape();

        expect(heard).toHaveBeenCalledWith('Escape');
    });

    it('offers to download a movie Ruffle can not play', async () => {
        failure = new Error('not a SWF');
        await show();

        expect(element.querySelector('.text-message')!.textContent).toContain('This movie could not be played: not a SWF');
        expect(element.querySelector('a.button')).not.toBeNull();
        expect(element.querySelector<HTMLElement>('.flash-stage')!.hidden).toBe(true);
    });

    it('says so without a link when Flash is off or the file is gone', async () => {
        canDownload = false;
        api.flashLink.mockRejectedValue(new Error('Not found'));
        await show();

        expect(opened).toHaveLength(0);
        expect(element.querySelector('.text-message')!.textContent).toContain('could not be played');
        expect(element.querySelector('a.button'), 'no right to download').toBeNull();
    });
});
