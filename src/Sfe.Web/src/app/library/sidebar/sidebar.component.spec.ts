import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TagSidebar } from '../../core/models';
import { TagManagerDialog } from '../../tags/tag-manager.dialog';
import { ApiMock, fakeConfig, loadConfig, mockApi, settle, setMediaQuery, summary, tag, testProviders } from '../../../testing/fakes';
import { ChangeFeed } from '../change-feed.service';
import { Dialogs } from '../dialogs.service';
import { ListingStore } from '../listing.store';
import { SidebarComponent } from './sidebar.component';
import { SidebarService } from './sidebar.service';

describe('SidebarComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<SidebarComponent>;
    let element: HTMLElement;
    let navigate: ReturnType<typeof vi.spyOn>;

    const sun = summary(tag('Sun'));
    const scifi = summary(tag('Science "Fiction"'));
    const data = (overrides: Partial<TagSidebar> = {}): TagSidebar => ({
        popular: [{ tag: sun, fileCount: 1234 }, { tag: scifi, fileCount: 1 }],
        searched: [],
        viewed: [{ tag: sun, fileCount: 1234 }],
        lucky: [],
        historyEnabled: true,
        ...overrides,
    });
    const section = (title: string) => [...element.querySelectorAll<HTMLElement>('.sidebar-section')].find(s => s.querySelector('h2')!.textContent!.trim() === title)!;
    const fold = (title: string) => section(title).querySelector<HTMLButtonElement>('.sidebar-fold')!;
    const folded = (title: string) => section(title).querySelector<HTMLElement>('.sidebar-tags')!.hidden;
    const names = (title: string) => [...section(title).querySelectorAll('.tag-name')].map(n => n.textContent!.trim());

    async function create(answer: TagSidebar = data()): Promise<void> {
        api.tagSidebar.mockResolvedValue(answer);
        fixture = TestBed.createComponent(SidebarComponent);
        element = fixture.nativeElement;
        await settle();
    }

    beforeEach(async () => {
        localStorage.clear();
        setMediaQuery('(max-width: 760px)', false);
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        navigate = vi.spyOn(TestBed.inject(ListingStore), 'navigate').mockImplementation(() => { });
    });

    afterEach(() => vi.restoreAllMocks());

    it('lists the four sections with their counts', async () => {
        await create();

        expect(names('Most popular')).toEqual(['Sun', 'Science "Fiction"']);
        expect(section('Most popular').querySelector('.tag-count')!.textContent).toBe((1234).toLocaleString());
        expect(section('Most popular').querySelector('.tag-count')!.getAttribute('title')).toBe(`${(1234).toLocaleString()} files`);
        expect(names('Last viewed')).toEqual(['Sun']);
    });

    it('explains the empty sections once loaded, not before', async () => {
        api.tagSidebar.mockReturnValue(new Promise(() => { }));
        fixture = TestBed.createComponent(SidebarComponent);
        element = fixture.nativeElement;
        await settle();
        expect(element.querySelector('.sidebar-empty')).toBeNull();

        await create(data({ popular: [], viewed: [] }));
        expect(section('Most popular').querySelector('.sidebar-empty')!.textContent).toBe('No file is tagged yet.');
        expect(section('Last searched').querySelector('.sidebar-empty')!.textContent).toContain('t:name');
    });

    it('hides the history sections while history is off', async () => {
        await create(data({ historyEnabled: false }));

        expect(section('Last searched').hidden).toBe(true);
        expect(section('Last viewed').hidden).toBe(true);
        expect(section('Most popular').hidden).toBe(false);
        expect(section('I feel lucky').hidden).toBe(false);
    });

    it('searches for a tag on click, without the quotes the query cannot hold', async () => {
        await create();

        section('Most popular').querySelectorAll<HTMLButtonElement>('button.tag-name')[1].click();

        expect(navigate).toHaveBeenCalledWith({ q: 't:"Science Fiction"' });
    });

    it('folds Last searched and I feel lucky until opened, and remembers it in this browser', async () => {
        await create();

        expect(folded('Most popular')).toBe(false);
        expect(folded('Last viewed')).toBe(false);
        expect(folded('Last searched')).toBe(true);
        expect(folded('I feel lucky')).toBe(true);
        expect(fold('I feel lucky').getAttribute('aria-expanded')).toBe('false');
        expect(section('I feel lucky').querySelector('button[aria-label="Other random tags"]')).toBeNull();

        fold('I feel lucky').click();
        fold('Most popular').click();
        await settle();
        expect(folded('I feel lucky')).toBe(false);
        expect(fold('I feel lucky').getAttribute('aria-expanded')).toBe('true');
        expect(folded('Most popular')).toBe(true);

        fixture.destroy();
        await create();
        expect(folded('I feel lucky')).toBe(false);
        expect(folded('Most popular')).toBe(true);
        expect(folded('Last searched')).toBe(true);
    });

    it('ignores a broken folding preference', async () => {
        localStorage.setItem('sfe.sidebar.sections', '{nope');
        await create();

        expect(folded('Most popular')).toBe(false);
        expect(folded('I feel lucky')).toBe(true);
    });

    it('draws other random tags on request', async () => {
        await create();
        fold('I feel lucky').click();
        await settle();
        api.tagSidebar.mockResolvedValue(data({ lucky: [{ tag: summary(tag('Random')), fileCount: 0 }] }));

        section('I feel lucky').querySelector<HTMLButtonElement>('button[aria-label="Other random tags"]')!.click();
        await settle();

        expect(names('I feel lucky')).toEqual(['Random']);
    });

    it('opens the tag manager', async () => {
        const manager = { open: vi.fn() };
        TestBed.inject(Dialogs).tagManager.set(manager as unknown as TagManagerDialog);
        await create();

        element.querySelector<HTMLButtonElement>('.sidebar-manage')!.click();

        expect(manager.open).toHaveBeenCalled();
    });

    it('links to the trash for those who may delete', async () => {
        await create();

        const link = element.querySelector<HTMLAnchorElement>('a.sidebar-trash')!;
        expect(link.textContent!.trim()).toBe('Trash');
        expect(link.getAttribute('href')).toBe('/admin?section=files&page=trash');
    });

    it('has no trash link without the right to delete, and does not count it', async () => {
        await loadConfig(api, fakeConfig({ rights: ['tags.view'] }));
        await create();

        expect(element.querySelector('a.sidebar-trash')).toBeNull();
        expect(api.trashCount).not.toHaveBeenCalled();
    });

    describe('the trash link', () => {
        const link = () => element.querySelector<HTMLAnchorElement>('a.sidebar-trash')!;
        const icon = () => link().querySelector('use')!.getAttribute('href');
        const badge = () => link().querySelector('.sidebar-badge')?.textContent?.trim() ?? null;

        it('shows an empty trash', async () => {
            api.trashCount.mockResolvedValue({ count: 0, more: false });
            await create();

            expect(icon()).toBe('#i-trash');
            expect(badge()).toBeNull();
            expect(link().classList).not.toContain('full');
            expect(link().getAttribute('aria-label')).toBe('Trash: empty');
        });

        it('shows a trash holding items, with their number', async () => {
            api.trashCount.mockResolvedValue({ count: 3, more: false });
            await create();

            expect(icon()).toBe('#i-trash-full');
            expect(badge()).toBe('3');
            expect(link().classList).toContain('full');
            expect(link().getAttribute('aria-label')).toBe('Trash: 3 items');
        });

        it('stops counting at 99', async () => {
            api.trashCount.mockResolvedValue({ count: 99, more: true });
            await create();

            expect(badge()).toBe('99+');
            expect(link().title).toBe('Trash: more than 99 items');
        });

        it('counts again when the change feed says the trash changed', async () => {
            api.trashCount.mockResolvedValue({ count: 0, more: false });
            await create();
            expect(api.trashCount).toHaveBeenCalledTimes(1);

            api.trashCount.mockResolvedValue({ count: 2, more: false });
            const feed = TestBed.inject(ChangeFeed);
            feed.trashChanges.update(n => n + 1);
            await settle();
            feed.trashChanges.update(n => n + 1);
            await settle(350);

            expect(api.trashCount).toHaveBeenCalledTimes(2);
            expect(badge()).toBe('2');
        });
    });

    describe('as a drawer on phones', () => {
        beforeEach(() => setMediaQuery('(max-width: 760px)', true));

        it('closes after a pick, on Escape, and on a tap next to it', async () => {
            const sidebar = TestBed.inject(SidebarService);
            await create();

            sidebar.setOpen(true);
            section('Most popular').querySelector<HTMLButtonElement>('button.tag-name')!.click();
            expect(sidebar.open()).toBe(false);

            sidebar.setOpen(true);
            document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
            expect(sidebar.open()).toBe(false);

            sidebar.setOpen(true);
            element.querySelector('h2')!.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
            expect(sidebar.open()).toBe(true);
            // A press, not a click: iOS sends no click for a tap on what isn't clickable.
            document.body.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
            expect(sidebar.open()).toBe(false);
        });

        it('stays open for a click on its own toggle button', async () => {
            const sidebar = TestBed.inject(SidebarService);
            await create();
            const toggle = document.body.appendChild(document.createElement('button'));
            toggle.className = 'sidebar-toggle';

            sidebar.setOpen(true);
            toggle.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));

            expect(sidebar.open()).toBe(true);
            toggle.remove();
        });
    });

    it('stays open on wide screens whatever is clicked', async () => {
        const sidebar = TestBed.inject(SidebarService);
        await create();

        document.body.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

        expect(sidebar.open()).toBe(true);
    });
});
