/*
 * The sections of the administration page (`admin?section=&page=`), listed in its sidebar under their `group`, each
 * with its sub-pages listed under it (on phones, as chips above the page). A sub-page with `settings` shows that tab of
 * settings-schema.ts; the others show the section's tools (the tag list, duplicates, the background tasks...). Every
 * tab of settings-schema.ts belongs to exactly one sub-page. A sub-page shows to those with one of its `rights`
 * (/api/config); a section with none of its sub-pages left is not listed.
 */

import { Right, Rights, SETTINGS_RIGHTS } from '../core/rights';

export interface AdminSubpage {
    id: string;
    title: string;
    /** The id of the tab of settings-schema.ts it shows. */
    settings?: string;
    /** Rights, one of which shows it. */
    rights: readonly Right[];
}

export interface AdminSection {
    id: string;
    group: string;
    title: string;
    icon: string;
    description: string;
    /** The first one opens by default; with a single one, nothing is listed under the section. */
    subpages: AdminSubpage[];
}

/** The settings of a tab: those with its section's right in the library, and server administrators. */
const settingsPage = (settings: string, title = 'Settings', id = 'settings'): AdminSubpage =>
    ({ id, title, settings, rights: [SETTINGS_RIGHTS[settings], Rights.serverSettings] });
const page = (id: string, title: string, ...rights: Right[]): AdminSubpage => ({ id, title, rights });

export const ADMIN_SECTIONS: AdminSection[] = [
    {
        id: 'interface',
        group: 'Preferences',
        title: 'Interface',
        icon: 'theme',
        description: 'Colors, listings and the search box: the defaults of the library and of the server.',
        subpages: [
            settingsPage('appearance', 'Appearance', 'appearance'),
            settingsPage('browsing', 'Browsing', 'browsing'),
            settingsPage('search', 'Search', 'search'),
        ],
    },
    {
        id: 'playback',
        group: 'Preferences',
        title: 'Playback',
        icon: 'play',
        description: 'The video player, an app to open videos in, and where files are streamed from.',
        subpages: [settingsPage('playback')],
    },
    {
        id: 'scanning',
        group: 'Library',
        title: 'Scanning',
        icon: 'scan',
        description: 'What scans leave out, which files are photos, videos and audio, and what scans read from them.',
        subpages: [
            settingsPage('skipped', 'What to scan', 'skipped'),
            settingsPage('fileTypes', 'File types', 'types'),
            settingsPage('indexing', 'Indexing', 'indexing'),
        ],
    },
    {
        id: 'tags',
        group: 'Library',
        title: 'Tags',
        icon: 'tag',
        description: 'Create, edit and delete tags; the tag sidebar and its history.',
        subpages: [page('list', 'All tags', Rights.manageTags), settingsPage('tags')],
    },
    {
        id: 'tracking',
        group: 'Library',
        title: 'Duplicates & moves',
        icon: 'copy',
        description: 'Identical files, similar videos, and tagged files that were moved or renamed.',
        subpages: [
            page('duplicates', 'Duplicates', Rights.duplicates),
            page('similar', 'Similar videos', Rights.similarVideos),
            page('missing', 'Moved files', Rights.missingFiles),
            settingsPage('tracking'),
        ],
    },
    {
        id: 'thumbnails',
        group: 'Media',
        title: 'Thumbnails',
        icon: 'image',
        description: 'The previews of photos and videos in the listings and the viewer.',
        subpages: [settingsPage('thumbnails')],
    },
    {
        id: 'trickplay',
        group: 'Media',
        title: 'Trickplay',
        icon: 'trickplay',
        description: 'Frames along each video, shown above the player\'s seek bar and when hovering a video.',
        subpages: [page('activity', 'Activity', Rights.manageTrickplay), settingsPage('trickplay')],
    },
    {
        id: 'editor',
        group: 'Media',
        title: 'Picture editor',
        icon: 'crop',
        description: 'Where the pictures cropped in the viewer are saved, and their quality.',
        subpages: [settingsPage('editor')],
    },
    {
        id: 'access',
        group: 'Access',
        title: 'Spaces & accounts',
        icon: 'group',
        description: 'Who can sign in, which spaces they see, and what they may do in each. A space is one library with its own groups and accounts.',
        subpages: [
            page('spaces', 'Spaces', Rights.manageTenants, Rights.manageMembers),
            page('accounts', 'Accounts', Rights.manageUsers, Rights.manageMembers),
        ],
    },
    {
        id: 'tasks',
        group: 'Server',
        title: 'Background tasks',
        icon: 'clock',
        description: 'What runs now, when each task runs next, the schedules that start them, and their logs.',
        subpages: [
            page('tasks', 'Tasks', Rights.viewTasks),
            page('schedules', 'Schedules', Rights.scheduleTasks),
            page('history', 'History', Rights.viewTasks),
            settingsPage('tasks'),
        ],
    },
    {
        id: 'system',
        group: 'Server',
        title: 'System',
        icon: 'power',
        description: 'The server\'s version, library folder and ffmpeg, its API, backups, the server commands, and the videos it converts.',
        subpages: [
            page('about', 'About', Rights.serverSettings, Rights.apiDocs, Rights.backup, Rights.shutdown, Rights.dismount),
            page('installation', 'Installation', Rights.serverSettings),
            page('streams', 'Video streams', Rights.viewStreams),
        ],
    },
    {
        id: 'account',
        group: 'Account',
        title: 'My account',
        icon: 'user',
        description: 'Your own preferences, kept by the server for every device you sign in on, and your password.',
        subpages: [
            page('preferences', 'Preferences', Rights.ownPreferences),
            page('password', 'Password', Rights.ownPassword),
        ],
    },
];

/** The sections by group, in order, for the sidebar. */
export function groupsOf(sections: AdminSection[]): { title: string; sections: AdminSection[] }[] {
    return [...new Set(sections.map(s => s.group))].map(title => ({ title, sections: sections.filter(s => s.group === title) }));
}

export const ADMIN_GROUPS = groupsOf(ADMIN_SECTIONS);

/** The sections and sub-pages a session sees: those with one of their rights (`canAny`). */
export function visibleSections(canAny: (rights: readonly string[]) => boolean): AdminSection[] {
    return ADMIN_SECTIONS
        .map(section => ({ ...section, subpages: section.subpages.filter(page => canAny(page.rights)) }))
        .filter(section => section.subpages.length > 0);
}

/** The section with `id` among `sections` (all of them by default), the first one otherwise. */
export function findSection(id: string | null, sections: AdminSection[] = ADMIN_SECTIONS): AdminSection {
    return sections.find(section => section.id === id) ?? sections[0];
}

/** The sub-page of `section` with `id`, its first one otherwise. */
export function findSubpage(section: AdminSection, id: string | null): AdminSubpage {
    return section.subpages.find(page => page.id === id) ?? section.subpages[0];
}

/** The section and sub-page showing the tab `settings` of settings-schema.ts. */
export function locateSettings(settings: string): { section: AdminSection; subpage: AdminSubpage } | undefined {
    for (const section of ADMIN_SECTIONS) {
        const subpage = section.subpages.find(page => page.settings === settings);
        if (subpage)
            return { section, subpage };
    }
    return undefined;
}
