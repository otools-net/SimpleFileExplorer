import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DirectoryListing, FolderDefaults, InheritedDefaultTag, LibraryItem, TagSummary } from '../core/models';
import { Rights } from '../core/rights';
import { FolderItemComponent } from '../library/items/folder-item.component';
import { ListingStore } from '../library/listing.store';
import { LocationComponent } from '../library/location/location.component';
import { SidebarService } from '../library/sidebar/sidebar.service';
import { ApiMock, fakeConfig, item, limitedConfig, loadConfig, mockApi, settle, summary, tag, testProviders } from '../../testing/fakes';
import { DefaultTagsBadgeComponent } from './default-tags-badge.component';
import { ScopeTagsDialog } from './scope-tags.dialog';

let api: ApiMock;
let store: ListingStore;

const family = summary(tag('Family'));
const beach = summary(tag('Beach'));
const sun = summary(tag('Sunset'));
const night = summary(tag('Night'));

async function setUp(config = fakeConfig()): Promise<void> {
    localStorage.clear();
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, config);
    store = TestBed.inject(ListingStore);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
    api.siblings.mockResolvedValue({ path: '', previous: null, next: null, position: 0, count: 0 });
}

async function show(path: string, items: LibraryItem[], defaultTags: FolderDefaults | null = null): Promise<void> {
    const page: DirectoryListing = { path, name: path.split('/').pop() ?? '', parentPath: '', items, offset: 0, limit: 100, total: items.length, directoryCount: items.length, fileCount: 0, defaultTags };
    store.route.set({ path, q: '', sort: '', desc: false });
    api.browse.mockResolvedValue(page);
    await store.load();
}

const chips = (root: ParentNode) => [...root.querySelectorAll('.default-tags-popover .tag-name')].map(chip => chip.textContent!.trim());
const pointer = (target: Element, type: string, pointerType: string) =>
    target.dispatchEvent(Object.assign(new Event(type), { pointerType }));

afterEach(() => vi.restoreAllMocks());

describe('DefaultTagsBadgeComponent', () => {
    let fixture: ComponentFixture<DefaultTagsBadgeComponent>;
    let element: HTMLElement;
    const button = () => element.querySelector<HTMLButtonElement>('.default-tags-button')!;
    const panel = () => element.querySelector<HTMLElement>('.default-tags-popover');

    async function make(own: TagSummary[], inherited: InheritedDefaultTag[] = [], editable = false): Promise<void> {
        await setUp();
        fixture = TestBed.createComponent(DefaultTagsBadgeComponent);
        fixture.componentRef.setInput('own', own);
        fixture.componentRef.setInput('inherited', inherited);
        fixture.componentRef.setInput('folder', '“Family”');
        fixture.componentRef.setInput('editable', editable);
        element = fixture.nativeElement;
        document.body.appendChild(element);
        await fixture.whenStable();
    }

    afterEach(() => element?.remove());

    it('is an icon counting the tags, naming them all for screen readers, whatever their number', async () => {
        await make([family, beach], [{ path: 'Photos', tag: sun }]);

        expect(button().textContent!.trim()).toBe('3');
        expect(button().getAttribute('aria-label')).toBe('Default tags: Family, Beach, Sunset');
        expect(panel()).toBeNull();
    });

    it('lists every tag in its panel on a tap, the inherited ones under their folder', async () => {
        await make([family, beach], [{ path: 'Photos', tag: sun }, { path: '', tag: night }]);

        button().click();
        await settle();

        expect(button().getAttribute('aria-expanded')).toBe('true');
        expect(chips(element)).toEqual(['Family', 'Beach', 'Sunset', 'Night']);
        expect([...panel()!.querySelectorAll('.default-tags-from')].map(p => p.textContent!.trim())).toEqual(['From “Photos”', 'From the top folder']);
        expect(panel()!.textContent).toContain('Files put into “Family” or its sub-folders get them.');

        button().click();
        await settle();
        expect(panel()).toBeNull();
    });

    it('never opens on hover: only a click or a tap opens it', async () => {
        await make([family]);

        pointer(element, 'pointerenter', 'mouse');
        pointer(button(), 'pointerover', 'mouse');
        await settle(400);

        expect(panel()).toBeNull();
    });

    it('stays open when the pointer leaves, until Esc or a click elsewhere', async () => {
        await make([family]);

        button().click();
        await settle();
        pointer(element, 'pointerleave', 'mouse');
        await settle(300);
        expect(panel()).not.toBeNull();

        panel()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        await settle();
        expect(panel()).toBeNull();
        expect(document.activeElement).toBe(button());

        button().click();
        await settle();
        document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }));
        await settle();
        expect(panel()).toBeNull();
    });

    it('is a phone sheet over a backdrop that closes it, in the top layer', async () => {
        await make([family]);
        const show = vi.fn();
        // jsdom has no popover API: the panel's layer is shown in the top layer where the browser has it.
        Object.defineProperty(HTMLElement.prototype, 'showPopover', { configurable: true, value: show });
        try {
            button().click();
            await settle();

            expect(panel()!.classList).toContain('phone-sheet');
            expect(show).toHaveBeenCalledTimes(1);
            expect(show.mock.contexts[0]).toBe(element.querySelector('.default-tags-layer'));
            element.querySelector<HTMLElement>('.sheet-backdrop')!.click();
            await settle();
            expect(panel()).toBeNull();
        } finally {
            delete (HTMLElement.prototype as Partial<HTMLElement>).showPopover;
        }
    });

    it('offers to change them when editable', async () => {
        await make([family], [], true);
        const edit = vi.fn();
        fixture.componentInstance.edit.subscribe(edit);

        button().click();
        await settle();
        panel()!.querySelector<HTMLButtonElement>('.default-tags-edit')!.click();
        await settle();

        expect(edit).toHaveBeenCalled();
        expect(panel()).toBeNull();
    });

    it('has nothing to change otherwise', async () => {
        await make([family]);

        button().click();
        await settle();

        expect(panel()!.querySelector('.default-tags-edit')).toBeNull();
    });
});

