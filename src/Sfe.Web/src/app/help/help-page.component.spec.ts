import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Api, ApiError } from '../core/api.service';
import { ApiMock, mockApi, settle, type } from '../../testing/fakes';
import { HelpPageComponent } from './help-page.component';
import { HelpIndex } from './help.models';

const INDEX: HelpIndex = {
    pages: [
        { path: 'README.md', title: 'SFE documentation', headings: [] },
        { path: 'user-guide/browsing.md', title: 'Browsing', headings: [{ level: 2, text: 'Layouts' }] },
        { path: 'reference/rights.md', title: 'Rights', headings: [{ level: 2, text: 'Every right' }] },
    ],
};

const PAGES: Record<string, string> = {
    'README.md': '# SFE documentation\n\nSee [Browsing](user-guide/browsing.md) and [the code](../AGENTS.md).\n',
    'user-guide/browsing.md': [
        '# Browsing',
        '',
        '![The grid](../images/library-grid.png)',
        '',
        'Who may: [rights](../reference/rights.md#every-right). [Below](#layouts).',
        '',
        '## Layouts',
        '',
        'Grid or list.',
    ].join('\n'),
    'reference/rights.md': '# Rights\n\n## Every right\n\nText.\n',
};

describe('HelpPageComponent', () => {
    let api: ApiMock;
    let harness: RouterTestingHarness;

    async function open(url: string): Promise<HTMLElement> {
        harness = await RouterTestingHarness.create();
        await harness.navigateByUrl(url, HelpPageComponent);
        await settle();
        harness.detectChanges();
        return harness.routeNativeElement!;
    }

    function article(element: HTMLElement): HTMLElement {
        return element.querySelector('.markdown-content')!;
    }

    beforeEach(() => {
        api = mockApi();
        api.helpIndex.mockResolvedValue(INDEX);
        api.helpPage.mockImplementation(async (path: string) => {
            if (!(path in PAGES)) throw new ApiError(404, 'Not found');
            return PAGES[path];
        });
        Element.prototype.scrollIntoView ??= () => {};
        TestBed.configureTestingModule({
            providers: [
                { provide: Api, useValue: api },
                provideRouter([{ path: 'help', component: HelpPageComponent }]),
            ],
        });
    });

    afterEach(() => TestBed.resetTestingModule());

    it('shows the first page and the contents by folder', async () => {
        const element = await open('/help');

        expect(api.helpPage).toHaveBeenCalledWith('README.md', expect.any(AbortSignal));
        expect(article(element).querySelector('h1')!.textContent).toBe('SFE documentation');
        expect([...element.querySelectorAll('.help-contents h2')].map((h) => h.textContent)).toEqual([
            'Getting started',
            'Using SFE',
            'Reference',
        ]);
        expect(element.querySelector('.help-contents [aria-current="page"]')!.textContent).toBe('SFE documentation');
        expect(TestBed.inject(Title).getTitle()).toBe('SFE documentation · SFE help');
    });

    it('keeps links and pictures in the help, and links out of it lead nowhere', async () => {
        const element = await open('/help?page=user-guide%2Fbrowsing.md');
        const links = [...article(element).querySelectorAll('a')];
        const picture = article(element).querySelector('img')!.getAttribute('src');
        await harness.navigateByUrl('/help');
        await settle();
        harness.detectChanges();

        expect(picture).toBe('api/v1/help/files/images/library-grid.png');
        expect(links.map((link) => link.getAttribute('href'))).toEqual([
            'help?page=reference%2Frights.md#every-right',
            'help?page=user-guide%2Fbrowsing.md#layouts',
        ]);
        const code = [...article(element).querySelectorAll('a')].find((link) => link.textContent === 'the code')!;
        expect(code.hasAttribute('href')).toBe(false);
    });

    it('follows a link to another page and its heading inside the app', async () => {
        const element = await open('/help?page=user-guide%2Fbrowsing.md');

        article(element).querySelector<HTMLAnchorElement>('a[data-path]')!.click();
        await settle();
        harness.detectChanges();

        expect(TestBed.inject(Router).url).toBe('/help?page=reference%2Frights.md#every-right');
        expect(api.helpPage).toHaveBeenLastCalledWith('reference/rights.md', expect.any(AbortSignal));
        await vi.waitFor(() => expect(article(element).querySelector('h1')!.textContent).toBe('Rights'));
    });

    it('searches the titles and headings, and opens a result', async () => {
        const element = await open('/help');

        type(element.querySelector<HTMLInputElement>('.help-search')!, 'layouts');
        harness.detectChanges();
        const results = [...element.querySelectorAll<HTMLAnchorElement>('.help-result')];

        expect(results.map((result) => [...result.children].map((part) => part.textContent))).toEqual([
            ['Layouts', 'Browsing'],
        ]);
        expect(results[0].getAttribute('href')).toBe('/help?page=user-guide%2Fbrowsing.md#layouts');
        expect(element.querySelector<HTMLElement>('.help-reader')!.hidden).toBe(true);

        results[0].click();
        await settle();
        harness.detectChanges();

        expect(element.querySelector('.help-results')).toBeNull();
        expect(TestBed.inject(Router).url).toBe('/help?page=user-guide%2Fbrowsing.md#layouts');
    });

    it('says when the page is not in the help', async () => {
        const element = await open('/help?page=nothing.md');

        expect(element.querySelector('.help-message')!.textContent).toContain('The help has no such page.');
    });

    it('asks to sign in when the server refuses a visitor, coming back to the page after', async () => {
        api.helpIndex.mockRejectedValue(new ApiError(401, 'Unauthorized'));
        api.helpPage.mockRejectedValue(new ApiError(401, 'Unauthorized'));

        const element = await open('/help?page=user-guide%2Fbrowsing.md');
        const link = element.querySelector<HTMLAnchorElement>('.help-message a')!;

        expect(element.querySelector('.help-message')!.textContent).toContain('Sign in to read the help.');
        expect(link.getAttribute('href')).toBe('/login?return=%2Fhelp%3Fpage%3Duser-guide%252Fbrowsing.md');
    });
});
