import { TestBed } from '@angular/core/testing';
import { ConfigService } from '../core/config.service';
import { ConnectionsConfig } from '../core/config.models';
import { FILE_SOURCE } from '../formats/file-source';
import { TextFile } from '../formats/format.models';
import { formatOf } from '../formats/formats';
import { ApiMock, fakeConfig, item, loadConfig, mockApi, testProviders } from '../../testing/fakes';
import { enterPlace } from './connection-pages';

const CONNECTIONS: ConnectionsConfig = {
    levels: ['account'],
    kinds: [{ kind: 'dropbox', name: 'Dropbox', icon: 'globe', signIn: [], capabilities: {} }],
};

/** Previews of a connection's files (agents/docs/features/connection-previews.md). */
describe('previews of a connection', () => {
    let api: ApiMock;
    let config: ConfigService;

    beforeEach(async () => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        config = await loadConfig(api, fakeConfig({ connections: CONNECTIONS, streaming: { always: false } }));
        enterPlace(config, 7);
    });

    afterEach(() => enterPlace(config, null));

    it('reads its text files through its own route, read only, in the text formats', async () => {
        const source = TestBed.inject(FILE_SOURCE);
        api.textFile.mockResolvedValue({ text: 'hello' } as TextFile);

        await source.textFile(item('Notes/a.md'), undefined, 'utf-8');

        expect(api.textFile).toHaveBeenCalledWith('Notes/a.md', undefined, 'utf-8', 7);
        expect(source.library).toBe(false);
        expect(formatOf(item('Notes/a.md'))?.id).toBe('markdown');
        expect(formatOf(item('data.json'))?.id).toBe('json');
        expect(formatOf(item('table.csv'))?.id).toBe('csv');
        expect(formatOf(item('film.srt'))?.id).toBe('subtitles');
        expect(formatOf(item('book.epub'))).toBeNull();
        expect(formatOf(item('app.bin'), { asText: true })?.id).toBe('text');

        enterPlace(config, null);
        expect(source.library).toBe(true);
    });

    it('plays its videos converted through its own routes, and shows thumbnails the server makes', async () => {
        const stream = config.videoStream('Trips/clip.mkv')!;
        api.streamProbe.mockResolvedValue({ duration: 3, transcoded: true });

        const url = stream.url(12.5);
        await stream.probe();

        expect(url).toMatch(/^api\/v1\/connections\/7\/streaming\/play\?path=Trips%2Fclip\.mkv&t=12\.5&player=\w+$/);
        expect(api.streamProbe).toHaveBeenCalledWith('Trips/clip.mkv', 7);
        expect(config.config()!.features.thumbnails).toBe(true);
    });
});
