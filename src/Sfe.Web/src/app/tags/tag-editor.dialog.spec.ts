import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ApiError } from '../core/api.service';
import { TagDetails } from '../core/models';
import { ToastService } from '../core/toast.service';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { ApiMock, deferred, loadConfig, mockApi, settle, summary, tag, testProviders, type } from '../../testing/fakes';
import { TagEditorDialog, TagEditResult } from './tag-editor.dialog';

describe('TagEditorDialog', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<TagEditorDialog>;
    let element: HTMLElement;
    let store: ListingStore;

    const dialog = () => element.querySelector('dialog')!;
    const field = <T extends HTMLElement = HTMLInputElement>(name: string) => element.querySelector<T>(`[name="${name}"]`)!;
    const buttons = () => [...element.querySelectorAll<HTMLButtonElement>('.sheet-foot button')];
    const button = (label: string) => buttons().find(b => b.textContent!.trim() === label);
    const error = () => element.querySelector('.form-error')?.textContent?.trim() ?? null;
    const parentChips = () => [...element.querySelectorAll('.tag-list sfe-tag-chip .tag-name')].map(chip => chip.textContent!.trim());
    const submit = async () => {
        element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();
    };

    beforeEach(async () => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        store = TestBed.inject(ListingStore);
        fixture = TestBed.createComponent(TagEditorDialog);
        element = fixture.nativeElement;
        await fixture.whenStable();
    });

    afterEach(() => vi.restoreAllMocks());

    /** Opens the editor; returns the promise of what it did, without waiting for it. */
    async function open(details: TagDetails | null, defaults?: { name?: string }): Promise<{ result: Promise<TagEditResult> }> {
        if (details)
            api.tag.mockResolvedValue(details);
        const result = fixture.componentInstance.open(details?.id ?? null, defaults);
        await settle(10);
        // Wrapped: an async function would wait for the promise it returns.
        return { result };
    }

    describe('a new tag', () => {
        it('opens empty, named after the search if any, with the name focused', async () => {
            await open(null, { name: 'Shrek' });

            expect(dialog().open).toBe(true);
            expect(element.querySelector('h2')!.textContent).toBe('New tag');
            expect(element.querySelector('.sheet-subtitle')).toBeNull();
            expect(field('name').value).toBe('Shrek');
            expect(field('shorthand').value).toBe('');
            expect(field<HTMLTextAreaElement>('aliases').value).toBe('');
            expect(document.activeElement).toBe(field('name'));
            expect(api.tag).not.toHaveBeenCalled();
        });

        it('has no Delete nor Find files button', async () => {
            await open(null);

            expect(button('Delete')).toBeUndefined();
            expect(button('Find files')).toBeUndefined();
        });

        it('offers no color first, then the configured ones', async () => {
            await open(null);

            const swatches = [...element.querySelectorAll<HTMLInputElement>('input[name=color]')];
            expect(swatches.map(s => s.value)).toEqual(['', 'red', 'yellow', 'blue']);
            expect(swatches.map(s => s.checked)).toEqual([true, false, false, false]);
            expect(element.querySelector<HTMLElement>('.swatch[title=Yellow]')!.style.getPropertyValue('--chip-text')).toBe('#16181d');
        });

        it('creates the tag with cleaned up values, then resolves with it', async () => {
            const created = tag('Cat');
            api.createTag.mockResolvedValue(created);
            const applied = vi.spyOn(store, 'applyTagChanged');
            const { result } = await open(null);

            type(field('name'), '  Cat  ');
            type(field('shorthand'), '   ');
            type(field<HTMLTextAreaElement>('aliases'), ' Kitty \n\n  Feline\n   \nkitty');
            const red = element.querySelector<HTMLInputElement>('input[name=color][value=red]')!;
            red.checked = true;
            red.dispatchEvent(new Event('change'));
            field('isCategory').click();
            field('isHidden').click();
            await submit();

            expect(api.createTag).toHaveBeenCalledWith({
                name: 'Cat',
                shorthand: null,
                // Duplicates are the server's to drop (case-insensitively).
                aliases: ['Kitty', 'Feline', 'kitty'],
                parentIds: [],
                disambiguationId: null,
                color: 'red',
                isCategory: true,
                isHidden: true,
            });
            expect(api.updateTag).not.toHaveBeenCalled();
            expect(applied).toHaveBeenCalledWith(created);
            expect(dialog().open).toBe(false);
            expect(await result).toBe(created);
        });

        it('refuses a blank name without asking the server', async () => {
            await open(null);
            type(field('name'), ' \t ');
            await submit();

            expect(error()).toBe('A tag needs a name.');
            expect(api.createTag).not.toHaveBeenCalled();
            expect(dialog().open).toBe(true);
            expect(document.activeElement).toBe(field('name'));
        });

        it('shows the server\'s refusal and stays open to fix it', async () => {
            api.createTag.mockRejectedValue(new ApiError(400, 'Unknown color \'plaid\'.', { color: ['Unknown color \'plaid\'.'] }));
            await open(null, { name: 'Cat' });
            await submit();

            expect(error()).toBe('Unknown color \'plaid\'.');
            expect(dialog().open).toBe(true);
            expect(button('Save')!.disabled).toBe(false);

            // Fixed, the error goes away with the dialog.
            api.createTag.mockResolvedValue(tag('Cat'));
            await submit();
            expect(dialog().open).toBe(false);
        });

        it('disables Save while saving', async () => {
            const pending = deferred<TagDetails>();
            api.createTag.mockReturnValue(pending.promise);
            await open(null, { name: 'Cat' });

            void submit();
            await settle();
            expect(button('Save')!.disabled).toBe(true);

            pending.resolve(tag('Cat'));
            await settle();
            expect(button('Save')!.disabled).toBe(false);
        });
    });

    describe('an existing tag', () => {
        const animal = tag('Animal', { shorthand: 'Ani' });
        const pet = tag('Pet');
        const cat = () => tag('Cat', {
            shorthand: 'C',
            aliases: ['Feline', 'Kitty'],
            parents: [summary(animal), summary(pet)],
            disambiguationId: animal.id,
            displayName: 'Cat (Ani)',
            color: 'blue',
            isCategory: true,
            fileCount: 3,
        });

        it('loads the tag and fills the form with it', async () => {
            const details = cat();
            await open(details);

            expect(api.tag).toHaveBeenCalledWith(details.id);
            expect(element.querySelector('h2')!.textContent).toBe('Edit tag');
            expect(element.querySelector('.sheet-subtitle')!.textContent).toBe('3 files');
            expect(field('name').value).toBe('Cat');
            expect(field('shorthand').value).toBe('C');
            expect(field<HTMLTextAreaElement>('aliases').value).toBe('Feline\nKitty');
            expect(parentChips()).toEqual(['Animal', 'Pet']);
            const select = field<HTMLSelectElement>('disambiguation');
            expect(select.disabled).toBe(false);
            expect([...select.options].map(o => o.textContent!.trim())).toEqual(['None', 'Animal', 'Pet']);
            expect(select.value).toBe(String(animal.id));
            expect(element.querySelector<HTMLInputElement>('input[name=color]:checked')!.value).toBe('blue');
            expect(field('isCategory').checked).toBe(true);
            expect(field('isHidden').checked).toBe(false);
        });

        it('saves the changes over the tag', async () => {
            const details = cat();
            const saved = { ...details, name: 'Kitty cat' };
            api.updateTag.mockResolvedValue(saved);
            const { result } = await open(details);

            type(field('name'), 'Kitty cat');
            await submit();

            expect(api.updateTag).toHaveBeenCalledWith(details.id, {
                name: 'Kitty cat',
                shorthand: 'C',
                aliases: ['Feline', 'Kitty'],
                parentIds: [animal.id, pet.id],
                disambiguationId: animal.id,
                color: 'blue',
                isCategory: true,
                isHidden: false,
            });
            expect(api.createTag).not.toHaveBeenCalled();
            expect(await result).toBe(saved);
        });

        it('can switch the disambiguation, or drop it', async () => {
            api.updateTag.mockImplementation(async (_, input) => tag(input.name));
            await open(cat());
            const select = field<HTMLSelectElement>('disambiguation');

            select.value = String(pet.id);
            select.dispatchEvent(new Event('change'));
            await submit();
            expect(api.updateTag.mock.lastCall![1].disambiguationId).toBe(pet.id);

            await open(cat());
            select.value = '';
            select.dispatchEvent(new Event('change'));
            await submit();
            expect(api.updateTag.mock.lastCall![1].disambiguationId).toBeNull();
        });

        it('drops the disambiguation with its parent, but not with another one', async () => {
            api.updateTag.mockImplementation(async (_, input) => tag(input.name));
            await open(cat());

            element.querySelector<HTMLButtonElement>('[aria-label="Remove Pet"]')!.click();
            await settle();
            expect(parentChips()).toEqual(['Animal']);
            expect(field<HTMLSelectElement>('disambiguation').value).toBe(String(animal.id));

            element.querySelector<HTMLButtonElement>('[aria-label="Remove Animal"]')!.click();
            await settle();
            expect(parentChips()).toEqual([]);
            expect(field<HTMLSelectElement>('disambiguation').disabled).toBe(true);
            await submit();

            expect(api.updateTag.mock.lastCall![1]).toMatchObject({ parentIds: [], disambiguationId: null });
        });

        it('adds parents from the picker, never offering the tag itself or its parents', async () => {
            const details = cat();
            const plant = tag('Plant');
            await open(details);
            api.tags.mockResolvedValue({ matches: [details, animal, plant], descendants: [pet] });

            const picker = element.querySelector<HTMLInputElement>('sfe-tag-picker input')!;
            picker.focus();
            await settle();
            const offered = [...element.querySelectorAll('.tag-options li')].map(li => li.querySelector('.tag-name')!.textContent!.trim());
            expect(offered).toEqual(['Plant']);
            // The picker has no "create" entry here.
            expect(element.querySelector('.tag-options li.create')).toBeNull();

            element.querySelector<HTMLElement>('.tag-options li')!.click();
            await settle();
            expect(parentChips()).toEqual(['Animal', 'Pet', 'Plant']);
            expect([...field<HTMLSelectElement>('disambiguation').options].map(o => o.textContent!.trim())).toContain('Plant');
        });

        it('says when a tag is built in, and cannot delete it', async () => {
            await open(tag('Favorite', { isBuiltIn: true, fileCount: 1 }));

            expect(element.querySelector('.sheet-subtitle')!.textContent).toBe('Built-in tag · 1 file');
            expect(button('Delete')).toBeUndefined();
            expect(button('Find files')).toBeDefined();
        });

        it('reports a tag that could not be loaded, without opening', async () => {
            const errors = vi.spyOn(TestBed.inject(ToastService), 'error');
            api.tag.mockRejectedValue(new ApiError(404, 'Not Found'));

            const result = await fixture.componentInstance.open(999);

            expect(result).toBeNull();
            expect(dialog().open).toBe(false);
            expect(errors).toHaveBeenCalledWith('Could not load the tag: Not Found');
        });

        it('does not keep the values of the tag edited before', async () => {
            await open(cat());
            await open(null);

            expect(field('name').value).toBe('');
            expect(field('shorthand').value).toBe('');
            expect(field<HTMLTextAreaElement>('aliases').value).toBe('');
            expect(parentChips()).toEqual([]);
            expect(element.querySelector<HTMLInputElement>('input[name=color]:checked')!.value).toBe('');
            expect(field('isCategory').checked).toBe(false);
            expect(element.querySelector('h2')!.textContent).toBe('New tag');
        });

        it('does not keep a server error from the tag edited before', async () => {
            api.updateTag.mockRejectedValue(new ApiError(400, 'Nope'));
            await open(cat());
            await submit();
            expect(error()).toBe('Nope');

            await open(cat());
            expect(error()).toBeNull();
        });

        it('resolves with null when cancelled, without saving', async () => {
            const { result } = await open(cat());

            button('Cancel')!.click();

            expect(await result).toBeNull();
            expect(api.updateTag).not.toHaveBeenCalled();
        });

        it('resolves with null when closed with Esc (the dialog\'s own close)', async () => {
            const { result } = await open(cat());

            dialog().close();

            expect(await result).toBeNull();
        });

        it('deletes the tag after asking, naming the files it is removed from', async () => {
            const details = cat();
            const ask = vi.spyOn(window, 'confirm').mockReturnValue(true);
            const deleted = vi.spyOn(store, 'applyTagDeleted');
            api.deleteTag.mockResolvedValue(undefined);
            const { result } = await open(details);

            button('Delete')!.click();
            await settle();

            expect(ask).toHaveBeenCalledWith('Delete the tag “Cat (Ani)”? It is removed from 3 files.');
            expect(api.deleteTag).toHaveBeenCalledWith(details.id);
            expect(deleted).toHaveBeenCalledWith(details.id);
            expect(await result).toEqual({ deleted: details.id });
        });

        it('asks without mentioning files when the tag is on none', async () => {
            const ask = vi.spyOn(window, 'confirm').mockReturnValue(false);
            await open(tag('Unused'));

            button('Delete')!.click();
            await settle();

            expect(ask).toHaveBeenCalledWith('Delete the tag “Unused”?');
            expect(api.deleteTag).not.toHaveBeenCalled();
            expect(dialog().open).toBe(true);
        });

        it('stays open with the error when deleting fails', async () => {
            vi.spyOn(window, 'confirm').mockReturnValue(true);
            const deleted = vi.spyOn(store, 'applyTagDeleted');
            api.deleteTag.mockRejectedValue(new ApiError(409, 'Built-in tags cannot be deleted.'));
            await open(cat());

            button('Delete')!.click();
            await settle();

            expect(error()).toBe('Built-in tags cannot be deleted.');
            expect(deleted).not.toHaveBeenCalled();
            expect(dialog().open).toBe(true);
        });

        it('finds the files with the tag: closes the dialogs and searches for it', async () => {
            const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
            const closeAll = vi.spyOn(TestBed.inject(Dialogs), 'closeAll');
            await open(tag('Science "Fiction"'));

            button('Find files')!.click();

            expect(closeAll).toHaveBeenCalled();
            expect(navigate).toHaveBeenCalledWith(['/'], { queryParams: { path: null, q: 't:"Science Fiction"', sort: null, desc: null }, replaceUrl: false });
        });
    });
});
