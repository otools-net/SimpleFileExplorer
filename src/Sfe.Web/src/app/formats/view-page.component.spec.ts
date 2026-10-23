import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { ApiError } from '../core/api.service';
import { ItemProperties } from '../core/models';
import { ApiMock, item, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { READING_KEY } from './reading/reading.service';
import { ContextMenu } from '../menus/context-menu';
import { ViewPageComponent } from './view-page.component';

describe('ViewPageComponent', () => {
    let api: ApiMock;

    function properties(path: string, kind: 'file' | 'image' = 'file'): ItemProperties {
        return { item: item(path, { kind, size: 2048 }), details: {} as ItemProperties['details'], canChecksum: true };
    }

    async function open(query: Record<string, string>): Promise<HTMLElement> {
        TestBed.configureTestingModule({
            providers: testProviders(api, { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } }),
        });
        await loadConfig(api);
        const fixture = TestBed.createComponent(ViewPageComponent);
        await fixture.whenStable();
        await vi.waitFor(() => expect(fixture.nativeElement.querySelector('.view-page-stage')!.children.length).toBeGreaterThan(0));
        await settle();
        await fixture.whenStable();
        return fixture.nativeElement;
    }

    beforeEach(() => {
        localStorage.removeItem(READING_KEY);
        api = mockApi();
        api.textFile.mockImplementation(async (path: string) => ({ text: `Text of ${path}`, encoding: 'utf-8', lineEnding: 'lf', version: 'v', size: 20, modified: '2026-10-18T10:00:00Z' }));
    });

    afterEach(() => {
        localStorage.removeItem(READING_KEY);
        TestBed.resetTestingModule();
    });

    it('shows a text file on a page of its own, with a way back to its folder', async () => {
        api.properties.mockResolvedValue(properties('Docs/Trip/notes.txt'));

        const element = await open({ path: 'Docs/Trip/notes.txt' });

        expect(api.properties).toHaveBeenCalledWith('Docs/Trip/notes.txt');
        await vi.waitFor(() => expect(element.querySelector('.cm-content')?.textContent).toContain('Text of Docs/Trip/notes.txt'));
        expect(element.querySelector('.text-view')!.classList).toContain('prose');
        expect(element.querySelector('.view-page-title strong')!.textContent).toBe('notes.txt');
        expect(element.querySelector('a[aria-label="Open its folder"]')!.getAttribute('href')).toBe('./?path=Docs%2FTrip');
        expect(element.querySelector('a[aria-label="Download"]')).not.toBeNull();
        expect(TestBed.inject(Title).getTitle()).toBe('notes.txt – SFE');
    });

    it('has a menu of its own for a right click on the page: its folder (in a new tab too), download', async () => {
        api.properties.mockResolvedValue(properties('Docs/Trip/notes.txt'));
        await open({ path: 'Docs/Trip/notes.txt' });
        const page = TestBed.inject(ContextMenu).page()!;

        expect(page).toMatchObject({ kind: 'page', name: 'notes.txt' });
        expect(page.entries().map(entry => entry.id)).toEqual(['openFolder', 'folderNewTab', 'download']);
        expect(page.entries()[1]).toMatchObject({ href: './?path=Docs%2FTrip', newTab: true });
    });

    it('shows any file as text when asked, and none otherwise', async () => {
        api.properties.mockResolvedValue(properties('Docs/backup.zip'));
        let element = await open({ path: 'Docs/backup.zip' });
        expect(element.querySelector('.viewer-message')!.textContent).toContain('no viewer');
        TestBed.resetTestingModule();

        element = await open({ path: 'Docs/backup.zip', asText: '1' });

        await vi.waitFor(() => expect(element.querySelector('.cm-content')?.textContent).toContain('Text of Docs/backup.zip'));
    });

    it('says when the file is not there or not visible', async () => {
        api.properties.mockRejectedValue(new ApiError(404, 'Not found'));

        const element = await open({ path: 'Docs/gone.txt' });

        expect(element.querySelector('.viewer-message')!.textContent).toContain('not there');
        expect(api.textFile).not.toHaveBeenCalled();
    });
});
