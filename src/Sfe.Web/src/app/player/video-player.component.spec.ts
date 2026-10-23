import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { settle } from '../../testing/fakes';
import { VideoStream } from '../core/config.service';
import { StreamProbe } from '../core/models';
import { VideoPlayerComponent } from './video-player.component';

describe('VideoPlayerComponent', () => {
    @Component({
        imports: [VideoPlayerComponent],
        template: `<sfe-video-player src="api/files/raw?path=clip.webm" [loop]="loop()" />`,
    })
    class Host {
        readonly loop = signal(false);
    }

    let fixture: ComponentFixture<Host>;

    const video = () => fixture.nativeElement.querySelector('video') as HTMLVideoElement;
    const loopButton = () => fixture.nativeElement.querySelector('.player-toggle') as HTMLButtonElement;
    const player = () => fixture.debugElement.children[0].componentInstance as VideoPlayerComponent;

    async function create(loop: boolean): Promise<void> {
        fixture = TestBed.createComponent(Host);
        fixture.componentInstance.loop.set(loop);
        await settle();
    }

    beforeEach(() => {
        // jsdom doesn't play media.
        vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue();
        vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => { });
        vi.spyOn(HTMLMediaElement.prototype, 'load').mockImplementation(() => { });
    });

    afterEach(() => {
        TestBed.resetTestingModule();      // destroys the players while the media methods are still stubbed
        vi.restoreAllMocks();
    });

    it('starts with the loop given by the settings', async () => {
        await create(false);
        expect(video().loop).toBe(false);
        expect(loopButton().getAttribute('aria-pressed')).toBe('false');
        expect(loopButton().title).toBe('Loop (R)');

        await create(true);
        expect(video().loop).toBe(true);
        expect(loopButton().classList).toContain('on');
        expect(loopButton().getAttribute('aria-pressed')).toBe('true');
        expect(loopButton().title).toBe('Stop looping (R)');
    });

    it('toggles the loop with its button', async () => {
        await create(false);
        loopButton().click();
        await settle();
        expect(video().loop).toBe(true);
        expect(loopButton().classList).toContain('on');

        loopButton().click();
        await settle();
        expect(video().loop).toBe(false);
        expect(loopButton().classList).not.toContain('on');
    });

    it('gives its controls to a context menu: play, speed, loop, mute, frame, full screen', async () => {
        await create(false);
        const entries = () => player().menuEntries();
        const entry = (id: string) => entries().find(e => e.id === id)!;

        expect(entries().map(e => e.id)).toEqual(['play', 'speed', 'loop', 'mute', 'saveFrame', 'fullscreen']);
        expect(entry('play')).toMatchObject({ label: 'Play', shortcut: 'K' });
        entry('play').run!();
        expect(HTMLMediaElement.prototype.play).toHaveBeenCalled();

        entry('loop').run!();
        await settle();
        expect(video().loop).toBe(true);
        expect(entry('loop').label).toBe('Stop looping');

        const rates = entry('speed').submenu!();
        expect(rates.map(rate => rate.label)).toEqual(['0.5×', '0.75×', '1×', '1.25×', '1.5×', '2×']);
        expect(rates[2].icon).toBe('check');
        rates[4].run!();
        expect(video().playbackRate).toBe(1.5);
        expect(entry('speed').label).toBe('Speed: 1.5×');
    });

    describe('playback speed', () => {
        const speedButton = () => fixture.nativeElement.querySelector('.player-speed') as HTMLButtonElement;
        const menu = () => fixture.nativeElement.querySelector('.player-speed-menu') as HTMLElement | null;
        const rates = () => Array.from(menu()!.querySelectorAll('button'));

        it('lists every rate when its button is clicked, the current one marked', async () => {
            await create(false);
            expect(menu()).toBeNull();

            speedButton().click();
            await settle();
            expect(rates().map(rate => rate.textContent)).toEqual(['0.5×', '0.75×', '1×', '1.25×', '1.5×', '2×']);
            expect(rates().filter(rate => rate.classList.contains('on')).map(rate => rate.textContent)).toEqual(['1×']);
            expect(speedButton().getAttribute('aria-expanded')).toBe('true');
        });

        it('puts the rates in rows when the player is too short for a column', async () => {
            await create(false);
            const host = fixture.nativeElement.querySelector('sfe-video-player') as HTMLElement;
            Object.defineProperty(host, 'clientHeight', { configurable: true, value: 400 });
            speedButton().click();
            await settle();
            expect(menu()!.classList).not.toContain('wide');
            expect(menu()!.style.maxHeight).toBe('336px');

            speedButton().click();
            Object.defineProperty(host, 'clientHeight', { configurable: true, value: 150 });
            speedButton().click();
            await settle();
            expect(menu()!.classList).toContain('wide');
        });

        it('plays at the rate picked and closes the list', async () => {
            await create(false);
            speedButton().click();
            await settle();
            rates()[4].click();
            await settle();

            expect(video().playbackRate).toBe(1.5);
            expect(speedButton().textContent).toBe('1.5×');
            expect(menu()).toBeNull();

            speedButton().click();
            await settle();
            expect(menu()!.querySelector('.on')!.textContent).toBe('1.5×');
        });

        it('is a phone sheet over a backdrop that closes it', async () => {
            await create(false);
            speedButton().click();
            await settle();

            expect(menu()!.classList).toContain('phone-sheet');
            fixture.nativeElement.querySelector('.player-speed-wrap .sheet-backdrop').click();
            await settle();
            expect(menu()).toBeNull();
        });

        it('closes on a press elsewhere, on Escape and on its button', async () => {
            await create(false);
            speedButton().click();
            await settle();
            menu()!.dispatchEvent(new Event('pointerdown', { bubbles: true }));
            await settle();
            expect(menu()).not.toBeNull();
            document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }));
            await settle();
            expect(menu()).toBeNull();

            speedButton().click();
            await settle();
            expect(player().handleKey(new KeyboardEvent('keydown', { key: 'Escape' }))).toBe(true);
            await settle();
            expect(menu()).toBeNull();
            expect(player().handleKey(new KeyboardEvent('keydown', { key: 'Escape' }))).toBe(false);

            speedButton().click();
            await settle();
            speedButton().click();
            await settle();
            expect(menu()).toBeNull();
            expect(video().playbackRate).toBe(1);
        });
    });

    describe('holding a finger on the right', () => {
        const surface = () => fixture.nativeElement.querySelector('.player-surface') as HTMLElement;
        const host = () => fixture.nativeElement.querySelector('sfe-video-player') as HTMLElement;

        function press(type: string, x: number, y = 100, pointerType = 'touch'): void {
            const event = new MouseEvent(type, { bubbles: true, cancelable: true, clientX: x, clientY: y });
            Object.defineProperty(event, 'pointerType', { value: pointerType });
            surface().dispatchEvent(event);
        }

        async function createPlaying(): Promise<void> {
            await create(false);
            surface().getBoundingClientRect = () => ({ left: 0, top: 0, width: 1000, height: 500, right: 1000, bottom: 500, x: 0, y: 0, toJSON: () => ({}) });
            Object.defineProperty(video(), 'paused', { configurable: true, value: false });
            vi.useFakeTimers();
        }

        afterEach(() => vi.useRealTimers());

        it('plays at 2× after 1.5 s until the finger lifts, then at the chosen rate again', async () => {
            await createPlaying();
            press('pointerdown', 900);
            await vi.advanceTimersByTimeAsync(1400);
            expect(video().playbackRate).toBe(1);

            await vi.advanceTimersByTimeAsync(100);
            fixture.detectChanges();
            expect(video().playbackRate).toBe(2);
            expect(host().classList).toContain('fast');

            press('pointerup', 900);
            await vi.advanceTimersByTimeAsync(500);
            fixture.detectChanges();
            expect(video().playbackRate).toBe(1);
            expect(host().classList).not.toContain('fast');
            expect(host().classList).toContain('controls-visible');      // the lift was not taken for a tap
        });

        it('goes back to the rate of the speed button', async () => {
            await createPlaying();
            vi.useRealTimers();
            (fixture.nativeElement.querySelector('.player-speed') as HTMLButtonElement).click();
            await settle();
            (fixture.nativeElement.querySelectorAll('.player-speed-menu button')[1] as HTMLButtonElement).click();
            await settle();
            vi.useFakeTimers();

            press('pointerdown', 900);
            await vi.advanceTimersByTimeAsync(1500);
            expect(video().playbackRate).toBe(2);
            press('pointercancel', 900);
            expect(video().playbackRate).toBe(0.75);
        });

        it('does nothing on the left or middle, with a moving finger, a mouse or a paused video', async () => {
            await createPlaying();
            for (const x of [100, 500]) {
                press('pointerdown', x);
                await vi.advanceTimersByTimeAsync(2000);
                expect(video().playbackRate).toBe(1);
                press('pointerup', x);
            }

            press('pointerdown', 900);
            press('pointermove', 900, 150);
            await vi.advanceTimersByTimeAsync(2000);
            expect(video().playbackRate).toBe(1);
            press('pointerup', 900, 150);

            press('pointerdown', 900, 100, 'mouse');
            await vi.advanceTimersByTimeAsync(2000);
            expect(video().playbackRate).toBe(1);
            press('pointerup', 900, 100, 'mouse');

            Object.defineProperty(video(), 'paused', { configurable: true, value: true });
            press('pointerdown', 900);
            await vi.advanceTimersByTimeAsync(2000);
            expect(video().playbackRate).toBe(1);
        });
    });

    describe('conversion by the server', () => {
        @Component({
            imports: [VideoPlayerComponent],
            template: `<sfe-video-player src="api/files/raw/clip.avi" [stream]="stream()" [startAt]="startAt()" (unsupported)="failures.push($event)" />`,
        })
        class StreamHost {
            readonly stream = signal<VideoStream | null>(null);
            readonly startAt = signal(0);
            readonly failures: boolean[] = [];
        }

        let host: ComponentFixture<StreamHost>;
        const element = () => host.nativeElement.querySelector('video') as HTMLVideoElement;
        const component = () => host.debugElement.children[0].componentInstance as VideoPlayerComponent;
        const convertButton = () => host.nativeElement.querySelector('.player-convert') as HTMLButtonElement | null;
        const timeText = () => (host.nativeElement.querySelector('.player-time') as HTMLElement).textContent;

        function fakeStream(always = false, probe: StreamProbe | null = { duration: 600, transcoded: true }) {
            return {
                always,
                url: vi.fn((start: number) => `api/streaming/play?t=${start}`),
                probe: vi.fn(() => Promise.resolve(probe)),
            };
        }

        /** jsdom's media elements neither seek nor know what they can seek in. */
        function fakeMedia(seekable: [number, number][] = []): void {
            let time = 0;
            Object.defineProperty(element(), 'currentTime', { configurable: true, get: () => time, set: (value: number) => time = value });
            Object.defineProperty(element(), 'seekable', {
                configurable: true,
                value: { length: seekable.length, start: (i: number) => seekable[i][0], end: (i: number) => seekable[i][1] },
            });
        }

        async function createWith(stream: ReturnType<typeof fakeStream> | null, startAt = 0): Promise<void> {
            host = TestBed.createComponent(StreamHost);
            host.componentInstance.stream.set(stream);
            host.componentInstance.startAt.set(startAt);
            await settle();
        }

        it('says the browser can\'t play the video when the server converts none', async () => {
            await createWith(null);
            expect(convertButton()).toBeNull();
            element().dispatchEvent(new Event('error'));
            await settle();
            expect(host.componentInstance.failures).toEqual([false]);
        });

        it('plays the conversion from where it was to start when the file fails', async () => {
            const stream = fakeStream();
            await createWith(stream, 30);
            expect(element().getAttribute('src')).toBe('api/files/raw/clip.avi');
            expect(convertButton()).not.toBeNull();

            element().dispatchEvent(new Event('error'));
            await settle();
            expect(stream.probe).toHaveBeenCalledOnce();
            expect(stream.url).toHaveBeenCalledWith(30);
            expect(element().getAttribute('src')).toBe('api/streaming/play?t=30');
            expect(host.componentInstance.failures).toEqual([]);
            expect(convertButton()).toBeNull();

            fakeMedia();
            element().dispatchEvent(new Event('loadedmetadata'));
            await settle();
            expect(component().currentTime()).toBe(30);
            expect(timeText()).toBe('0:30 / 10:00');      // the length comes from the server

            element().dispatchEvent(new Event('error'));
            await settle();
            expect(host.componentInstance.failures).toEqual([true]);
        });

        it('converts a video whose pictures the browser can\'t decode', async () => {
            const stream = fakeStream();
            await createWith(stream);
            Object.defineProperty(element(), 'videoWidth', { configurable: true, value: 0 });
            element().dispatchEvent(new Event('loadedmetadata'));
            await settle();
            expect(element().getAttribute('src')).toBe('api/streaming/play?t=0');
        });

        it('gives up when the server can\'t convert it either', async () => {
            await createWith(fakeStream(false, null));
            element().dispatchEvent(new Event('error'));
            await settle();
            expect(host.componentInstance.failures).toEqual([true]);
            expect(element().getAttribute('src')).toBe('api/files/raw/clip.avi');
        });

        it('converts the video on screen with its button', async () => {
            const stream = fakeStream();
            await createWith(stream);
            convertButton()!.click();
            await settle();
            expect(element().getAttribute('src')).toBe('api/streaming/play?t=0');
        });

        it('always converts, seeking within what arrived and asking again further away', async () => {
            const stream = fakeStream(true);
            await createWith(stream, 5);
            expect(element().getAttribute('src')).toBe('api/streaming/play?t=5');
            expect(convertButton()).toBeNull();

            fakeMedia();
            expect(component().handleKey(new KeyboardEvent('keydown', { key: 'l' }))).toBe(true);
            await settle();
            expect(element().getAttribute('src')).toBe('api/streaming/play?t=15');

            fakeMedia([[0, 60]]);
            component().handleKey(new KeyboardEvent('keydown', { key: 'l' }));
            await settle();
            expect(element().getAttribute('src')).toBe('api/streaming/play?t=15');
            expect(element().currentTime).toBe(10);
            expect(component().currentTime()).toBe(25);
        });
    });

    describe('cinema mode and size', () => {
        @Component({
            imports: [VideoPlayerComponent],
            template: `<sfe-video-player src="api/files/raw?path=clip.webm" [cinemaButton]="cinema()" />`,
        })
        class CinemaHost {
            readonly cinema = signal(true);
        }

        let host: ComponentFixture<CinemaHost>;
        const element = () => host.debugElement.children[0].nativeElement as HTMLElement;
        const component = () => host.debugElement.children[0].componentInstance as VideoPlayerComponent;
        const cinemaButton = () => host.nativeElement.querySelector('.player-cinema') as HTMLButtonElement | null;
        const minWidth = () => parseFloat(element().style.getPropertyValue('--player-min-width'));

        async function createWith(cinema: boolean): Promise<void> {
            host = TestBed.createComponent(CinemaHost);
            host.componentInstance.cinema.set(cinema);
            await settle();
        }

        it('is at least as wide as its row of buttons, wider with more of them', async () => {
            await createWith(false);
            const buttons = element().querySelectorAll('.player-row > .player-button, .player-speed-wrap > .player-button').length;
            const without = minWidth();
            expect(without).toBeGreaterThanOrEqual(buttons * 40 + 80);

            host.componentInstance.cinema.set(true);
            await settle();
            expect(cinemaButton()).not.toBeNull();
            expect(minWidth()).toBeGreaterThan(without);
        });

        it('switches cinema mode with its button and remembers it in this browser', async () => {
            await createWith(true);
            expect(element().classList).not.toContain('cinema');
            expect(cinemaButton()!.title).toBe('Cinema mode (C)');

            cinemaButton()!.click();
            await settle();
            expect(element().classList).toContain('cinema');
            expect(cinemaButton()!.getAttribute('aria-pressed')).toBe('true');
            expect(cinemaButton()!.title).toBe('Default view (C)');
            expect(localStorage.getItem('sfe.player.cinema')).toBe('1');

            // The next video (the viewer makes a player per item) opens in cinema mode too.
            await createWith(true);
            expect(element().classList).toContain('cinema');
        });

        it('switches with the C key only where it is offered', async () => {
            await createWith(false);
            expect(cinemaButton()).toBeNull();
            expect(component().handleKey(new KeyboardEvent('keydown', { key: 'c' }))).toBe(false);
            expect(element().classList).not.toContain('cinema');

            await createWith(true);
            expect(component().handleKey(new KeyboardEvent('keydown', { key: 'C' }))).toBe(true);
            await settle();
            expect(element().classList).toContain('cinema');
        });
    });

    it('toggles the loop with the R key', async () => {
        await create(true);
        expect(player().handleKey(new KeyboardEvent('keydown', { key: 'R' }))).toBe(true);
        await settle();
        expect(video().loop).toBe(false);

        expect(player().handleKey(new KeyboardEvent('keydown', { key: 'r', ctrlKey: true }))).toBe(false);
        await settle();
        expect(video().loop).toBe(false);
    });
});
