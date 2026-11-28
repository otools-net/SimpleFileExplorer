import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfigService } from '../../core/config.service';
import { Rights } from '../../core/rights';
import { ToastService } from '../../core/toast.service';
import { ItemMenu } from '../../files/item-menu';
import { ContextMenu } from '../../menus/context-menu';
import {
    ALL_RIGHTS,
    ApiMock,
    item,
    limitedConfig,
    loadConfig,
    mockApi,
    settle,
    setMediaQuery,
    testProviders,
} from '../../../testing/fakes';
import { HomeFolder } from '../library.models';
import { HomeFolderService } from './home-folder.service';
import { HomeLinkComponent } from './home-link.component';

function home(overrides: Partial<HomeFolder> = {}): HomeFolder {
    return {
        path: 'Homes/tom',
        name: 'tom',
        exists: true,
        chosen: false,
        group: 'Users',
        groupPath: 'Homes/tom',
        ...overrides,
    };
}

@Component({
    selector: 'sfe-home-link-host',
    imports: [HomeLinkComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `<aside class="sidebar"><section sfeHomeLink></section></aside>`,
})
class HostComponent {}

describe('Home folder', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<HostComponent>;
    let element: HTMLElement;
    let toasts: ToastService;

    const section = () => element.querySelector<HTMLElement>('section.home-link')!;
    const target = () => element.querySelector<HTMLAnchorElement>('.quick-link-target');

    async function create(answer: HomeFolder, rights: string[] = ALL_RIGHTS): Promise<void> {
        api.homeFolder.mockResolvedValue(answer);
        await loadConfig(api, limitedConfig(rights));
        fixture = TestBed.createComponent(HostComponent);
        element = fixture.nativeElement;
        await settle();
        fixture.detectChanges();
    }

    function menuIds(): string[] {
        target()!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
        return TestBed.inject(ContextMenu)
            .open()!
            .target.entries()
            .map((entry) => entry.id);
    }

    beforeEach(() => {
        setMediaQuery('(max-width: 760px)', false);
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        toasts = TestBed.inject(ToastService);
        vi.spyOn(toasts, 'success').mockImplementation(() => {});
        vi.spyOn(toasts, 'warning').mockImplementation(() => {});
    });

    afterEach(() => vi.restoreAllMocks());

    it('shows Home at the top of the sidebar, opening the folder its group gives', async () => {
        await create(home());

        expect(section().hidden).toBe(false);
        expect(target()!.textContent).toContain('Home');
        expect(target()!.getAttribute('href')).toContain('path=Homes%2Ftom');
        expect(target()!.title).toBe('Open your home folder, /Homes/tom (given by the group “Users”).');
    });

    it('shows nothing without a home folder, nor without the right (then asks nothing)', async () => {
        await create(home({ path: null, name: null, group: null, groupPath: null }));
        expect(section().hidden).toBe(true);
        expect(target()).toBeNull();

        TestBed.resetTestingModule();
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await create(home(), [Rights.browse]);
        expect(api.homeFolder).not.toHaveBeenCalled();
        expect(section().hidden).toBe(true);
    });

    it('fades a home folder not there, and says so instead of opening it (SFE never makes it)', async () => {
        await create(home({ exists: false }));
        const click = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 });

        target()!.dispatchEvent(click);

        expect(element.querySelector('.quick-link')!.classList).toContain('missing');
        expect(click.defaultPrevented).toBe(true);
        expect(toasts.warning).toHaveBeenCalledWith(
            'Your home folder “tom” is not there: not made yet, moved or deleted.',
        );
        target()!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
        expect(TestBed.inject(ContextMenu).open(), 'nothing to do: no menu').toBeNull();
    });

    it("offers going back to the group's home folder once the account chose its own", async () => {
        await create(home({ path: 'Media', name: 'Media', chosen: true }));
        api.clearHomeFolder.mockResolvedValue(home());

        const ids = menuIds();
        const entry = TestBed.inject(ContextMenu)
            .open()!
            .target.entries()
            .find((candidate) => candidate.id === 'clearHomeFolder')!;
        entry.run!();
        await settle();
        fixture.detectChanges();

        expect(ids).toEqual(['open', 'newTab', 'quickLink', 'folderAccess', 'clearHomeFolder']);
        expect(entry.label).toBe("Use your group's home folder");
        expect(api.clearHomeFolder).toHaveBeenCalled();
        expect(target()!.getAttribute('href')).toContain('path=Homes%2Ftom');
        expect(toasts.success).toHaveBeenCalledWith("Your home folder is your group's again.");
    });

    describe('in the menus of folders', () => {
        const folder = item('Media/Trips', { kind: 'directory' });

        async function menu(answer: HomeFolder): Promise<ItemMenu> {
            api.homeFolder.mockResolvedValue(answer);
            await loadConfig(api, limitedConfig(ALL_RIGHTS));
            await TestBed.inject(HomeFolderService).load();
            return TestBed.inject(ItemMenu);
        }

        it('makes a folder the home folder, never a file', async () => {
            const items = await menu(home());
            api.setHomeFolder.mockResolvedValue(home({ path: 'Media/Trips', name: 'Trips', chosen: true }));
            const entry = items.entries(folder).find((candidate) => candidate.id === 'homeFolder')!;

            entry.run();
            await settle();

            expect(entry).toMatchObject({ label: 'Set as home folder', group: 'organise' });
            expect(api.setHomeFolder).toHaveBeenCalledWith('Media/Trips');
            expect(TestBed.inject(HomeFolderService).path()).toBe('Media/Trips');
            expect(toasts.success).toHaveBeenCalledWith('“Trips” is now your home folder.');
            expect(items.entries(item('Media/a.jpg')).some((candidate) => candidate.id === 'homeFolder')).toBe(false);
        });

        it('stops using the chosen one; nothing to do on the one the group gives', async () => {
            const chosen = await menu(home({ path: 'Media/Trips', name: 'Trips', chosen: true }));
            const entry = chosen.entries(folder).find((candidate) => candidate.id === 'homeFolder')!;
            expect(entry.label).toBe('Stop using as home folder');

            TestBed.inject(HomeFolderService).home.set(home({ path: 'Media/Trips', name: 'Trips' }));
            expect(chosen.entries(folder).some((candidate) => candidate.id === 'homeFolder')).toBe(false);
        });

        it("leaves it out in a connection's folders", async () => {
            const items = await menu(home());
            TestBed.inject(ConfigService).place.set({ id: 3 } as never);
            expect(items.entries(folder).some((candidate) => candidate.id === 'homeFolder')).toBe(false);
        });
    });
});
