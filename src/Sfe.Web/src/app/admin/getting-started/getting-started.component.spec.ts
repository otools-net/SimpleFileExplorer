import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiMock, fakeConfig, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { ApiError } from '../../core/api.service';
import { Rights } from '../../core/rights';
import { WIZARDS, WizardEntry } from '../../wizards/wizard-registry';
import { ADMIN_SECTIONS } from '../admin-sections';
import { GettingStartedView } from '../admin.models';
import { GETTING_STARTED } from './getting-started-items';
import { GettingStartedComponent } from './getting-started.component';
import { gettingStartedLines } from './getting-started-lines';

describe('Getting started', () => {
    @Component({ imports: [GettingStartedComponent], template: `<sfe-getting-started />` })
    class Host {}

    let api: ApiMock;
    let fixture: ComponentFixture<Host>;
    const element = () => fixture.nativeElement as HTMLElement;
    const text = () => element().textContent!.replace(/\s+/g, ' ').trim();

    const VIEW: GettingStartedView = {
        items: [
            { id: 'library', done: true },
            { id: 'groups', done: false },
            { id: 'folders', done: false },
            { id: 'accounts', done: true },
            { id: 'connection', done: false },
        ],
    };

    async function create(view: GettingStartedView = VIEW, config = fakeConfig({ mode: 'external' })): Promise<void> {
        api.gettingStarted.mockResolvedValue(view);
        await loadConfig(api, config);
        fixture = TestBed.createComponent(Host);
        await settle();
        fixture.detectChanges();
    }

    beforeEach(() => {
        localStorage.clear();
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
    });

    afterEach(() => TestBed.resetTestingModule());

    it('lists the first things to set up, ticks those done and links each to its page', async () => {
        await create();

        const items = [...element().querySelectorAll('li')];
        expect(items.map((item) => item.querySelector('a')!.textContent!.trim())).toEqual([
            'Library folder',
            'A group and its rights',
            'Folder access',
            'An account',
            'A connection',
        ]);
        expect(items.map((item) => item.classList.contains('done'))).toEqual([true, false, false, true, false]);
        expect(items[0].textContent).toContain('Done');
        expect(text()).toContain('2 of 5 done');
        expect(items[1].querySelector('a')!.getAttribute('href')).toBe('/admin?section=access&page=groups');
        // Its wizard is registered: the item opens it.
        expect(items[4].querySelector('a')!.getAttribute('href')).toBe('/wizard?id=add-connection');
    });

    it('leaves out the accounts on a Local server, where only administrators sign in', async () => {
        await create(VIEW, fakeConfig({ mode: 'local' }));

        expect(text()).not.toContain('An account');
        expect(text()).toContain('1 of 4 done');
    });

    it('is hidden by its ×, per account and space, and comes back from the link left in its place', async () => {
        await create();
        element().querySelector<HTMLButtonElement>('button[aria-label="Hide the list"]')!.click();
        fixture.detectChanges();

        expect(element().querySelector('.getting-started')).toBeNull();
        const show = element().querySelector<HTMLButtonElement>('.getting-started-show')!;
        expect(show.textContent!.trim()).toBe('Getting started: 2 of 5 done');

        TestBed.resetTestingModule();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await create();
        expect(element().querySelector('.getting-started')).toBeNull();

        TestBed.resetTestingModule();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await create(VIEW, fakeConfig({ mode: 'external', tenant: { id: 7, slug: 'other', name: 'Other' } }));
        expect(element().querySelector('.getting-started')).not.toBeNull();
    });

    it('shows nothing once everything is done and the list is hidden', async () => {
        const done = { items: VIEW.items.map((item) => ({ ...item, done: true })) };
        await create(done);
        expect(text()).toContain('All set');
        element().querySelector<HTMLButtonElement>('button[aria-label="Hide the list"]')!.click();
        fixture.detectChanges();

        expect(text()).toBe('');
    });

    it('shows nothing when the server refuses or has no item for the session', async () => {
        api.gettingStarted.mockRejectedValue(new ApiError(403, 'Forbidden'));
        await loadConfig(api, fakeConfig());
        fixture = TestBed.createComponent(Host);
        await settle();
        fixture.detectChanges();
        expect(text()).toBe('');

        TestBed.resetTestingModule();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await create({ items: [] });
        expect(text()).toBe('');
    });

    it('opens the wizard of an item once one is registered under its id, and the session may open it', () => {
        const wizard: WizardEntry = {
            id: 'share-folder',
            title: 'Share a folder',
            description: '',
            words: '',
            right: Rights.manageMembers,
            home: { section: 'access', page: 'folders' },
            load: () => new Promise(() => {}),
        };
        (WIZARDS as WizardEntry[]).push(wizard);
        try {
            const view = { items: [{ id: 'folders', done: false }] };
            expect(gettingStartedLines(view, () => true, 'external')[0].link).toEqual({
                path: '/wizard',
                params: { id: 'share-folder' },
            });
            expect(gettingStartedLines(view, () => false, 'external')[0].link.path).toBe('/admin');
        } finally {
            (WIZARDS as WizardEntry[]).pop();
        }
    });

    it('leads each item to an administration page that exists', () => {
        for (const entry of GETTING_STARTED) {
            const section = ADMIN_SECTIONS.find((known) => known.id === entry.home.section);
            expect(section, entry.id).toBeDefined();
            expect(
                section!.subpages.some((page) => page.id === entry.home.page),
                entry.id,
            ).toBe(true);
        }
    });
});
