import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QuickLink } from '../../core/models';
import { Rights } from '../../core/rights';
import { ToastService } from '../../core/toast.service';
import { ALL_RIGHTS, ApiMock, item, limitedConfig, loadConfig, mockApi, settle, setMediaQuery, testProviders } from '../../../testing/fakes';
import { ListingStore } from '../listing.store';
import { SelectionBarComponent } from '../selection-bar.component';
import { PinButtonComponent } from './pin-button.component';
import { QuickLinksComponent } from './quick-links.component';
import { DRAG_ITEMS, DRAG_LINK } from './quick-links.service';

function link(path: string, overrides: Partial<QuickLink> = {}): QuickLink {
    const name = path.split('/').pop() || 'Root';
    const extension = name.includes('.') ? name.split('.').pop()! : null;
    const kind = overrides.kind ?? (extension === 'jpg' ? 'image' : extension ? 'file' : 'directory');
    return { path, name, kind, extension, exists: true, ...overrides };
}

/** A DataTransfer as far as the drags use it (jsdom has none). */
class FakeDataTransfer {
    private readonly data = new Map<string, string>();
    effectAllowed = 'all';
    dropEffect = 'none';
    get types(): string[] {
        return [...this.data.keys()];
    }
    setData(type: string, value: string): void {
        this.data.set(type, value);
    }
    getData(type: string): string {
        return this.data.get(type) ?? '';
    }
}

function drag(target: Element, type: string, dataTransfer: FakeDataTransfer, clientY = 0): Event {
    const event = new Event(type, { bubbles: true, cancelable: true });
    Object.defineProperty(event, 'dataTransfer', { value: dataTransfer });
    Object.defineProperty(event, 'clientY', { value: clientY });
    target.dispatchEvent(event);
    return event;
}

