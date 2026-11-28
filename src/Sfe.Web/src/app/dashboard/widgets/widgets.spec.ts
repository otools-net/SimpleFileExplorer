import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { ClientConfig } from '../../core/config.models';
import { Notifications } from '../../core/notifications';
import { TagUsage } from '../../core/tag.models';
import { ApiMock, fakeConfig, loadConfig, mockApi, settle, tag, testProviders } from '../../../testing/fakes';
import { ConnectionsWidgetComponent } from './connections-widget.component';
import { NotificationsWidgetComponent } from './notifications-widget.component';
import { TagHistoryWidgetComponent } from './tag-history-widget.component';
import { UploadsWidgetComponent } from './uploads-widget.component';

const CONNECTIONS = { levels: ['account' as const], kinds: [] };

function usage(name: string, id: number): TagUsage {
    return { tag: { ...tag(name), id }, fileCount: 3 };
}

describe('dashboard widgets', () => {
    let api: ApiMock;

    async function render(component: Type<unknown>, config: ClientConfig = fakeConfig()): Promise<HTMLElement> {
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api, config);
        const fixture = TestBed.createComponent(component);
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
    }

    beforeEach(() => {
        api = mockApi();
    });

    afterEach(() => TestBed.resetTestingModule());

    describe('uploads', () => {
        it('lists the uploads a reload interrupted, each opening its folder', async () => {
            api.uploads.mockResolvedValue([
                {
                    id: 'u1',
                    target: 'Photos',
                    name: 'a.jpg',
                    size: 400,
                    received: 100,
                    modified: null,
                    ownerId: 1,
                    ownerName: 'admin',
                    started: '2026-11-28T10:00:00Z',
                    lastActivity: '2026-11-28T10:01:00Z',
                },
            ]);
            const element = await render(UploadsWidgetComponent);

            const link = element.querySelector<HTMLAnchorElement>('.dashboard-upload a')!;
            expect(link.textContent).toContain('Uploading 1 file to “Photos”');
            expect(link.getAttribute('href')).toBe('/?path=Photos');
            expect(element.querySelector('[role="progressbar"]')!.getAttribute('aria-valuenow')).toBe('25');
            expect(api.uploads).toHaveBeenCalledWith(false, null);
        });

        it('says when there are none', async () => {
            api.uploads.mockResolvedValue([]);
            const element = await render(UploadsWidgetComponent);
            expect(element.textContent).toContain('No uploads under way');
        });
    });

    describe('notifications', () => {
        it('lists the latest notifications, and how many more the bell has', async () => {
            TestBed.configureTestingModule({ providers: testProviders(api) });
            await loadConfig(api);
            const notifications = TestBed.inject(Notifications);
            for (let i = 1; i <= 7; i++)
                notifications.record(`Message ${i}`, i === 7 ? 'error' : 'info', [], Date.now() - (8 - i) * 1000);
            const fixture = TestBed.createComponent(NotificationsWidgetComponent);
            fixture.detectChanges();
            const element = fixture.nativeElement as HTMLElement;

            const items = [...element.querySelectorAll('.dashboard-notification')];
            expect(items.map((item) => item.querySelector('.notification-text')!.textContent)).toEqual([
                'Message 7',
                'Message 6',
                'Message 5',
                'Message 4',
                'Message 3',
            ]);
            expect(items[0].classList).toContain('error');
            expect(element.textContent).toContain('2 more: the bell above lists them all.');
        });

        it('says when there are none', async () => {
            const element = await render(NotificationsWidgetComponent);
            expect(element.textContent).toContain('No notifications.');
        });
    });

    describe('connections', () => {
        it('lists the connections with their state, each opening its files', async () => {
            api.connections.mockResolvedValue({
                usable: [
                    { id: 4, kind: 'dropbox', name: 'Family', level: 'library', state: 'ready', access: 'write' },
                    {
                        id: 5,
                        kind: 'dropbox',
                        name: 'Old',
                        level: 'account',
                        state: 'needsReconnecting',
                        access: 'read',
                    },
                ],
                managed: [],
                groups: [],
                libraries: [],
            });
            const element = await render(ConnectionsWidgetComponent, fakeConfig({ connections: CONNECTIONS }));

            const links = [...element.querySelectorAll<HTMLAnchorElement>('.quick-link-target')];
            expect(links.map((link) => link.getAttribute('href'))).toEqual(['/?c=4', '/?c=5']);
            const states = [...element.querySelectorAll('.dashboard-state')];
            expect(states.map((state) => state.textContent)).toEqual(['read and change', 'sign in again']);
            expect(states[1].classList).toContain('warning');
        });

        it('says when there are none', async () => {
            api.connections.mockResolvedValue({ usable: [], managed: [], groups: [], libraries: [] });
            const element = await render(ConnectionsWidgetComponent, fakeConfig({ connections: CONNECTIONS }));
            expect(element.textContent).toContain('No connections you may use');
        });
    });

    describe('tag history', () => {
        const sidebar = { popular: [], lucky: [], searched: [usage('Beach', 1)], viewed: [usage('Cats', 2)] };

        it('lists the tags last searched and viewed, each finding its files', async () => {
            api.tagSidebar.mockResolvedValue({ ...sidebar, historyEnabled: true });
            const element = await render(TagHistoryWidgetComponent);
            const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

            expect(element.querySelector('[data-history="searched"]')!.textContent).toContain('Beach');
            expect(element.querySelector('[data-history="viewed"]')!.textContent).toContain('Cats');
            element.querySelector<HTMLElement>('[data-history="viewed"] .tag-name')!.click();
            expect(navigate).toHaveBeenCalledWith(['/'], { queryParams: { q: 't:"Cats"' } });
        });

        it('says when the history is switched off', async () => {
            api.tagSidebar.mockResolvedValue({ ...sidebar, searched: [], viewed: [], historyEnabled: false });
            const element = await render(TagHistoryWidgetComponent);
            expect(element.textContent).toContain('The tag history is switched off');
        });
    });
});
