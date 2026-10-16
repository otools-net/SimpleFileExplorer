import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiMock, buttonNamed, fakeConfig, installationView, limitedConfig, loadConfig, mockApi, settle, testProviders, type } from '../../testing/fakes';
import { ApiError } from '../core/api.service';
import { SystemInfo } from '../core/models';
import { Rights } from '../core/rights';
import { findSection, visibleSections } from './admin-sections';
import { SystemSectionComponent } from './system-section.component';

describe('System section', () => {
    @Component({
        imports: [SystemSectionComponent],
        template: `<sfe-system-section [tab]="tab()" />`,
    })
    class Host {
        readonly tab = signal('about');
    }

    let api: ApiMock;
    let fixture: ComponentFixture<Host>;
    const text = () => (fixture.nativeElement as HTMLElement).textContent!.replace(/\s+/g, ' ');

    const INFO: SystemInfo = {
        root: 'D:\\Media\\',
        ffmpeg: { configured: 'ffmpeg', path: 'C:\\ffmpeg\\bin\\ffmpeg.exe', version: '7.1-full_build' },
    };

    async function create(tab = 'about', config = fakeConfig()): Promise<void> {
        await loadConfig(api, config);
        fixture = TestBed.createComponent(Host);
        fixture.componentInstance.tab.set(tab);
        await settle();
    }

    beforeEach(() => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
    });

    afterEach(() => TestBed.resetTestingModule());

    it('tells server administrators where the library is and which ffmpeg runs', async () => {
        api.systemInfo.mockResolvedValue(INFO);
        await create();
        expect(fixture.nativeElement.querySelector('.system-root').textContent).toBe('D:\\Media\\');
        const ffmpeg = fixture.nativeElement.querySelector('.system-ffmpeg') as HTMLElement;
        expect(ffmpeg.dataset['found']).toBe('yes');
        expect(ffmpeg.querySelector('.system-badge')!.textContent).toBe('Found');
        expect(ffmpeg.querySelector('code')!.textContent).toBe('C:\\ffmpeg\\bin\\ffmpeg.exe');
        expect(ffmpeg.textContent).toContain('version 7.1-full_build');
    });

    it('says when ffmpeg is missing, and where it was looked for', async () => {
        api.systemInfo.mockResolvedValue({ ...INFO, ffmpeg: { configured: '/opt/ffmpeg', path: null, version: null } });
        await create();
        expect(text()).toContain('Not found');
        expect(text()).toContain('Looked for /opt/ffmpeg');
        expect(fixture.nativeElement.querySelector('.system-ffmpeg').dataset['found']).toBe('no');
    });

    it('keeps the server\'s layout from other administrators', async () => {
        await create('about', limitedConfig([Rights.apiDocs]));
        expect(api.systemInfo).not.toHaveBeenCalled();
        expect(text()).not.toContain('Library folder');
    });

    it('asks for the setup code before showing the installation settings, then saves them', async () => {
        api.systemInfo.mockResolvedValue(INFO);
        api.serverFolders.mockResolvedValue({ path: null, parent: null, folders: [] });
        await create('installation');
        const element = fixture.nativeElement as HTMLElement;
        expect(element.querySelector('sfe-installation-form')).toBeNull();

        api.checkInstallation.mockRejectedValueOnce(new ApiError(400, 'x', { code: ['This is not the server\'s setup code.'] }));
        type(element.querySelector<HTMLInputElement>('#installation-code')!, 'wrong-code-0000');
        await settle();
        buttonNamed(element, 'Unlock')!.click();
        await settle();
        expect(element.querySelector('.field-error')!.textContent).toContain('setup code');

        api.checkInstallation.mockResolvedValue({ token: 'the-token', settings: installationView() });
        api.saveInstallation.mockResolvedValue({ settings: installationView({ port: 8080, restartRequired: true }), restartRequired: true });
        buttonNamed(element, 'Unlock')!.click();
        await settle();
        expect(element.querySelector('sfe-installation-form')).not.toBeNull();
        expect(text()).toContain('the next scan treats the files of the old folder as gone');
        type(element.querySelector<HTMLInputElement>('#installation-port')!, '8080');
        await settle();
        buttonNamed(element, 'Save')!.click();
        await settle();

        expect(api.saveInstallation).toHaveBeenCalledWith('the-token', { network: false, port: 8080 });
        expect(text()).toContain('applies once SFE starts again');
    });

    it('shows each page to those with its rights only', () => {
        const pages = (section: string, rights: string[]) =>
            findSection(section, visibleSections(wanted => wanted.some(r => rights.includes(r)))).subpages.map(p => p.id);
        expect(pages('system', [Rights.serverSettings])).toEqual(['about', 'installation', 'startup']);
        expect(pages('system', [Rights.serverSettings, Rights.backup])).toEqual(['about', 'installation', 'startup', 'maintenance']);
        expect(pages('system', [Rights.backup])).toEqual(['maintenance']);
        expect(pages('system', [Rights.apiDocs, Rights.shutdown])).toEqual(['about', 'maintenance']);
        expect(pages('playback', [Rights.viewStreams])).toEqual(['streams']);
    });
});
