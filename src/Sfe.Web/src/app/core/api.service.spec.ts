import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { Api, ApiError, redirected } from './api.service';

describe('Api', () => {
    let api: Api;
    let http: HttpTestingController;

    beforeEach(() => {
        TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
        api = TestBed.inject(Api);
        http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => http.verify());

    it('uses relative URLs, so the app works under a virtual directory', async () => {
        const config = api.config();
        const request = http.expectOne('api/config');
        request.flush({ rootName: 'Root' });

        expect(request.request.method).toBe('GET');
        expect(await config).toEqual({ rootName: 'Root' });
    });

    it('leaves empty and false values out of the query, as the API defaults apply then', async () => {
        void api.search({ q: 'cat', path: '', after: null, limit: 50, sort: 'path', desc: false });
        const request = http.expectOne(r => r.url === 'api/search');

        expect(request.request.params.keys().sort()).toEqual(['limit', 'q', 'sort']);
        expect(request.request.params.get('limit')).toBe('50');
        request.flush({ items: [] });
    });

    it('keeps 0 and true in the query', () => {
        void api.browse({ path: 'a', offset: 0, limit: 100, sort: 'name', desc: true });
        const request = http.expectOne(r => r.url === 'api/browse');

        expect(request.request.params.get('offset')).toBe('0');
        expect(request.request.params.get('desc')).toBe('true');
        request.flush({});
    });

    it('sends the tag routes with the right methods and bodies', () => {
        void api.tags({ q: 'sh', limit: 40 });
        void api.tag(7);
        void api.createTag({ name: 'Cat' });
        void api.updateTag(7, { name: 'Kitty' });
        void api.deleteTag(7);
        void api.fileTags('a/b #1.jpg');
        void api.changeFileTags({ paths: ['a.jpg'], add: [1], remove: [2] });
        void api.fileViewed('a.jpg');
        void api.tagSidebar();
        void api.clearTagHistory();

        const search = http.expectOne(r => r.url === 'api/tags' && r.method === 'GET');
        expect(search.request.params.get('q')).toBe('sh');
        expect(http.expectOne(r => r.url === 'api/tags/7' && r.method === 'GET')).toBeTruthy();
        expect(http.expectOne(r => r.url === 'api/tags' && r.method === 'POST').request.body).toEqual({ name: 'Cat' });
        expect(http.expectOne(r => r.url === 'api/tags/7' && r.method === 'PUT').request.body).toEqual({ name: 'Kitty' });
        expect(http.expectOne(r => r.url === 'api/tags/7' && r.method === 'DELETE')).toBeTruthy();
        // A query parameter: encoded by HttpParams, not by hand.
        expect(http.expectOne(r => r.url === 'api/file-tags' && r.method === 'GET').request.params.get('path')).toBe('a/b #1.jpg');
        expect(http.expectOne(r => r.url === 'api/file-tags' && r.method === 'POST').request.body).toEqual({ paths: ['a.jpg'], add: [1], remove: [2] });
        expect(http.expectOne('api/file-tags/viewed').request.body).toEqual({ path: 'a.jpg' });
        expect(http.expectOne('api/tags/sidebar')).toBeTruthy();
        expect(http.expectOne(r => r.url === 'api/tags/history' && r.method === 'DELETE')).toBeTruthy();
    });

    it('names another space in the query of the access routes, and none for the session\'s', () => {
        void api.groups(false, 2);
        void api.groups(true);
        void api.createGroup({ name: 'Kids' }, 2);
        void api.updateGroup(5, { name: 'Kids' });
        void api.deleteGroup(5, 2);

        http.expectOne(r => r.url === 'api/access/groups' && r.method === 'GET' && r.params.get('tenant') === '2' && !r.params.has('all')).flush([]);
        http.expectOne(r => r.url === 'api/access/groups' && r.method === 'GET' && r.params.get('all') === 'true' && !r.params.has('tenant')).flush([]);
        http.expectOne(r => r.urlWithParams === 'api/access/groups?tenant=2' && r.method === 'POST').flush({});
        http.expectOne(r => r.urlWithParams === 'api/access/groups/5' && r.method === 'PUT').flush({});
        http.expectOne(r => r.urlWithParams === 'api/access/groups/5?tenant=2' && r.method === 'DELETE').flush(null);
    });

    it('encodes each segment of a path in the URL', () => {
        void api.trickplay('Ñandú/a #1?.mp4');
        void api.existingTrickplay('x/y.mp4');
        void api.imageAnimation('a b/c#.gif');

        expect(http.expectOne('api/trickplay/files/%C3%91and%C3%BA/a%20%231%3F.mp4')).toBeTruthy();
        expect(http.expectOne(r => r.url === 'api/trickplay/files/x/y.mp4').request.params.get('queue')).toBe('false');
        expect(http.expectOne('api/files/animated/a%20b/c%23.gif')).toBeTruthy();
    });

    it('forgets one missing file, or all of them', () => {
        void api.forgetMissingFiles(3);
        void api.forgetMissingFiles();

        expect(http.expectOne(r => r.url === 'api/missing-files/3' && r.method === 'DELETE')).toBeTruthy();
        expect(http.expectOne(r => r.url === 'api/missing-files' && r.method === 'DELETE')).toBeTruthy();
    });

    it('rejects with the first validation message of a problem', async () => {
        const created = api.createTag({ name: '' });
        http.expectOne('api/tags').flush(
            { title: 'One or more validation errors occurred.', errors: { name: ['A tag needs a name.'], color: ['Unknown color'] } },
            { status: 400, statusText: 'Bad Request' });

        const error = await created.catch(e => e) as ApiError;
        expect(error).toBeInstanceOf(ApiError);
        expect(error.status).toBe(400);
        expect(error.message).toBe('A tag needs a name.');
        expect(error.errors).toEqual({ name: ['A tag needs a name.'], color: ['Unknown color'] });
    });

    it('rejects with the detail, else the title, else the status of a problem', async () => {
        const detail = api.tag(1);
        http.expectOne('api/tags/1').flush({ title: 'Conflict', detail: 'Built-in tags cannot be deleted.' }, { status: 409, statusText: 'Conflict' });
        // A 409 resolves with its body (the API's current state).
        expect(await detail).toEqual({ title: 'Conflict', detail: 'Built-in tags cannot be deleted.' });

        const title = api.tag(2);
        http.expectOne('api/tags/2').flush({ title: 'Not Found' }, { status: 404, statusText: 'Not Found' });
        await expect(title).rejects.toThrow('Not Found');

        const bare = api.tag(3);
        http.expectOne('api/tags/3').flush(null, { status: 500, statusText: 'Internal Server Error' });
        await expect(bare).rejects.toThrow('500 Internal Server Error');

        const text = api.tag(4);
        http.expectOne('api/tags/4').flush('oops', { status: 502, statusText: 'Bad Gateway' });
        await expect(text).rejects.toThrow('502 Bad Gateway');
    });

    it('says when the server is not reachable', async () => {
        const request = api.config();
        http.expectOne('api/config').error(new ProgressEvent('error'), { status: 0, statusText: '' });

        const error = await request.catch(e => e) as ApiError;
        expect(error.status).toBe(0);
        expect(error.message).toBe('The server is not reachable');
    });

    it('resolves a conflict with its body: the trickplay status while it is off', async () => {
        const check = api.checkTrickplay();
        http.expectOne('api/trickplay').flush({ state: 'disabled' }, { status: 409, statusText: 'Conflict' });

        expect(await check).toEqual({ state: 'disabled' });
    });

    it('cancels the request when its signal aborts', async () => {
        const controller = new AbortController();
        const page = api.browse({ path: '', offset: 0, limit: 10, sort: 'name', desc: false }, controller.signal);
        const request = http.expectOne(r => r.url === 'api/browse');

        controller.abort();

        await expect(page).rejects.toMatchObject({ status: 0, message: 'Cancelled' });
        expect(request.cancelled).toBe(true);
    });

    it('does not send a request whose signal already aborted', async () => {
        const controller = new AbortController();
        controller.abort();

        await expect(api.folderCovers(['a'], controller.signal)).rejects.toThrow('Cancelled');
        http.expectNone('api/browse/covers');
    });

    it('marks the requests that change something, so that another site cannot send them with this browser\'s session', () => {
        void api.config();
        void api.createTag({ name: 'Cat' });
        void api.deleteTag(7);

        expect(http.expectOne('api/config').request.headers.has('X-SFE-Request')).toBe(false);
        expect(http.expectOne(r => r.url === 'api/tags' && r.method === 'POST').request.headers.get('X-SFE-Request')).toBe('1');
        expect(http.expectOne('api/tags/7').request.headers.get('X-SFE-Request')).toBe('1');
    });

    it('names the library the page shows, once known', () => {
        void api.config();
        expect(http.expectOne('api/config').request.headers.has('X-SFE-Tenant')).toBe(false);

        api.tenantId = 3;
        void api.scanStatus();
        expect(http.expectOne('api/scan').request.headers.get('X-SFE-Tenant')).toBe('3');
    });

    it('opens the sign-in page when a request needs a session, coming back to the page after', async () => {
        const router = TestBed.inject(Router);
        vi.spyOn(router, 'url', 'get').mockReturnValue('/admin?section=tags');
        const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

        const config = api.config();
        http.expectOne('api/config').flush({ status: 401 }, { status: 401, statusText: 'Unauthorized' });

        await expect(config).rejects.toMatchObject({ status: 401 });
        expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { return: '/admin?section=tags' } });
        expect(redirected(await config.catch(error => error))).toBe(true);
    });

    it('opens the setup page while the server has no administrator', async () => {
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

        const config = api.config();
        http.expectOne('api/config').flush({ type: 'setup-required', status: 503 }, { status: 503, statusText: 'Service Unavailable' });

        await expect(config).rejects.toMatchObject({ status: 503, type: 'setup-required' });
        expect(navigate).toHaveBeenCalledWith(['/setup']);
    });

    it('opens the update page while the install waits for its update, from any request', async () => {
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

        const setup = api.setupInfo();
        http.expectOne('api/setup').flush({ type: 'migration-required', status: 503 }, { status: 503, statusText: 'Service Unavailable' });

        await expect(setup).rejects.toMatchObject({ status: 503, type: 'migration-required' });
        expect(redirected(await setup.catch((error: unknown) => error))).toBe(true);
        expect(navigate).toHaveBeenCalledWith(['/migration']);
    });

    it('sends the setup code to start the update and to make a backup', () => {
        void api.startMigration('the-setup-code');
        void api.migrationBackup('the-setup-code');

        const start = http.expectOne('api/migration');
        const backup = http.expectOne('api/migration/backup');
        expect(start.request.method).toBe('POST');
        expect(start.request.body).toEqual({ code: 'the-setup-code' });
        expect(start.request.headers.get('X-SFE-Request')).toBe('1');
        expect(backup.request.body).toEqual({ code: 'the-setup-code' });
    });

    it('leaves the session and setup routes alone: their pages handle the answers', async () => {
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

        const signIn = api.signIn('admin', 'wrong', false);
        const request = http.expectOne('api/session/login');
        expect(request.request.body).toEqual({ userName: 'admin', password: 'wrong', remember: false });
        request.flush({ detail: 'Wrong name or password.' }, { status: 401, statusText: 'Unauthorized' });

        await expect(signIn).rejects.toThrow('Wrong name or password.');
        expect(navigate).not.toHaveBeenCalled();
    });

    it('asks for the settings of a level, and saves locks and preferences on their own routes', () => {
        void api.settings('server');
        void api.saveSettings({}, 'tenant');
        void api.saveSettingLocks(['player.seekSeconds'], 'server');
        void api.resetSettings();
        void api.savePreferences({ player: { seekSeconds: 30 } });

        expect(http.expectOne(r => r.url === 'api/settings' && r.method === 'GET').request.params.get('level')).toBe('server');
        expect(http.expectOne(r => r.url === 'api/settings?level=tenant' && r.method === 'PUT')).toBeTruthy();
        expect(http.expectOne('api/settings/locks?level=server').request.body).toEqual({ keys: ['player.seekSeconds'] });
        expect(http.expectOne(r => r.url === 'api/settings' && r.method === 'DELETE')).toBeTruthy();
        expect(http.expectOne('api/me/preferences').request.method).toBe('PUT');
    });

    it('saves only the changes of a level, or of the preferences, with PATCH', () => {
        void api.saveSettingChanges({ values: { 'player.seekSeconds': 15 }, lock: ['player.loop'], unlock: [] }, 'server');
        void api.savePreferenceChanges({ values: { 'player.loop': true } });

        const level = http.expectOne('api/settings?level=server');
        expect(level.request.method).toBe('PATCH');
        expect(level.request.headers.get('X-SFE-Request')).toBe('1');
        expect(level.request.body).toEqual({ values: { 'player.seekSeconds': 15 }, lock: ['player.loop'], unlock: [] });
        const preferences = http.expectOne('api/me/preferences');
        expect(preferences.request.method).toBe('PATCH');
        expect(preferences.request.body).toEqual({ values: { 'player.loop': true } });
    });
});