/** The sidebar section, next to a listing's items as the page has them (`data-path`). */
@Component({
    selector: 'sfe-quick-links-host',
    imports: [QuickLinksComponent, PinButtonComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <aside class="sidebar"><section sfeQuickLinks></section></aside>
        <main class="page">
            <article class="tile item" data-path="Media/photo.jpg"><a class="item-link" href="#">photo</a></article>
            <article class="tile item" data-path="Media/clip.mp4"><a class="item-link" href="#">clip</a></article>
            <div class="folder-item" data-path="Media/Trips"><a class="folder" href="#">Trips</a></div>
            <button sfePin="Media/Trips" name="Trips"></button>
        </main>
    `,
})
class HostComponent {}

describe('Quick links', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<HostComponent>;
    let element: HTMLElement;
    let store: ListingStore;

    const section = () => element.querySelector<HTMLElement>('.quick-links')!;
    const names = () => [...element.querySelectorAll('.quick-link-name')].map(name => name.textContent);
    const rows = () => [...element.querySelectorAll<HTMLElement>('li.quick-link')];

    async function create(links: QuickLink[], rights: string[] = ALL_RIGHTS): Promise<void> {
        api.quickLinks.mockResolvedValue(links);
        await loadConfig(api, limitedConfig(rights));
        fixture = TestBed.createComponent(HostComponent);
        element = fixture.nativeElement;
        await settle();
    }

    beforeEach(() => {
        setMediaQuery('(max-width: 760px)', false);
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        store = TestBed.inject(ListingStore);
        vi.spyOn(TestBed.inject(ToastService), 'show').mockImplementation(() => { });
    });

    afterEach(() => vi.restoreAllMocks());

    it('lists the links in order: folders link to their listing, files to the file', async () => {
        await create([link('Media'), link('Media/photo.jpg'), link('')]);

        expect(names()).toEqual(['Media', 'photo.jpg', 'Root']);
        const targets = [...element.querySelectorAll<HTMLAnchorElement>('.quick-link-target')];
        expect(targets[0].getAttribute('href')).toContain('path=Media');
        expect(targets[1].getAttribute('href')).toContain('Media/photo.jpg');
        expect(rows()[1].dataset['kind']).toBe('image');
    });

    it('explains how to add links while there are none', async () => {
        await create([]);

        expect(section().querySelector('.sidebar-empty')!.textContent).toContain('Drag folders and files here');
        expect(section().querySelector('.quick-links-edit')).toBeNull();
    });

    it('is hidden, and asks nothing, without the right', async () => {
        await create([link('Media')], ALL_RIGHTS.filter(right => right !== Rights.quickLinks));

        expect(section().hidden).toBe(true);
        expect(api.quickLinks).not.toHaveBeenCalled();
        expect(element.querySelector<HTMLButtonElement>('button[sfePin]')!.hidden).toBe(true);
    });

    it('shows a file in its folder on a plain click', async () => {
        const reveal = vi.spyOn(store, 'reveal').mockImplementation(() => { });
        await create([link('Media/photo.jpg')]);

        const click = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 });
        element.querySelector('.quick-link-target')!.dispatchEvent(click);

        expect(click.defaultPrevented).toBe(true);
        expect(reveal).toHaveBeenCalledWith('Media/photo.jpg');
    });

    it('keeps a link whose target is gone, and says so when it is clicked', async () => {
        const reveal = vi.spyOn(store, 'reveal').mockImplementation(() => { });
        await create([link('Media/gone.jpg', { exists: false })]);

        element.querySelector<HTMLElement>('.quick-link-target')!.click();

        expect(rows()[0].classList).toContain('missing');
        expect(reveal).not.toHaveBeenCalled();
        expect(TestBed.inject(ToastService).show).toHaveBeenCalledWith(expect.stringContaining('“gone.jpg” is not there any more'));
    });

    it('removes a link with its × button', async () => {
        await create([link('Media'), link('Docs')]);
        api.removeQuickLink.mockResolvedValue([link('Docs')]);

        element.querySelector<HTMLButtonElement>('button[aria-label="Remove Media from the quick links"]')!.click();
        await settle();

        expect(api.removeQuickLink).toHaveBeenCalledWith('Media');
        expect(names()).toEqual(['Docs']);
    });

    it('moves links up and down in the edit mode (phones have no drag and drop)', async () => {
        await create([link('A'), link('B'), link('C')]);
        api.addQuickLinks.mockResolvedValue([link('B'), link('A'), link('C')]);

        element.querySelector<HTMLButtonElement>('.quick-links-edit')!.click();
        await settle();
        const up = element.querySelector<HTMLButtonElement>('button[aria-label="Move A up"]')!;
        expect(up.disabled).toBe(true);
        expect(element.querySelector<HTMLButtonElement>('button[aria-label="Move C down"]')!.disabled).toBe(true);
        expect(rows()[0].getAttribute('draggable')).toBe('false');

        element.querySelector<HTMLButtonElement>('button[aria-label="Move A down"]')!.click();
        await settle();
        element.querySelector<HTMLButtonElement>('button[aria-label="Move C up"]')!.click();

        expect(api.addQuickLinks).toHaveBeenNthCalledWith(1, ['A'], 2);
        expect(api.addQuickLinks).toHaveBeenNthCalledWith(2, ['C'], 1);
    });

    describe('drag and drop', () => {
        /** Puts the links one under the other, 30 px each from the top. */
        function layOut(): void {
            rows().forEach((row, i) => vi.spyOn(row, 'getBoundingClientRect').mockReturnValue({ top: i * 30, height: 30 } as DOMRect));
        }

        it('adds a listed folder or file where it is dropped', async () => {
            await create([link('A'), link('B')]);
            layOut();
            api.addQuickLinks.mockResolvedValue([link('A'), link('Media/Trips'), link('B')]);
            const data = new FakeDataTransfer();

            drag(element.querySelector('.folder-item .folder')!, 'dragstart', data);
            await settle();
            expect(JSON.parse(data.getData(DRAG_ITEMS))).toEqual(['Media/Trips']);
            expect(section().classList).toContain('drop-ready');

            const over = drag(rows()[1], 'dragover', data, 40);
            await settle();
            expect(over.defaultPrevented).toBe(true);
            expect(rows()[1].classList).toContain('drop-before');

            drag(rows()[1], 'drop', data, 40);
            await settle();

            expect(api.addQuickLinks).toHaveBeenCalledWith(['Media/Trips'], 1);
            expect(names()).toEqual(['A', 'Trips', 'B']);
            expect(section().classList).not.toContain('drop-ready');
        });

        it('carries every selected file when a selected one is dragged', async () => {
            await create([]);
            store.items.set([item('Media/photo.jpg'), item('Media/clip.mp4'), item('Media/other.jpg')]);
            store.select(item('Media/photo.jpg'), true);
            store.select(item('Media/clip.mp4'), true);
            const data = new FakeDataTransfer();

            drag(element.querySelector('[data-path="Media/clip.mp4"] a')!, 'dragstart', data);
            api.addQuickLinks.mockResolvedValue([]);
            drag(section(), 'dragover', data, 500);
            drag(section(), 'drop', data, 500);

            expect(api.addQuickLinks).toHaveBeenCalledWith(['Media/photo.jpg', 'Media/clip.mp4'], 0);
        });

        it('moves a link dragged within the list', async () => {
            await create([link('A'), link('B'), link('C')]);
            layOut();
            api.addQuickLinks.mockResolvedValue([link('B'), link('C'), link('A')]);
            const data = new FakeDataTransfer();

            drag(rows()[0], 'dragstart', data);
            await settle();
            expect(data.getData(DRAG_LINK)).toBe('A');
            expect(rows()[0].classList).toContain('dragged');
            drag(rows()[2], 'dragover', data, 85);
            await settle();
            expect(element.querySelector('.quick-link-drop-end')).not.toBeNull();
            drag(rows()[2], 'drop', data, 85);

            expect(api.addQuickLinks).toHaveBeenCalledWith(['A'], 3);
        });

        it('ignores other drags', async () => {
            await create([link('A')]);
            const data = new FakeDataTransfer();
            data.setData('text/plain', 'hello');

            drag(element.querySelector('.quick-link-target')!.parentElement!.parentElement!, 'dragstart', new FakeDataTransfer());
            const over = drag(section(), 'dragover', data);
            drag(section(), 'drop', data);

            expect(over.defaultPrevented).toBe(false);
            expect(api.addQuickLinks).not.toHaveBeenCalled();
        });
    });

    describe('pin buttons', () => {
        it('add the folder or file, and remove it once it is a link', async () => {
            await create([]);
            const pin = element.querySelector<HTMLButtonElement>('button[sfePin]')!;
            expect(pin.getAttribute('aria-pressed')).toBe('false');
            expect(pin.title).toBe('Add “Trips” to the quick links (or drag it onto the sidebar)');

            api.addQuickLinks.mockResolvedValue([link('Media/Trips')]);
            pin.click();
            await settle();
            expect(api.addQuickLinks).toHaveBeenCalledWith(['Media/Trips'], null);
            expect(pin.classList).toContain('pinned');
            expect(TestBed.inject(ToastService).show).toHaveBeenCalledWith('Added “Trips” to the quick links');

            api.removeQuickLink.mockResolvedValue([]);
            pin.click();
            await settle();
            expect(api.removeQuickLink).toHaveBeenCalledWith('Media/Trips');
            expect(pin.classList).not.toContain('pinned');
        });

        it('of the selection bar add the selected files', async () => {
            await create([]);
            const bar = TestBed.createComponent(SelectionBarComponent);
            store.items.set([item('a.jpg'), item('b.jpg')]);
            store.selectAll();
            await settle();
            api.addQuickLinks.mockResolvedValue([link('a.jpg'), link('b.jpg')]);

            bar.nativeElement.querySelector('.selection-pin').click();
            await settle();

            expect(api.addQuickLinks).toHaveBeenCalledWith(['a.jpg', 'b.jpg'], null);
            expect(TestBed.inject(ToastService).show).toHaveBeenCalledWith('Added 2 items to the quick links');
        });

        it('say what went wrong', async () => {
            await create([]);
            const error = vi.spyOn(TestBed.inject(ToastService), 'error').mockImplementation(() => { });
            api.addQuickLinks.mockRejectedValue(new Error('Quick links hold at most 100 folders and files: remove some first.'));

            element.querySelector<HTMLButtonElement>('button[sfePin]')!.click();
            await settle();

            expect(error).toHaveBeenCalledWith('Quick links: Quick links hold at most 100 folders and files: remove some first.');
        });
    });
});
