import { ConnectionIndexStatus } from '../core/connection.models';
import { target } from '../tasks/tasks';
import { describeIndex } from './connection-index';

const NOW = Date.parse('2026-11-15T12:00:00Z');

function status(overrides: Partial<ConnectionIndexStatus> = {}): ConnectionIndexStatus {
    return {
        indexed: true,
        running: false,
        listing: false,
        files: 1204,
        folders: 37,
        lastSync: '2026-11-15T11:55:00Z',
        tooLong: 0,
        lastError: null,
        activity: null,
        ...overrides,
    };
}

describe('The index of a connection, in words', () => {
    it('says when nothing is indexed in the space yet', () => {
        expect(describeIndex(status({ indexed: false, lastSync: null }), NOW)).toContain('Not indexed in this space');
    });

    it('tells the counts and when it was synced', () => {
        const text = describeIndex(status(), NOW);
        expect(text).toContain((1204).toLocaleString() + ' files and 37 folders');
        expect(text).toContain('synced 5 minutes ago');
    });

    it('tells what a running sync does, a listing left half done, what was left out and what went wrong', () => {
        expect(describeIndex(status({ running: true, activity: 'Listing “Team”: 12 files' }), NOW)).toContain(
            'Listing “Team”: 12 files',
        );
        const text = describeIndex(status({ listing: true, lastSync: null, tooLong: 2, lastError: 'Gone.' }), NOW);
        expect(text).toContain('A full listing is under way');
        expect(text).toContain('2 items left out (path too long)');
        expect(text).toContain('Last sync stopped: Gone.');
    });

    it('names the connection a run of the task synced, never a private one', () => {
        expect(target('connectionIndex', 'Team')).toBe('“Team”');
        expect(target('connectionIndex', null)).toBeNull();
    });
});
