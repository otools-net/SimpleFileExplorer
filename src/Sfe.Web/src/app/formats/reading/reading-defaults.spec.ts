import { TestBed } from '@angular/core/testing';
import { Rights } from '../../core/rights';
import { ApiMock, fakeConfig, limitedConfig, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { DEFAULT_READING } from './reading';
import { READING_KEY, ReadingService } from './reading.service';
import { ReadingPanelComponent } from './reading-panel.component';

describe('Reading preferences of the account', () => {
    let api: ApiMock;
    const accounts = { ...DEFAULT_READING, theme: 'sepia', prose: { ...DEFAULT_READING.prose, size: 20, font: 'atkinson' } };

    async function setUp(config = fakeConfig({ reading: accounts })): Promise<ReadingService> {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api, config);
        return TestBed.inject(ReadingService);
    }

    async function panel(): Promise<HTMLElement> {
        const fixture = TestBed.createComponent(ReadingPanelComponent);
        fixture.componentRef.setInput('profile', 'prose');
        await settle();
        await fixture.whenStable();
        return fixture.nativeElement as HTMLElement;
    }

    beforeEach(() => localStorage.removeItem(READING_KEY));

    afterEach(() => {
        localStorage.removeItem(READING_KEY);
        TestBed.resetTestingModule();
    });

    it("starts from the account's defaults, checked, under this browser's changes", async () => {
        const reading = await setUp(fakeConfig({ reading: { ...accounts, code: { ...DEFAULT_READING.code, lineHeight: 7 } } }));

        expect(reading.preferences().theme).toBe('sepia');
        expect(reading.profile('prose').font).toBe('atkinson');
        expect(reading.profile('code').lineHeight).toBe(DEFAULT_READING.code.lineHeight);
        expect(reading.changed()).toBe(false);

        reading.update('prose', { size: 24 });
        expect(reading.changed()).toBe(true);
        reading.changeSize('prose', 0);
        expect(reading.profile('prose').size).toBe(20);
    });

    it("saves this browser's preferences as the account's, then follows them", async () => {
        const reading = await setUp();
        reading.update('code', { wrap: true });
        reading.setTheme('dark');
        api.savePreferenceChanges.mockResolvedValue({ values: {}, customized: [] } as never);
        api.config.mockResolvedValue(fakeConfig({ reading: reading.preferences() }));
        const element = await panel();

        element.querySelector<HTMLButtonElement>('[data-save-defaults]')!.click();
        await settle();

        const values = api.savePreferenceChanges.mock.calls[0][0].values!;
        expect(values).toMatchObject({ 'reading.theme': 'dark', 'reading.code.wrap': true, 'reading.prose.size': 20, 'reading.prose.lineHeight': 1.65, 'reading.customFont': '' });
        expect(Object.keys(values)).toHaveLength(16);
        expect(JSON.parse(localStorage.getItem(READING_KEY)!)).toEqual({ version: 1 });
        expect(reading.preferences().theme).toBe('dark');
        expect(reading.profile('code').wrap).toBe(true);
        expect(reading.changed()).toBe(false);
    });

    it('offers to save them only to accounts that keep preferences', async () => {
        await setUp(limitedConfig([Rights.browse, Rights.viewFiles]));
        expect((await panel()).querySelector('[data-save-defaults]')).toBeNull();

        TestBed.resetTestingModule();
        await setUp(fakeConfig({ user: null }));
        expect((await panel()).querySelector('[data-save-defaults]')).toBeNull();
    });
});
