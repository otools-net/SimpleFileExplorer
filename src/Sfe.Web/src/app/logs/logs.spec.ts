import { TestBed } from '@angular/core/testing';
import { LogEntry, LogSearchResult } from '../core/models';
import { Rights } from '../core/rights';
import { ApiMock, limitedConfig, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { visibleSections } from '../admin/admin-sections';
import { LogsSectionComponent } from './logs-section.component';
import { dayStart, NO_FILTERS, shortCategory, toQuery } from './logs';

let api: ApiMock;

beforeEach(async () => {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api);
});

afterEach(() => vi.restoreAllMocks());

function entry(overrides: Partial<LogEntry> = {}): LogEntry {
    return {
        id: 10, timestamp: '2026-10-15T08:30:00Z', level: 'warning', category: 'Sfe.Core.Indexing.ScanCoordinator',
        message: 'Cannot read Movies/a.mkv', exception: null, tenantId: 1, ...overrides,
    };
}

function result(overrides: Partial<LogSearchResult> = {}): LogSearchResult {
    return { entries: [entry()], next: null, retentionDays: 30, writeFiles: false, filesDirectory: null, allSpaces: true, ...overrides };
}

async function render() {
    const fixture = TestBed.createComponent(LogsSectionComponent);
    await settle();
    return { fixture, root: fixture.nativeElement as HTMLElement };
}

const buttonNamed = (root: HTMLElement, label: string) =>
    [...root.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent!.trim() === label);

describe('log filters', () => {
    it('make the query of api/logs, the "to" day included', () => {
        const query = toQuery({ text: '  scan ', levels: new Set(['error', 'warning']), fromDay: '2026-10-01', toDay: '2026-10-03', space: 'server' }, 42);

        expect(query).toEqual({
            q: 'scan',
            levels: 'warning,error',
            from: new Date(2026, 9, 1).toISOString(),
            to: new Date(2026, 9, 4).toISOString(),
            tenant: 0,
            before: 42,
        });
        expect(toQuery(NO_FILTERS)).toEqual({ q: null, levels: null, from: null, to: null, tenant: null, before: null });
        expect(toQuery({ ...NO_FILTERS, space: 3 }).tenant).toBe(3);
    });

    it('ignore broken days and shorten loggers to their last name', () => {
        expect(dayStart('')).toBeNull();
        expect(dayStart('15/10/2026')).toBeNull();
        expect(shortCategory('Sfe.Core.Indexing.ScanCoordinator')).toBe('ScanCoordinator');
        expect(shortCategory('Sfe.Api')).toBe('Api');
        expect(shortCategory('Program')).toBe('Program');
    });
});

