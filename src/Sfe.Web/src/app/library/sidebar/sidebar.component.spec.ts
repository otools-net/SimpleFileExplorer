import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TagSidebar } from '../../core/models';
import { TagManagerDialog } from '../../tags/tag-manager.dialog';
import { ApiMock, loadConfig, mockApi, settle, setMediaQuery, summary, tag, testProviders } from '../../../testing/fakes';
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
    const section = (title: string) => [...element.querySelectorAll<HTMLElement>('.sidebar-section')].find(s => s.querySelector('h2')!.textContent === title)!;
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

    it('draws other random tags on request', async () => {
        await create();
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
            element.querySelector('h2')!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
            expect(sidebar.open()).toBe(true);
            document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }));
            expect(sidebar.open()).toBe(false);
        });

        it('stays open for a click on its own toggle button', async () => {
            const sidebar = TestBed.inject(SidebarService);
            await create();
            const toggle = document.body.appendChild(document.createElement('button'));
            toggle.className = 'sidebar-toggle';

            sidebar.setOpen(true);
            toggle.dispatchEvent(new MouseEvent('click', { bubbles: true }));

            expect(sidebar.open()).toBe(true);
            toggle.remove();
        });
    });

    it('stays open on wide screens whatever is clicked', async () => {
        const sidebar = TestBed.inject(SidebarService);
        await create();

        document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }));
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

        expect(sidebar.open()).toBe(true);
    });
});
