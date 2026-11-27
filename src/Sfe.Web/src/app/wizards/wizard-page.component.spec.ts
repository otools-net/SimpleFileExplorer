import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Api } from '../core/api.service';
import { Rights } from '../core/rights';
import { ApiMock, fakeConfig, limitedConfig, loadConfig, mockApi, settle } from '../../testing/fakes';
import { routes } from '../app.routes';

let api: ApiMock;
let harness: RouterTestingHarness;

async function open(address: string, config = fakeConfig()): Promise<HTMLElement> {
    api = mockApi();
    api.tenantFolders.mockResolvedValue({ path: null, parent: null, folders: [] });
    TestBed.configureTestingModule({
        providers: [{ provide: Api, useValue: api }, provideRouter(routes.filter((r) => r.path === 'wizard'))],
    });
    await loadConfig(api, config);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(address);
    // The wizard's code loads after the page: a few turns.
    for (let i = 0; i < 4; i++) {
        await settle(10);
        harness.detectChanges();
    }
    return harness.routeNativeElement!;
}

describe('WizardPageComponent', () => {
    it('opens the wizard named in its address, titled after it', async () => {
        const page = await open('/wizard?id=new-space');

        expect(page.querySelector('.wizard-name')!.textContent).toBe('New space');
        expect(page.querySelector('.wizard-stage h2')!.textContent).toBe('Library folder');
        expect(document.title).toBe('New space – SFE');
    });

    it('says so for a wizard that does not exist, or that the session may not use', async () => {
        expect((await open('/wizard?id=nothing')).textContent).toContain('There is no such wizard.');
        TestBed.resetTestingModule();
        const limited = limitedConfig([Rights.browse]);
        expect((await open('/wizard?id=new-space', limited)).textContent).toContain('You may not use this wizard.');
    });

    it('goes back to the administration page the wizard belongs to', async () => {
        const page = await open('/wizard?id=new-space');
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

        page.querySelector<HTMLButtonElement>('.wizard-topbar .icon-button')!.click();

        expect(navigate).toHaveBeenCalledWith(['/admin'], { queryParams: { section: 'access', page: 'spaces' } });
    });
});