describe('LogsSectionComponent', () => {
    it('lists the entries newest first with their level, space and logger, and opens one on its error', async () => {
        api.logs.mockResolvedValue(result({
            entries: [entry({ id: 12, level: 'error', message: 'Scan failed', exception: 'System.IO.IOException: gone\n   at Scan()' }), entry({ id: 11, tenantId: null, category: 'Sfe.Api' })],
        }));
        const { root } = await render();

        const rows = root.querySelectorAll('.log-entry');
        expect(rows).toHaveLength(2);
        expect(rows[0].classList).toContain('level-error');
        expect(rows[0].querySelector('.log-level')!.textContent).toBe('Error');
        expect(rows[0].querySelector('.log-space')!.textContent).toBe('Library');
        expect(rows[0].querySelector('.log-category')!.textContent).toBe('ScanCoordinator');
        expect(rows[1].querySelector('.log-space')!.textContent).toBe('Server');
        expect(root.querySelector('.log-exception')).toBeNull();

        rows[0].querySelector<HTMLButtonElement>('.log-entry-head')!.click();
        await settle();

        expect(root.querySelector('.log-exception')!.textContent).toContain('System.IO.IOException: gone');
        expect(root.textContent).toContain('Entries are kept 30 days.');
    });

    it('shows security warnings as such, and filters them apart from warnings', async () => {
        api.logs.mockResolvedValue(result({
            entries: [entry({ id: 13, level: 'securityWarning', category: 'Sfe.Api.Endpoints.SessionEndpoints', message: 'Sign-in refused for admin from 203.0.113.7: wrong name or password' })],
        }));
        const { root } = await render();

        const row = root.querySelector('.log-entry')!;
        expect(row.classList).toContain('level-securityWarning');
        expect(row.querySelector('.log-level')!.textContent).toBe('Security warning');

        const chips = [...root.querySelectorAll<HTMLLabelElement>('.logs-level-chip')].map(l => l.textContent!.trim());
        expect(chips.indexOf('Security warning')).toBe(chips.indexOf('Warning') + 1);
        [...root.querySelectorAll<HTMLLabelElement>('.logs-level-chip')].find(l => l.textContent!.trim() === 'Security warning')!.querySelector('input')!.click();
        await settle();
        expect(api.logs).toHaveBeenLastCalledWith(expect.objectContaining({ levels: 'securityWarning' }), expect.anything());
    });

    it('searches again by severity, text (after a pause), day and space', async () => {
        api.logs.mockResolvedValue(result());
        const { root } = await render();
        expect(api.logs).toHaveBeenCalledWith(expect.objectContaining({ q: null, levels: null, tenant: null }), expect.anything());

        const error = [...root.querySelectorAll<HTMLLabelElement>('.logs-level-chip')].find(l => l.textContent!.trim() === 'Error')!;
        error.querySelector('input')!.click();
        await settle();
        expect(api.logs).toHaveBeenLastCalledWith(expect.objectContaining({ levels: 'error' }), expect.anything());

        const text = root.querySelector<HTMLInputElement>('input[type="search"]')!;
        text.value = 'boom';
        text.dispatchEvent(new Event('input'));
        await settle();
        expect(api.logs).not.toHaveBeenLastCalledWith(expect.objectContaining({ q: 'boom' }), expect.anything());
        await settle(450);
        expect(api.logs).toHaveBeenLastCalledWith(expect.objectContaining({ q: 'boom', levels: 'error' }), expect.anything());

        const from = root.querySelector<HTMLInputElement>('input[type="date"]')!;
        from.value = '2026-10-01';
        from.dispatchEvent(new Event('change'));
        await settle();
        expect(api.logs).toHaveBeenLastCalledWith(expect.objectContaining({ from: new Date(2026, 9, 1).toISOString() }), expect.anything());

        const space = root.querySelector<HTMLSelectElement>('.logs-space select')!;
        space.value = 'server';
        space.dispatchEvent(new Event('change'));
        await settle();
        expect(api.logs).toHaveBeenLastCalledWith(expect.objectContaining({ tenant: 0 }), expect.anything());

        buttonNamed(root, 'Clear filters')!.click();
        await settle();
        expect(api.logs).toHaveBeenLastCalledWith(expect.objectContaining({ q: null, levels: null, from: null, tenant: null }), expect.anything());
    });

    it('adds older pages at the end', async () => {
        api.logs.mockResolvedValueOnce(result({ entries: [entry({ id: 20 })], next: 20 }))
            .mockResolvedValueOnce(result({ entries: [entry({ id: 19 })], next: null }));
        const { root } = await render();

        buttonNamed(root, 'Load older entries')!.click();
        await settle();

        expect(api.logs).toHaveBeenLastCalledWith(expect.objectContaining({ before: 20 }));
        expect(root.querySelectorAll('.log-entry')).toHaveLength(2);
        expect(buttonNamed(root, 'Load older entries')).toBeUndefined();
    });

    it('shows a space administrator only its space, without the space filter', async () => {
        api.logs.mockResolvedValue(result({ allSpaces: false, writeFiles: true }));
        const { root } = await render();

        expect(root.querySelector('.logs-space')).toBeNull();
        expect(root.querySelector('.log-space')).toBeNull();
        expect(root.textContent).toContain('a copy goes to a file per day.');
        expect(root.textContent).toContain('This page shows the entries of Library.');
    });

    it('says why the log could not be read', async () => {
        api.logs.mockRejectedValue(new Error('Forbidden'));
        const { root } = await render();

        expect(root.querySelector('.form-error')!.textContent).toContain('Forbidden');
    });

    it('asks the page for its settings', async () => {
        api.logs.mockResolvedValue(result());
        const { fixture, root } = await render();
        const opened = vi.fn();
        fixture.componentInstance.openSettings.subscribe(opened);

        buttonNamed(root, 'Keep for…')!.click();

        expect(opened).toHaveBeenCalled();
    });
});

describe('the Logs section', () => {
    it('shows to those with the right to see the logs, its settings to those with the log settings', async () => {
        const viewer = await loadConfig(api, limitedConfig([Rights.viewLogs]));
        const logs = visibleSections(rights => viewer.canAny(rights)).find(section => section.id === 'logs');

        expect(logs?.subpages.map(page => page.id)).toEqual(['entries']);

        const none = await loadConfig(api, limitedConfig([Rights.browse]));
        expect(visibleSections(rights => none.canAny(rights)).find(section => section.id === 'logs')).toBeUndefined();

        const settings = await loadConfig(api, limitedConfig([Rights.logSettings]));
        expect(visibleSections(rights => settings.canAny(rights)).find(section => section.id === 'logs')?.subpages.map(page => page.id)).toEqual(['settings']);
    });
});
