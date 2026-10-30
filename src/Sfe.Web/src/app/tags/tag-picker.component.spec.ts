import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiError } from '../core/api.service';
import { TagSearchResults } from '../core/models';
import { ToastService } from '../core/toast.service';
import { ApiMock, deferred, loadConfig, mockApi, settle, tag, testProviders, type } from '../../testing/fakes';
import { TagOption, TagPickerComponent } from './tag-picker.component';

describe('TagPickerComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<TagPickerComponent>;
    let element: HTMLElement;
    let pick: ReturnType<typeof vi.fn<(option: TagOption) => Promise<void>>>;
    let errors: ReturnType<typeof vi.spyOn>;

    const cat = tag('Cat', { aliases: ['Kitty'] });
    const cactus = tag('Cactus');
    const lion = tag('Lion');
    const results = (matches = [cat, cactus], descendants = [lion]): TagSearchResults => ({ matches, descendants });

    const input = () => element.querySelector<HTMLInputElement>('input')!;
    const options = () => [...element.querySelectorAll<HTMLLIElement>('.tag-options li')];
    const labels = () => options().map(li => li.classList.contains('create') ? `+${li.querySelector('strong')!.textContent}` : li.querySelector('.tag-name')!.textContent!.trim());
    const active = () => options().findIndex(li => li.getAttribute('aria-selected') === 'true');
    const key = async (name: string) => {
        const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
        input().dispatchEvent(event);
        await settle();
        return event;
    };

    beforeEach(async () => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        errors = vi.spyOn(TestBed.inject(ToastService), 'error');
        fixture = TestBed.createComponent(TagPickerComponent);
        element = fixture.nativeElement;
        pick = vi.fn(() => Promise.resolve());
        fixture.componentRef.setInput('pick', pick);
        fixture.componentRef.setInput('placeholder', 'Add a tag…');
        api.tags.mockResolvedValue(results());
        await fixture.whenStable();
    });

    afterEach(() => vi.restoreAllMocks());

    async function focusAndType(text: string): Promise<void> {
        input().focus();
        type(input(), text);
        await settle(200);
    }

    it('suggests every tag when focused, the matches then the tags inheriting from them', async () => {
        input().focus();
        await settle();

        expect(api.tags).toHaveBeenCalledWith({ q: '', limit: 40 });
        expect(labels()).toEqual(['Cat', 'Cactus', 'Lion']);
        expect(active()).toBe(0);
        expect(input().getAttribute('aria-expanded')).toBe('true');
        expect(input().getAttribute('aria-activedescendant')).toBe(options()[0].id);
        expect(input().placeholder).toBe('Add a tag…');
    });

    it('shows the aliases next to a suggestion', async () => {
        input().focus();
        await settle();

        expect(options()[0].querySelector('.tag-option-detail')!.textContent).toBe('Kitty');
        expect(options()[1].querySelector('.tag-option-detail')).toBeNull();
    });

    it('searches with the typed text trimmed, once typing pauses', async () => {
        await focusAndType('  ca ');

        expect(api.tags).toHaveBeenLastCalledWith({ q: 'ca', limit: 40 });
    });

    it('leaves out the excluded tags', async () => {
        fixture.componentRef.setInput('exclude', () => new Set([cat.id, lion.id]));
        input().focus();
        await settle();

        expect(labels()).toEqual(['Cactus']);
    });

    it('offers to create a tag only when allowed, there is text, and no tag has that name', async () => {
        await focusAndType('Ca');
        expect(labels()).toEqual(['Cat', 'Cactus', 'Lion']);

        fixture.componentRef.setInput('allowCreate', true);
        await focusAndType('  Ca  ');
        expect(labels()).toEqual(['Cat', 'Cactus', 'Lion', '+“Ca”']);

        // Same name in another case: no duplicate.
        await focusAndType('cAT');
        expect(labels()).toEqual(['Cat', 'Cactus', 'Lion']);

        await focusAndType('   ');
        expect(labels()).not.toContain('+“”');
    });

    it('does not offer to create a tag whose name is excluded (e.g. already on the file)', async () => {
        fixture.componentRef.setInput('allowCreate', true);
        fixture.componentRef.setInput('exclude', () => new Set([cat.id]));
        api.tags.mockResolvedValue(results([cat], []));

        await focusAndType('cat');

        expect(options()).toHaveLength(0);
        expect(input().getAttribute('aria-expanded')).toBe('false');
    });

    it('offers to create a tag named like another one\'s alias (aliases are not names)', async () => {
        fixture.componentRef.setInput('allowCreate', true);
        api.tags.mockResolvedValue(results([cat], []));

        await focusAndType('Kitty');

        expect(labels()).toEqual(['Cat', '+“Kitty”']);
    });

    it('offers only the creation when nothing matches', async () => {
        fixture.componentRef.setInput('allowCreate', true);
        api.tags.mockResolvedValue(results([], []));

        await focusAndType('Brand new');

        expect(labels()).toEqual(['+“Brand new”']);
        expect(active()).toBe(0);
    });

    it('moves through the suggestions with the arrow keys, wrapping around', async () => {
        input().focus();
        await settle();

        await key('ArrowDown');
        expect(active()).toBe(1);
        await key('ArrowDown');
        await key('ArrowDown');
        expect(active()).toBe(0);
        await key('ArrowUp');
        expect(active()).toBe(2);
        expect(input().getAttribute('aria-activedescendant')).toBe(options()[2].id);
    });

    it('picks the active suggestion with Enter, then empties the box and suggests again', async () => {
        await focusAndType('l');
        await key('ArrowUp');
        api.tags.mockClear();

        const event = await key('Enter');
        await settle();

        expect(event.defaultPrevented).toBe(true);
        expect(pick).toHaveBeenCalledWith({ tag: lion });
        expect(input().value).toBe('');
        expect(api.tags).toHaveBeenCalledWith({ q: '', limit: 40 });
    });

    it('picks the creation with the typed text', async () => {
        fixture.componentRef.setInput('allowCreate', true);
        api.tags.mockResolvedValue(results([], []));
        await focusAndType(' New one ');

        await key('Enter');

        expect(pick).toHaveBeenCalledWith({ create: 'New one' });
    });

    it('picks a suggestion on click', async () => {
        input().focus();
        await settle();

        options()[1].click();
        await settle();

        expect(pick).toHaveBeenCalledWith({ tag: cactus });
    });

    it('keeps the focus in the box when a suggestion is pressed', async () => {
        input().focus();
        await settle();

        // On mousedown, not pointerdown: a pointerdown prevented on a touch screen cancels the tap in WebKit (no click,
        // the suggestion not taken on an iPhone).
        options()[0].dispatchEvent(new Event('pointerdown', { bubbles: true, cancelable: true }));
        const press = new Event('mousedown', { bubbles: true, cancelable: true });
        options()[0].dispatchEvent(press);
        options()[0].dispatchEvent(new Event('pointerup', { bubbles: true }));

        expect(press.defaultPrevented).toBe(true);
    });

    it('ignores Enter without suggestions', async () => {
        api.tags.mockResolvedValue(results([], []));
        await focusAndType('zzz');

        await key('Enter');

        expect(pick).not.toHaveBeenCalled();
    });

    it('asks for suggestions on an arrow key when there are none shown', async () => {
        input().focus();
        await settle();
        await key('Escape');
        expect(options()).toHaveLength(0);
        api.tags.mockClear();

        await key('ArrowDown');

        expect(api.tags).toHaveBeenCalledTimes(1);
        expect(labels()).toEqual(['Cat', 'Cactus', 'Lion']);
    });

    it('closes the suggestions with Escape without closing the dialog around it', async () => {
        const reachedDialog = vi.fn();
        element.addEventListener('keydown', reachedDialog);
        input().focus();
        await settle();

        const event = await key('Escape');

        expect(options()).toHaveLength(0);
        expect(event.defaultPrevented).toBe(true);
        expect(reachedDialog).not.toHaveBeenCalled();

        // A second Escape, with nothing to close, is the dialog's.
        const second = await key('Escape');
        expect(second.defaultPrevented).toBe(false);
        expect(reachedDialog).toHaveBeenCalledTimes(1);
    });

    it('closes the suggestions when the focus leaves', async () => {
        input().focus();
        await settle();

        input().blur();
        await settle();

        expect(options()).toHaveLength(0);
    });

    it('waits for the end of a press elsewhere before closing, so its click still lands', async () => {
        input().focus();
        await settle();

        window.dispatchEvent(new Event('pointerdown'));
        input().blur();
        await settle();
        expect(options()).toHaveLength(3);

        window.dispatchEvent(new Event('pointerup'));
        await settle();
        expect(options()).toHaveLength(0);
    });

    it('stays open when the press gave the focus back to the box', async () => {
        input().focus();
        await settle();

        window.dispatchEvent(new Event('pointerdown'));
        input().blur();
        input().focus();
        window.dispatchEvent(new Event('pointerup'));
        await settle();

        expect(options()).toHaveLength(3);
    });

    it('drops suggestions arriving after the focus left', async () => {
        const late = deferred<TagSearchResults>();
        api.tags.mockReturnValue(late.promise);
        input().focus();
        input().blur();

        late.resolve(results());
        await settle();

        expect(options()).toHaveLength(0);
    });

    it('keeps the suggestions of the last text typed when an earlier search answers late', async () => {
        const slow = deferred<TagSearchResults>();
        api.tags.mockReturnValueOnce(slow.promise).mockResolvedValueOnce(results([lion], []));
        input().focus();
        await settle();
        await focusAndType('li');

        slow.resolve(results());
        await settle();

        expect(labels()).toEqual(['Lion']);
    });

    it('reports a failed search', async () => {
        api.tags.mockRejectedValue(new ApiError(500, 'Boom'));
        input().focus();
        await settle();

        expect(errors).toHaveBeenCalledWith('Could not load tags: Boom');
        expect(options()).toHaveLength(0);
    });

    it('reports a refused pick and keeps the text to try again', async () => {
        pick.mockRejectedValue(new ApiError(400, 'Tag names are limited to 200 characters.'));
        fixture.componentRef.setInput('allowCreate', true);
        api.tags.mockResolvedValue(results([], []));
        await focusAndType('x'.repeat(201));

        await key('Enter');
        await settle();

        expect(errors).toHaveBeenCalledWith('Tag names are limited to 200 characters.');
        expect(input().value).toBe('x'.repeat(201));
    });

    it('picks once while a pick is still running', async () => {
        const running = deferred<void>();
        pick.mockReturnValue(running.promise);
        input().focus();
        await settle();

        await key('Enter');
        await key('Enter');
        options()[1].click();
        running.resolve();
        await settle();

        expect(pick).toHaveBeenCalledTimes(1);
    });

    it('gives each picker its own list ids', async () => {
        const other = TestBed.createComponent(TagPickerComponent);
        other.componentRef.setInput('pick', pick);
        await other.whenStable();

        const mine = input().getAttribute('aria-controls');
        const theirs = other.nativeElement.querySelector('input').getAttribute('aria-controls');
        expect(mine).toBeTruthy();
        expect(theirs).not.toBe(mine);
        expect(element.querySelector('ul')!.id).toBe(mine);
    });
});
