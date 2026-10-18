import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EditorView } from '@codemirror/view';
import { ApiError } from '../../core/api.service';
import { TextAccess, TextFile } from '../../core/models';
import { ApiMock, item, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { FILE_VIEW_HOST, SUBTITLES_FORMAT } from '../formats';
import { READING_KEY } from '../reading/reading.service';
import { SUBTITLES_MODE_KEY, SubtitlesViewComponent } from './subtitles-view.component';

const SRT = [
    '1', '00:00:01,000 --> 00:00:03,000', 'Where are we <i>going</i>?', '',
    '2', '00:00:04,000 --> 00:00:06,500', 'Up the ridge.', '',
    '3', '00:00:06,000 --> 00:00:08,000', 'Then down to the lake.', '',
].join('\n');

describe('SubtitlesViewComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<SubtitlesViewComponent>;
    let element: HTMLElement;
    let can: (right: string) => boolean;

    const ALL: TextAccess = { overwrite: true, saveCopy: true, download: true, permanent: false };
    const text = (overrides: Partial<TextFile> = {}): TextFile => ({
        text: SRT, encoding: 'utf-8', lineEnding: 'lf', version: 'v1', size: 150, modified: '2026-10-18T10:00:00Z', access: ALL, ...overrides,
    });
    const list = () => element.querySelector<HTMLElement>('.subtitle-list')!;
    const texts = () => [...element.querySelectorAll('.subtitle-cue .subtitle-text')].map(cue => cue.textContent);
    const cue = (index: number) => element.querySelector<HTMLElement>(`.subtitle-cue[data-cue="${index}"]`)!;
    const status = () => element.querySelector('[data-counts]')!.textContent;
    const view = () => EditorView.findFromDOM(element.querySelector<HTMLElement>('.json-source .cm-editor')!)!;

    async function stable(): Promise<void> {
        await settle();
        await fixture.whenStable();
    }

    async function show(name = 'Film.en.srt'): Promise<void> {
        fixture = TestBed.createComponent(SubtitlesViewComponent);
        element = fixture.nativeElement;
        fixture.componentRef.setInput('item', item(`Films/${name}`, { kind: 'file' }));
        fixture.componentRef.setInput('format', SUBTITLES_FORMAT);
        await vi.waitFor(async () => {
            await stable();
            expect(element.querySelector('.text-status')).not.toBeNull();
        });
    }

    async function keydown(target: Element, key: string, init: KeyboardEventInit = {}): Promise<KeyboardEvent> {
        const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
        target.dispatchEvent(event);
        await stable();
        return event;
    }

    beforeEach(async () => {
        for (const key of [READING_KEY, SUBTITLES_MODE_KEY])
            localStorage.removeItem(key);
        api = mockApi();
        api.textFile.mockResolvedValue(text());
        api.subtitleVideo.mockResolvedValue({ path: 'Films/Film.mkv' });
        can = () => true;
        TestBed.configureTestingModule({ providers: testProviders(api, { provide: FILE_VIEW_HOST, useValue: { place: 'viewer', can: (right: string) => can(right) } }) });
        await loadConfig(api);
    });

    afterEach(() => {
        for (const key of [READING_KEY, SUBTITLES_MODE_KEY])
            localStorage.removeItem(key);
        fixture?.destroy();
        TestBed.resetTestingModule();
    });

    it('lists the cues with their times, marks and overlaps', async () => {
        await show();

        expect(texts()).toEqual(['Where are we going?', 'Up the ridge.', 'Then down to the lake.']);
        expect(cue(0).querySelector('.italic')!.textContent).toBe('going');
        expect([...cue(1).querySelectorAll('.subtitle-time')].map(time => time.textContent)).toEqual(['00:04.000', '00:06.500']);
        expect(cue(2).classList).toContain('overlaps');
        expect(status()).toBe('3 cues · 8 s · 1 overlapping');
        expect(element.querySelector('.text-status')!.textContent).toContain('SubRip');
        expect(element.hasAttribute('data-owns-keys')).toBe(true);
    });

    it('plays the cue selected in the video next to it, from its time', async () => {
        await show();

        cue(1).click();
        await stable();
        await keydown(list(), 'ArrowDown');

        expect(element.querySelector('[data-selected-cue]')!.textContent).toBe('Cue 3 · 00:06.000 · 2 s');
        const play = element.querySelector<HTMLAnchorElement>('[data-play]')!;
        expect(play.getAttribute('href')).toContain('player?path=Films%2FFilm.mkv');
        expect(play.getAttribute('href')).toContain('t=6');
        expect(api.subtitleVideo).toHaveBeenCalledWith('Films/Film.en.srt', expect.anything());
    });

    it('offers no Play without a video, nor asks for one without the right to view files', async () => {
        api.subtitleVideo.mockResolvedValue({ path: null });
        await show();
        cue(0).click();
        await stable();
        expect(element.querySelector('[data-play]')).toBeNull();

        fixture.destroy();
        api.subtitleVideo.mockClear();
        can = right => right !== 'files.view';
        await show();
        expect(api.subtitleVideo).not.toHaveBeenCalled();
    });

    it('filters the cues', async () => {
        await show();
        const box = element.querySelector<HTMLInputElement>('.json-filter')!;
        box.value = 'RIDGE';
        box.dispatchEvent(new Event('input'));

        await vi.waitFor(async () => {
            await stable();
            expect(texts()).toEqual(['Up the ridge.']);
        });
        expect(status()).toContain('1 of 3 cues');
    });

    it('shows a cue in the source, beside the cues', async () => {
        await show();

        cue(1).dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
        await stable();

        expect(element.getAttribute('data-mode')).toBe('split');
        await vi.waitFor(() => expect(view().state.doc.lineAt(view().state.selection.main.from).number).toBe(6));
    });

    it('tells the lines where it isn\'t valid', async () => {
        api.textFile.mockResolvedValue(text({ text: 'WEBVTT\n\n00:01.000 --> nonsense\nText\n' }));
        await show('clip.vtt');

        expect(element.querySelector('[data-problem]')!.textContent).toContain('Line 3:');
        expect(element.querySelector('.text-status')!.textContent).toContain('WebVTT');
    });

    it('moves every cue by the seconds typed while editing, one undo', async () => {
        await show();

        element.querySelector<HTMLButtonElement>('[data-edit]')!.click();
        await stable();
        await vi.waitFor(() => expect(view().state.readOnly).toBe(false));
        element.querySelector<HTMLInputElement>('[data-shift-box]')!.value = '-0.5';
        element.querySelector<HTMLButtonElement>('[data-shift]')!.click();
        await stable();

        expect(view().state.doc.line(2).text).toBe('00:00:00,500 --> 00:00:02,500');
        expect(view().state.doc.line(10).text).toBe('00:00:05,500 --> 00:00:07,500');
        await vi.waitFor(async () => {
            await stable();
            expect([...cue(0).querySelectorAll('.subtitle-time')].map(time => time.textContent)).toEqual(['00:00.500', '00:02.500']);
        });
        expect(element.querySelector('[data-edited]')).not.toBeNull();
    });

    it('refuses a shift that isn\'t a number of seconds', async () => {
        await show();
        element.querySelector<HTMLButtonElement>('[data-edit]')!.click();
        await stable();
        await vi.waitFor(() => expect(view().state.readOnly).toBe(false));

        element.querySelector<HTMLInputElement>('[data-shift-box]')!.value = 'later';
        element.querySelector<HTMLButtonElement>('[data-shift]')!.click();
        await stable();

        expect(view().state.doc.toString()).toBe(SRT);
    });

    it('offers a download when the file cannot be shown', async () => {
        api.textFile.mockRejectedValue(new ApiError(413, 'too large', null, 'too-large'));
        fixture = TestBed.createComponent(SubtitlesViewComponent);
        element = fixture.nativeElement;
        fixture.componentRef.setInput('item', item('Films/huge.srt', { kind: 'file' }));
        await stable();

        expect(element.querySelector('.text-message')!.textContent).toContain('too large');
        expect(element.querySelector('.text-message a')!.getAttribute('href')).toContain('download=true');
    });
});