describe('Default tags in the listing', () => {
    beforeEach(async () => await setUp());

    it('show an icon next to the count of cards, and among the buttons of lines, that have some', async () => {
        const make = async (value: LibraryItem, cards: boolean) => {
            const fixture = TestBed.createComponent(FolderItemComponent);
            fixture.componentRef.setInput('item', value);
            fixture.componentRef.setInput('cards', cards);
            await fixture.whenStable();
            return fixture.nativeElement as HTMLElement;
        };
        const card = await make(item('Family', { defaultTags: [family], indexed: true, indexedItems: 4 }), true);
        const line = await make(item('Beach', { defaultTags: [beach, sun, night] }), false);
        const plain = await make(item('Films'), false);

        // On a card, next to its count over the pictures; on a line, among its buttons (after the count).
        expect(card.querySelector('.folder-cover-marks .default-tags-badge + .folder-count')!.textContent!.trim()).toBe('4');
        expect(card.querySelector('.folder-cover-marks .default-tags-button')!.getAttribute('aria-label')).toBe('Default tags: Family');
        expect(card.querySelector('.folder-actions .default-tags-badge')).toBeNull();
        expect(line.querySelector('.folder-actions .default-tags-button')!.textContent!.trim()).toBe('3');
        expect(card.querySelector('.folder .default-tags-badge')).toBeNull();
        expect(plain.querySelector('.default-tags-badge')).toBeNull();
    });

    it('show those of the folder browsed in the location bar, whose panel opens its tags for those who may change them', async () => {
        const dialog = TestBed.createComponent(ScopeTagsDialog).componentInstance;
        const open = vi.spyOn(dialog, 'open').mockImplementation(() => { });
        await show('Photos/Family', [], { path: 'Photos/Family', own: [family], inherited: [{ path: 'Photos', tag: beach }] });
        const fixture = TestBed.createComponent(LocationComponent);
        await settle();
        const element = fixture.nativeElement as HTMLElement;

        element.querySelector<HTMLButtonElement>('.summary .default-tags-button')!.click();
        await settle();
        expect(chips(element)).toEqual(['Family', 'Beach']);
        expect(element.querySelector('.default-tags-popover')!.textContent).toContain('Files put into “Family”');
        element.querySelector<HTMLButtonElement>('.default-tags-edit')!.click();
        expect(open).toHaveBeenCalledWith({ path: 'Photos/Family', recursive: false, query: null });
    });

    it('without Change… for those who may not change them', async () => {
        TestBed.resetTestingModule();
        await setUp(limitedConfig([Rights.browse, Rights.viewTags]));
        await show('Photos', [], { path: 'Photos', own: [family], inherited: [] });
        const fixture = TestBed.createComponent(LocationComponent);
        await settle();
        const element = fixture.nativeElement as HTMLElement;

        element.querySelector<HTMLButtonElement>('.summary .default-tags-button')!.click();
        await settle();
        expect(chips(element)).toEqual(['Family']);
        expect(element.querySelector('.default-tags-edit')).toBeNull();
    });

    it('nothing without default tags', async () => {
        await show('Photos', [], { path: 'Photos', own: [], inherited: [] });
        const fixture = TestBed.createComponent(LocationComponent);
        await settle();

        expect((fixture.nativeElement as HTMLElement).querySelector('.default-tags-badge')).toBeNull();
    });
});
