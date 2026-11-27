import { helpLink } from './help-links';

/** The help page of an administration section, and the heading of each of its sub-pages (their GitHub anchors). */
interface SectionHelp {
    page: string;
    subpages?: Record<string, string>;
}

/**
 * Where the help explains each section and sub-page of the administration page (admin-sections.ts): the "?" next to
 * a sub-page's title opens it. A spec checks that every section has one and that every page and heading exists.
 */
export const ADMIN_HELP: Readonly<Record<string, SectionHelp>> = {
    account: {
        page: 'administration/my-account.md',
        subpages: {
            preferences: 'settings',
            password: 'password',
            connections: 'connections',
            tokens: 'access-tokens',
        },
    },
    interface: {
        page: 'administration/interface.md',
        subpages: { appearance: 'appearance', browsing: 'browsing', search: 'search' },
    },
    playback: {
        page: 'administration/playback.md',
        subpages: { player: 'player', streaming: 'streaming', streams: 'video-streams' },
    },
    scanning: {
        page: 'administration/scanning.md',
        subpages: {
            skipped: 'what-to-scan',
            exclusions: 'excluded-folders',
            types: 'file-types',
            indexing: 'indexing',
        },
    },
    files: {
        page: 'administration/file-management.md',
        subpages: { trash: 'trash', uploads: 'uploads', settings: 'settings' },
    },
    keepAlive: { page: 'administration/keep-alive.md', subpages: { status: 'status', settings: 'settings' } },
    tags: { page: 'administration/tags.md', subpages: { list: 'all-tags', settings: 'settings' } },
    tracking: {
        page: 'administration/duplicates-and-moves.md',
        subpages: { duplicates: 'duplicates', similar: 'similar-videos', missing: 'moved-files', settings: 'settings' },
    },
    thumbnails: { page: 'administration/thumbnails.md' },
    trickplay: { page: 'administration/trickplay.md', subpages: { activity: 'activity', settings: 'settings' } },
    editor: { page: 'administration/picture-editor.md' },
    access: {
        page: 'administration/spaces-and-accounts.md',
        subpages: {
            spaces: 'spaces',
            accounts: 'accounts',
            groups: 'groups',
            folders: 'folder-access',
            visitors: 'visitors',
            tokens: 'access-tokens',
            tokenSettings: 'access-tokens',
        },
    },
    connections: {
        page: 'administration/connections.md',
        subpages: {
            connections: 'managing-a-connection',
            setup: 'setting-up-dropbox-server-administrators',
            settings: 'levels',
        },
    },
    tasks: {
        page: 'administration/background-tasks.md',
        subpages: { tasks: 'tasks', schedules: 'schedules', history: 'history', settings: 'settings' },
    },
    logs: { page: 'administration/logs.md', subpages: { entries: 'log', settings: 'settings' } },
    system: {
        page: 'administration/system.md',
        subpages: { about: 'about', installation: 'installation', startup: 'start-up', maintenance: 'maintenance' },
    },
};

/** The address of the help about a section's sub-page (its page when the sub-page has no heading of its own). */
export function adminHelpLink(section: string, subpage: string): string | null {
    const help = ADMIN_HELP[section];
    return help ? helpLink(help.page, help.subpages?.[subpage]) : null;
}
