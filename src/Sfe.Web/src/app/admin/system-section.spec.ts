import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiMock, buttonNamed, fakeConfig, installationView, limitedConfig, loadConfig, mockApi, settle, testProviders, type } from '../../testing/fakes';
import { ApiError } from '../core/api.service';
import { StreamingStatus, SystemInfo } from '../core/models';
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

    function status(overrides: Partial<StreamingStatus> = {}): StreamingStatus {
        return { available: true, mode: 'fallback', maxStreams: 2, streams: [], ...overrides };
    }

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

    it('lists the videos being converted, with who watches them', async () => {
        api.streamingStatus.mockResolvedValue(status({
            streams: [{
                id: 'a', path: 'Films/Old/film.avi', viewer: { library: 'Home', user: 'alice', address: '10.0.0.2' },
                start: 754, transcoded: true, started: new Date(Date.now() - 125_000).toISOString(), bytes: 3 * 1024 * 1024,
            }],
        }));
        await create('streams');
        const row = fixture.nativeElement.querySelector('.stream-row') as HTMLElement;
        expect(row.querySelector('.trickplay-name')!.textContent).toBe('film.avi');
        expect(row.querySelector('.trickplay-folder')!.textContent).toBe('Films/Old');
        expect(row.querySelector('.stream-who')!.textContent).toBe('alice · Home · 10.0.0.2');
        expect(row.textContent).toContain('from 12:34 · converted · 3 MB · for 2m 05s');
        expect(text()).toContain('At most 2 videos at once');
        expect(api.systemInfo).toHaveBeenCalled();     // loaded for About, not shown here
        expect(text()).not.toContain('Library folder');
    });

    it('says why nothing is converted', async () => {
        api.streamingStatus.mockResolvedValue(status({ available: false }));
        await create('streams');
        expect(text()).toContain('No video is being converted.');
        expect(text()).toContain('ffmpeg can\'t be found');

        api.streamingStatus.mockResolvedValue(status({ available: false, mode: 'off' }));
        await create('streams');
        expect(text()).toContain('Turned off in this space');
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

    it('shows the streams page to those with its right only', () => {
        const pages = (rights: string[]) => findSection('system', visibleSections(wanted => wanted.some(r => rights.includes(r)))).subpages.map(p => p.id);
        expect(pages([Rights.viewStreams])).toEqual(['streams']);
        expect(pages([Rights.serverSettings, Rights.viewStreams])).toEqual(['about', 'installation', 'startup', 'streams']);
        expect(pages([Rights.backup])).toEqual(['about']);
    });
});
