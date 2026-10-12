import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiError } from '../../core/api.service';
import { TagSearchResults } from '../../core/models';
import { ApiMock, deferred, fakeConfig, loadConfig, mockApi, settle, tag, testProviders } from '../../../testing/fakes';
import { ListingStore } from '../listing.store';
import { SearchBoxState } from '../search-explain/search-explain.component';
import { matchedParts, SearchHistory } from '../search-history.service';
import { SearchBoxComponent } from './search-box.component';

describe('SearchBoxComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<SearchBoxComponent>;
    let element: HTMLElement;
    let store: ListingStore;
    let navigate: ReturnType<typeof vi.spyOn>;

    const shrek = tag('Shrek', { aliases: ['Ogre'] });
    const scifi = tag('Science Fiction');
    const results = (...matches: TagSearchResults['matches']): TagSearchResults => ({ matches, descendants: [] });

    const input = () => element.querySelector<HTMLInputElement>('input')!;
    const list = () => element.querySelector<HTMLElement>('.search-tag-options')!;
    const options = () => [...list().querySelectorAll<HTMLLIElement>('li[role=option]')];
    const active = () => options().findIndex(li => li.getAttribute('aria-selected') === 'true');

    /** Types `text` as a user would; `|` marks the caret (at the end without it). */
    async function typeText(text: string, wait = 150): Promise<void> {
        const caret = text.includes('|') ? text.indexOf('|') : text.length;
        input().focus();
        input().value = text.replace('|', '');
        input().setSelectionRange(caret, caret);
        input().dispatchEvent(new Event('input'));
        await settle(wait);
    }

    async function key(name: string): Promise<KeyboardEvent> {
        const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
        input().dispatchEvent(event);
        await settle();
        return event;
    }

    beforeEach(async () => {
        localStorage.clear();
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        store = TestBed.inject(ListingStore);
        navigate = vi.spyOn(store, 'navigate').mockImplementation(() => { });
        api.tags.mockResolvedValue(results(shrek, scifi));
        fixture = TestBed.createComponent(SearchBoxComponent);
        element = fixture.nativeElement;
        await fixture.whenStable();
    });

    afterEach(() => vi.restoreAllMocks());

    describe('searching', () => {
        it('searches once the typing pauses and the query is longer than 3 characters', async () => {
            await typeText('cat', 400);
            expect(navigate).not.toHaveBeenCalled();

            await typeText('cats', 100);
            expect(navigate).not.toHaveBeenCalled();
            await settle(300);
            expect(navigate).toHaveBeenCalledWith({ q: 'cats' }, { replace: false });
        });

        it('replaces the history entry while refining a search', async () => {
            store.route.set({ path: '', q: 'cats', sort: '', desc: false });
            await settle();

            await typeText('cats dogs', 400);

            expect(navigate).toHaveBeenCalledWith({ q: 'cats dogs' }, { replace: true });
        });

        it('does not search again for the query shown, e.g. after a trailing space', async () => {
            store.route.set({ path: '', q: 'cats', sort: '', desc: false });
            await settle();

            await typeText('cats ', 400);

            expect(navigate).not.toHaveBeenCalled();
        });

        it('searches short queries on Enter, trimmed', async () => {
            await typeText('  ab ', 0);

            element.dispatchEvent(new Event('submit', { cancelable: true }));
            await settle(400);

            expect(navigate).toHaveBeenCalledTimes(1);
            expect(navigate).toHaveBeenCalledWith({ q: 'ab' });
        });

        it('keeps the results listed on Enter for the query they are of, so the scroll position stays', async () => {
            store.route.set({ path: '', q: 'cats', sort: '', desc: false });
            store.status.set('ready');
            await settle();
            await typeText('cats ', 0);

            element.dispatchEvent(new Event('submit', { cancelable: true }));
            await settle(400);

            expect(navigate).not.toHaveBeenCalled();
            expect(TestBed.inject(SearchHistory).suggest('')).toEqual(['cats']);
        });

        it('searches the same query again on Enter when it failed', async () => {
            store.route.set({ path: '', q: 'cats', sort: '', desc: false });
            store.status.set('error');
            await settle();
            await typeText('cats', 0);

            element.dispatchEvent(new Event('submit', { cancelable: true }));
            await settle();

            expect(navigate).toHaveBeenCalledWith({ q: 'cats' });
        });

        it('goes back to browsing when the box is cleared', async () => {
            store.route.set({ path: '', q: 'cats', sort: '', desc: false });
            await settle();
            input().value = '';

            input().dispatchEvent(new Event('search'));

            expect(navigate).toHaveBeenCalledWith({ q: '' });
        });

        it('shows the query of the route, and keeps what is typed while it matches', async () => {
            store.route.set({ path: '', q: 'from url', sort: '', desc: false });
            await settle();
            expect(input().value).toBe('from url');
            expect(TestBed.inject(SearchBoxState).text()).toBe('from url');

            input().focus();
            input().value = 'from url  ';
            store.route.set({ path: '', q: 'from url', sort: 'size', desc: false });
            await settle();
            expect(input().value).toBe('from url  ');
        });

        it('shows the folder searched, and a click searches the whole library', async () => {
            store.route.set({ path: 'Media/2020', q: 'beach', sort: '', desc: false });
            await settle();
            const scope = element.querySelector<HTMLButtonElement>('.search-scope')!;

            expect(scope.hidden).toBe(false);
            expect(scope.textContent).toBe('in 2020');
            scope.click();

            expect(navigate).toHaveBeenCalledWith({ path: '' });
        });

        it('shows how many files were found while the box holds the query listed', async () => {
            const count = () => element.querySelector<HTMLElement>('.search-count');
            store.route.set({ path: '', q: 'cats', sort: '', desc: false });
            store.total.set({ count: 1234, capped: false });
            await settle();
            expect(count()).toBeNull();

            store.status.set('ready');
            await settle();
            expect(count()!.textContent).toBe((1234).toLocaleString());
            expect(count()!.title).toBe(`${(1234).toLocaleString()} files found`);

            store.total.set({ count: 10000, capped: true });
            await settle();
            expect(count()!.textContent).toBe(`${(10000).toLocaleString()}+`);
            expect(count()!.title).toContain('or more');

            await typeText('cats and', 0);
            expect(count()).toBeNull();
        });

        it('shows no count while browsing', async () => {
            store.route.set({ path: 'Media', q: '', sort: '', desc: false });
            store.total.set({ count: 12, capped: false });
            store.status.set('ready');
            await settle();

            expect(element.querySelector('.search-count')).toBeNull();
        });

        it('waits with the live search while a tag name is typed', async () => {
            await typeText('t:shr', 400);

            expect(navigate).not.toHaveBeenCalled();
        });
    });

    describe('tag suggestions', () => {
        it('suggests tags for the t: term around the caret', async () => {
            await typeText('cat t:sh');

            expect(api.tags).toHaveBeenLastCalledWith({ q: 'sh', limit: 12 });
            expect(list().hidden).toBe(false);
            expect(options()).toHaveLength(2);
            expect(active()).toBe(0);
            expect(input().getAttribute('aria-expanded')).toBe('true');
        });

        it('recognises tag:, negations and quoted names, in any case', async () => {
            await typeText('-TAG:"Science Fi');

            expect(api.tags).toHaveBeenLastCalledWith({ q: 'Science Fi', limit: 12 });
        });

        it('suggests every tag right after the prefix', async () => {
            await typeText('t:');

            expect(api.tags).toHaveBeenLastCalledWith({ q: '', limit: 12 });
        });

        it('suggests nothing outside a tag term', async () => {
            await typeText('t:shrek |dog');

            expect(list().hidden).toBe(true);
        });

        it('says when no tag matches, or there are no tags at all', async () => {
            api.tags.mockResolvedValue(results());
            await typeText('t:zzz');
            expect(list().querySelector('.empty')!.textContent).toBe('No tag matches “zzz”.');

            await typeText('t:');
            expect(list().querySelector('.empty')!.textContent).toBe('No tags yet.');
        });

        it('completes the term with the picked tag, then searches it right away', async () => {
            await typeText('beach -t:sh');

            options()[0].click();
            await settle();

            expect(input().value).toBe('beach -t:Shrek ');
            expect(input().selectionStart).toBe(input().value.length);
            expect(list().hidden).toBe(true);
            expect(navigate).toHaveBeenCalledWith({ q: 'beach -t:Shrek' }, { replace: false });
        });

        it('quotes a picked name holding spaces, and replaces only the term in the middle', async () => {
            await typeText('a t:sci| b');

            options()[1].click();
            await settle();

            expect(input().value).toBe('a t:"Science Fiction" b');
        });

        it('completes a term typed with an opening quote', async () => {
            await typeText('t:"Science');

            options()[1].click();
            await settle();

            expect(input().value).toBe('t:"Science Fiction" ');
        });

        it('picks with Enter or Tab instead of submitting or leaving the box', async () => {
            await typeText('t:s');
            await key('ArrowDown');
            expect(active()).toBe(1);

            const enter = await key('Enter');

            expect(enter.defaultPrevented).toBe(true);
            expect(input().value).toBe('t:"Science Fiction" ');

            await typeText('x t:s');
            const tab = await key('Tab');
            expect(tab.defaultPrevented).toBe(true);
            expect(input().value).toBe('x t:Shrek ');
        });

        it('wraps around with the arrow keys', async () => {
            await typeText('t:s');

            await key('ArrowUp');
            expect(active()).toBe(1);
            await key('ArrowDown');
            expect(active()).toBe(0);
        });

        it('closes the suggestions with Escape, keeping the text', async () => {
            await typeText('t:s');

            const escape = await key('Escape');

            expect(escape.defaultPrevented).toBe(true);
            expect(list().hidden).toBe(true);
            expect(input().value).toBe('t:s');
        });

        it('leaves the keys alone without suggestions', async () => {
            await typeText('cat');

            expect((await key('Enter')).defaultPrevented).toBe(false);
            expect((await key('ArrowDown')).defaultPrevented).toBe(false);
        });

        it('follows the caret moved with the arrow keys', async () => {
            await typeText('t:shrek dog');
            expect(list().hidden).toBe(true);

            input().setSelectionRange(3, 3);
            input().dispatchEvent(new KeyboardEvent('keyup', { key: 'ArrowLeft' }));
            await settle();

            expect(list().hidden).toBe(false);
            expect(api.tags).toHaveBeenLastCalledWith({ q: 's', limit: 12 });
        });

        it('suggests nothing while text is selected', async () => {
            await typeText('t:shrek');
            input().setSelectionRange(2, 7);
            input().dispatchEvent(new Event('click'));
            await settle();

            expect(list().hidden).toBe(true);
        });

        it('hides the suggestions when the box loses the focus, and drops late ones', async () => {
            const late = deferred<TagSearchResults>();
            api.tags.mockReturnValue(late.promise);
            await typeText('t:s');

            input().blur();
            late.resolve(results(shrek));
            await settle();

            expect(list().hidden).toBe(true);
        });

        it('hides the suggestions when they cannot be loaded', async () => {
            api.tags.mockRejectedValue(new ApiError(500, 'Boom'));

            await typeText('t:s');

            expect(list().hidden).toBe(true);
        });

        it('shows at most 12 suggestions, matches first', async () => {
            const many = Array.from({ length: 10 }, (_, i) => tag(`Tag ${i}`));
            api.tags.mockResolvedValue({ matches: many, descendants: [tag('Child 1'), tag('Child 2'), tag('Child 3')] });

            await typeText('t:tag');

            expect(options()).toHaveLength(12);
            expect(options().at(-1)!.textContent).toContain('Child 2');
        });
    });

    describe('search history', () => {
        let history: SearchHistory;
        const texts = () => options().map(li => li.querySelector('.search-history-text')!.textContent);

        function submit(text: string): void {
            input().value = text;
            element.dispatchEvent(new Event('submit', { cancelable: true }));
        }

        beforeEach(() => history = TestBed.inject(SearchHistory));

        it('keeps the submitted searches and suggests them when the box gets the focus, newest first', async () => {
            submit('cats');
            submit(' dogs ');
            submit('cats');

            await typeText('');

            expect(list().hidden).toBe(false);
            expect(list().getAttribute('aria-label')).toBe('Recent searches');
            expect(texts()).toEqual(['cats', 'dogs']);
            expect(JSON.parse(localStorage.getItem('sfe.searchHistory')!)).toEqual(['cats', 'dogs']);
        });

        it('suggests the searches starting with the text first, then those containing it, without the text itself', async () => {
            ['big cat', 'dog', 'Cats', 'cat'].forEach(q => history.record(q));

            await typeText('cat');

            expect(texts()).toEqual(['Cats', 'big cat']);
            await typeText('zebra');
            expect(list().hidden).toBe(true);
        });

        it('shows the typed characters in bold where they match, wherever they are', async () => {
            ['Big Cat', 'cats'].forEach(q => history.record(q));
            const bold = () => options().map(li => [...li.querySelectorAll('b')].map(b => b.textContent));

            await typeText('CA');

            expect(texts()).toEqual(['cats', 'Big Cat']);
            expect(bold()).toEqual([['ca'], ['Ca']]);

            await typeText('');
            expect(bold()).toEqual([[], []]);
        });

        it('leaves Enter to the box until a search is highlighted, then searches that one', async () => {
            ['one', 'two'].forEach(q => history.record(q));
            await typeText('');
            expect(active()).toBe(-1);

            await key('ArrowUp');
            expect(active()).toBe(1);
            await key('ArrowDown');
            expect(active()).toBe(-1);
            await key('ArrowDown');
            expect(active()).toBe(0);
            const enter = await key('Enter');

            expect(enter.defaultPrevented).toBe(true);
            expect(input().value).toBe('two');
            expect(navigate).toHaveBeenCalledWith({ q: 'two' });
            expect(history.entries()).toEqual(['two', 'one']);
            expect(list().hidden).toBe(true);
        });

        it('searches a clicked search', async () => {
            history.record('beach');
            await typeText('be');

            options()[0].click();
            await settle(400);

            expect(input().value).toBe('beach');
            expect(navigate).toHaveBeenCalledTimes(1);
            expect(navigate).toHaveBeenCalledWith({ q: 'beach' });
        });

        it('forgets a search with its remove button, keeping the others open', async () => {
            ['one', 'two'].forEach(q => history.record(q));
            await typeText('');

            options()[0].querySelector('button')!.click();
            await settle();

            expect(navigate).not.toHaveBeenCalled();
            expect(texts()).toEqual(['one']);
            expect(history.entries()).toEqual(['one']);
        });

        it('keeps a live search when the box is left on its results', async () => {
            await typeText('sunset');
            store.route.set({ path: '', q: 'sunset', sort: '', desc: false });

            input().blur();

            expect(history.entries()).toEqual(['sunset']);
        });

        it('suggests tags rather than searches in a tag term', async () => {
            history.record('t:shrek');

            await typeText('t:sh');

            expect(list().getAttribute('aria-label')).toBe('Tags');
            expect(options()).toHaveLength(2);
        });

        it('keeps only as many searches as configured, and none when off', async () => {
            await loadConfig(api, fakeConfig({ searchHistory: 2 }));
            ['a', 'b', 'c'].forEach(q => history.record(q));
            expect(history.entries()).toEqual(['c', 'b']);

            await loadConfig(api, fakeConfig({ searchHistory: 0 }));
            submit('d');
            await typeText('');

            expect(history.entries()).toEqual([]);
            expect(list().hidden).toBe(true);
        });
    });
});

describe('matchedParts', () => {
    it('marks the first match of the trimmed text, in any case', () => {
        expect(matchedParts('a cat, a Cat', ' CAT ')).toEqual([
            { text: 'a ', mark: false },
            { text: 'cat', mark: true },
            { text: ', a Cat', mark: false },
        ]);
        expect(matchedParts('cats', 'cats')).toEqual([{ text: 'cats', mark: true }]);
    });

    it('marks nothing without a match or when lower-casing moves the letters', () => {
        expect(matchedParts('dogs', 'cat')).toEqual([{ text: 'dogs', mark: false }]);
        expect(matchedParts('İstanbul', 'stan')).toEqual([{ text: 'İstanbul', mark: false }]);
    });
});
