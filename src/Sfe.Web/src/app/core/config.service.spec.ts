import { TestBed } from '@angular/core/testing';
import { ApiMock, fakeConfig, item, limitedConfig, loadConfig, mockApi, testProviders } from '../../testing/fakes';
import { Api } from './api.service';
import { ConfigService, createUrls, markSaved, withVersion } from './config.service';
import { Rights } from './rights';

describe('createUrls', () => {
    it('links the files through the API, each segment encoded', () => {
        const urls = createUrls(fakeConfig());

        expect(urls.file('a b/c#1%.jpg')).toBe('api/files/raw/a%20b/c%231%25.jpg');
        expect(urls.download('x/y.mp4')).toBe('api/files/raw/x/y.mp4?download=true');
        expect(urls.thumbnail('x/y.mp4')).toBe('api/thumbnails/x/y.mp4');
        expect(urls.view('a b/c&d.txt')).toBe('view?path=a+b%2Fc%26d.txt');
        expect(urls.trickplaySheet('x/y.mp4', 2, 99)).toBe('api/trickplay/files/x/y.mp4?sheet=2&v=99');
    });

    it('links the files to the data server when there is one, downloads still through the API', () => {
        const urls = createUrls(fakeConfig({ dataServer: 'http://nas:8080' }));

        expect(urls.file('a b.jpg')).toBe('http://nas:8080/a%20b.jpg');
        expect(urls.download('a b.jpg')).toBe('api/files/raw/a%20b.jpg?download=true');
    });

    it('fills a data server template: {url} with the path, {encodedUrl} with it fully encoded', () => {
        const plain = createUrls(fakeConfig({ dataServer: 'http://nas/files/{url}?raw=1' }));
        const query = createUrls(fakeConfig({ dataServer: 'http://nas/get?file={encodedUrl}&also={url}' }));

        expect(plain.file('Movies/a b#1.mp4')).toBe('http://nas/files/Movies/a%20b%231.mp4?raw=1');
        expect(query.file('Movies/a b&c.mp4')).toBe('http://nas/get?file=Movies%2Fa%20b%26c.mp4&also=Movies/a%20b%26c.mp4');
        expect(query.download('a.mp4')).toBe('api/files/raw/a.mp4?download=true');
    });

    it('opens the player at a whole second, from the start when under one', () => {
        const urls = createUrls(fakeConfig());

        expect(urls.player('a/b c.mp4')).toBe('player?path=a%2Fb+c.mp4');
        expect(urls.player('a.mp4', 0.9)).toBe('player?path=a.mp4');
        expect(urls.player('a.mp4', 61.7)).toBe('player?path=a.mp4&t=61');
    });

    it('has no external player link without a template', () => {
        const urls = createUrls(fakeConfig({ externalPlayer: null }));

        expect(urls.external('a.mp4')).toBeNull();
        expect(urls.externalName).toBeNull();
    });

    it('fills {url} and {encodedUrl} with the file\'s full address', () => {
        const address = 'http://nas:5080/api/files/shared/TOKEN/a%20b.mp4';
        const vlc = createUrls(fakeConfig({ externalPlayer: { name: 'VLC', url: 'vlc://{url}' } }));
        const encoded = createUrls(fakeConfig({ externalPlayer: { name: 'Infuse', url: 'infuse://x-callback-url/play?url={encodedUrl}&again={encodedUrl}' } }));

        expect(vlc.external(address)).toBe(`vlc://${address}`);
        expect(vlc.externalName).toBe('VLC');
        const full = encodeURIComponent(address);
        expect(encoded.external(address)).toBe(`infuse://x-callback-url/play?url=${full}&again=${full}`);
    });

    it('appends the address to a template without a placeholder', () => {
        const urls = createUrls(fakeConfig({ externalPlayer: { name: 'Player', url: 'player://open?u=' } }));

        expect(urls.external('http://nas/x.mkv')).toBe('player://open?u=http://nas/x.mkv');
    });
});

describe('withVersion', () => {
    it('marks the addresses of a file saved over in this tab only, not those of a data server', () => {
        expect(withVersion('api/files/raw/a.jpg', { path: 'a.jpg' })).toBe('api/files/raw/a.jpg');

        markSaved('b.jpg', '2026-10-19T10:00:00Z');

        const time = Date.parse('2026-10-19T10:00:00Z');
        expect(withVersion('api/files/raw/b.jpg', { path: 'b.jpg' })).toBe(`api/files/raw/b.jpg?v=${time}`);
        expect(withVersion('api/thumbnails/b.jpg?x=1', { path: 'b.jpg' })).toBe(`api/thumbnails/b.jpg?x=1&v=${time}`);
        expect(withVersion('http://nas/files/b.jpg', { path: 'b.jpg' })).toBe('http://nas/files/b.jpg');
        expect(withVersion('api/files/raw/a.jpg', { path: 'a.jpg' })).toBe('api/files/raw/a.jpg');
    });
});

describe('ConfigService', () => {
    let api: ApiMock;
    let config: ConfigService;

    beforeEach(() => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        config = TestBed.inject(ConfigService);
    });

    it('has nothing before it is loaded', () => {
        expect(config.config()).toBeNull();
        expect(config.urls()).toBeNull();
        expect(() => config.value).toThrow('The configuration is not loaded yet.');
    });

    it('rejects when the server cannot be reached, still unloaded', async () => {
        api.config.mockRejectedValue(new Error('The server is not reachable'));

        await expect(config.load()).rejects.toThrow('The server is not reachable');
        expect(config.config()).toBeNull();
    });

    it('rebuilds the links when loaded again (settings saved)', async () => {
        await loadConfig(api, fakeConfig());
        expect(config.links.file('a.jpg')).toBe('api/files/raw/a.jpg');

        await loadConfig(api, fakeConfig({ dataServer: 'http://media' }));
        expect(config.links.file('a.jpg')).toBe('http://media/a.jpg');
    });

    it('sends large images through their thumbnail, only when there are thumbnails', async () => {
        await loadConfig(api, fakeConfig({ largeImageSize: 1000 }));

        expect(config.isLargeImage(item('a.jpg', { size: 1001 }))).toBe(true);
        expect(config.isLargeImage(item('a.jpg', { size: 1000 }))).toBe(false);
        expect(config.isLargeImage(item('a.jpg', { size: null }))).toBe(false);
        expect(config.isLargeImage(item('a.mp4', { size: 5000 }))).toBe(false);

        await loadConfig(api, fakeConfig({ largeImageSize: 1000, features: { thumbnails: false, trickplay: false } }));
        expect(config.isLargeImage(item('a.jpg', { size: 5000 }))).toBe(false);
    });

    it('knows the session\'s rights, and names its library in every request', async () => {
        await loadConfig(api, limitedConfig([Rights.browse, Rights.viewTags], { tenant: { id: 7, slug: 'films', name: 'Films' } }));

        expect(config.can(Rights.browse)).toBe(true);
        expect(config.can(Rights.assignTags)).toBe(false);
        expect(config.canAny([Rights.scan, Rights.viewTags])).toBe(true);
        expect(config.canAny([Rights.scan])).toBe(false);
        expect(config.user()?.userName).toBe('alice');
        expect(TestBed.inject(Api).tenantId).toBe(7);

        config.clear();
        expect(config.config()).toBeNull();
        expect(config.can(Rights.browse)).toBe(false);
        expect(TestBed.inject(Api).tenantId).toBeNull();
    });

    it('opens external players through a link of their own, which works without the browser\'s session', async () => {
        await loadConfig(api, fakeConfig({ externalPlayer: { name: 'VLC', url: 'vlc://{url}' } }));
        api.fileLink.mockResolvedValue({ url: 'api/files/shared/TOKEN/a%20b.mkv', expires: '2026-01-01T00:00:00Z' });

        const link = await config.externalLink('Movies/a b.mkv');

        expect(api.fileLink).toHaveBeenCalledWith('Movies/a b.mkv');
        expect(link).toBe(`vlc://${new URL('api/files/shared/TOKEN/a%20b.mkv', document.baseURI).href}`);
    });

    it('gives external players the data server\'s address as it is: that server has no sessions', async () => {
        await loadConfig(api, fakeConfig({ externalPlayer: { name: 'VLC', url: 'vlc://{url}' }, dataServer: 'http://nas/x/{url}/' }));

        expect(await config.externalLink('a/b.mkv')).toBe('vlc://http://nas/x/a/b.mkv/');
        expect(api.fileLink).not.toHaveBeenCalled();
    });

    it('has no external player link without one (or without the right: the server sends none)', async () => {
        await loadConfig(api, fakeConfig({ externalPlayer: null }));

        expect(await config.externalLink('a.mkv')).toBeNull();
        expect(api.fileLink).not.toHaveBeenCalled();
    });
});
