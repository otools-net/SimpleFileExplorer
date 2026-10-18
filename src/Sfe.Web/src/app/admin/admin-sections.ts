/*
 * The sections of the administration page (`admin?section=&page=`), listed in its sidebar under their `group`, each
 * with its sub-pages listed under it (on phones, as chips above the page). A sub-page with `settings` shows that tab of
 * settings-schema.ts; the others show the section's tools (the tag list, duplicates, the background tasks...). Every
 * tab of settings-schema.ts belongs to exactly one sub-page. A sub-page shows to those with one of its `rights`
 * (/api/config); a section with none of its sub-pages left is not listed. My account comes first: everyone has it.
 * `/admin` without a section is the overview of the sections (on phones, their list).
 */

import { Right, Rights, SETTINGS_RIGHTS } from '../core/rights';

export interface AdminSubpage {
    id: string;
    title: string;
    /** Under its title, instead of the section's description; a section with a single sub-page shows its own. */
    description?: string;
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
const settingsPage = (settings: string, description?: string, title = 'Settings', id = 'settings'): AdminSubpage =>
    ({ id, title, description, settings, rights: [SETTINGS_RIGHTS[settings], Rights.serverSettings] });
const page = (id: string, title: string, description: string, ...rights: Right[]): AdminSubpage => ({ id, title, description, rights });

export const ADMIN_SECTIONS: AdminSection[] = [
    {
        id: 'account',
        group: 'Account',
        title: 'My account',
        icon: 'user',
        description: 'Your own preferences, kept by the server for every device you sign in on, and your password.',
        subpages: [
            page('preferences', 'Preferences', 'Your own choices over the space\'s settings, kept by the server for every device you sign in on.', Rights.ownPreferences),
            page('password', 'Password', 'The password you sign in with.', Rights.ownPassword),
        ],
    },
    {
        id: 'interface',
        group: 'Viewing',
        title: 'Interface',
        icon: 'theme',
        description: 'Colors, listings and the search box: the defaults of the library and of the server.',
        subpages: [
            settingsPage('appearance', 'The default theme, and this browser\'s own; how text files read.', 'Appearance', 'appearance'),
            settingsPage('browsing', 'How many items a listing loads, the pictures on folders, and the resolutions and durations shown.', 'Browsing', 'browsing'),
            settingsPage('search', 'How many results a search loads, its wording as you type, and the searches remembered.', 'Search', 'search'),
        ],
    },
    {
        id: 'playback',
        group: 'Viewing',
        title: 'Playback',
        icon: 'play',
        description: 'The video player, an app to open videos in, and how videos reach the browser: from a data server, or converted by this one.',
        subpages: [
            settingsPage('player', 'The player\'s jumps and loop, and the app the viewer\'s ▶ button opens videos and audio in.', 'Player', 'player'),
            settingsPage('streaming', 'Where files are streamed from, and how the server converts the videos a browser can\'t play.', 'Streaming', 'streaming'),
            page('streams', 'Video streams', 'The videos the server converts now, in every space, and who watches them.', Rights.viewStreams),
        ],
    },
    {
        id: 'scanning',
        group: 'Library',
        title: 'Scanning',
        icon: 'scan',
        description: 'What scans leave out, which files are photos, videos and audio, and what scans read from them.',
        subpages: [
            settingsPage('skipped', 'The folders and files scans leave out, and hidden items.', 'What to scan', 'skipped'),
            settingsPage('fileTypes', 'Which extensions are photos, videos and audio.', 'File types', 'types'),
            settingsPage('indexing', 'What scans read from files (fingerprints, resolutions, durations), and how many folders at once.', 'Indexing', 'indexing'),
        ],
    },
    {
        id: 'files',
        group: 'Library',
        title: 'File management',
        icon: 'folder',
        description: 'The trash, uploads under way, the names new files and folders may have, and where SFE keeps its work files.',
        subpages: [
            page('trash', 'Trash', 'What was deleted to the trash: restore it, or delete it for good.', Rights.deleteFiles, Rights.deletePermanently),
            page('uploads', 'Uploads', 'Uploads not finished yet, by everyone: how far they got, and cancelling them.', Rights.fileSettings),
            settingsPage('files', 'How deleting works, how long the trash keeps things, limits of copies, uploads and zips, names, and where SFE keeps its work files.'),
        ],
    },
    {
        id: 'tags',
        group: 'Library',
        title: 'Tags',
        icon: 'tag',
        description: 'Create, edit and delete tags; the tag sidebar and its history.',
        subpages: [
            page('list', 'All tags', 'Create, edit and delete the tags of the library.', Rights.manageTags),
            settingsPage('tags', 'How many tags the sidebar shows, and the history of the tags given.'),
        ],
    },
    {
        id: 'tracking',
        group: 'Library',
        title: 'Duplicates & moves',
        icon: 'copy',
        description: 'Identical files, similar videos, and tagged files that were moved or renamed.',
        subpages: [
            page('duplicates', 'Duplicates', 'Groups of identical files, and removing the extra copies.', Rights.duplicates),
            page('similar', 'Similar videos', 'Videos showing the same pictures in other files: another resolution, quality or format, or trimmed.', Rights.similarVideos),
            page('missing', 'Moved files', 'Tagged files that are no longer where they were, and where they went.', Rights.missingFiles),
            settingsPage('tracking', 'The smallest duplicates, how alike similar videos are, and how moved files keep their tags.'),
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
        subpages: [
            page('activity', 'Activity', 'What the trickplay job works on, what waits, and what it did last.', Rights.manageTrickplay),
            settingsPage('trickplay', 'Which videos get trickplay, its frames, and how much of the server it uses.'),
        ],
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
            page('spaces', 'Spaces', 'Each space\'s library, groups, accounts and visitors.', Rights.manageTenants, Rights.manageMembers),
            page('accounts', 'Accounts', 'Everyone who can sign in, with the spaces each one sees and their groups there.', Rights.manageUsers, Rights.manageMembers),
        ],
    },
    {
        id: 'tasks',
        group: 'Server',
        title: 'Background tasks',
        icon: 'clock',
        description: 'What runs now, when each task runs next, the schedules that start them, and their logs.',
        subpages: [
            page('tasks', 'Tasks', 'What runs now and when each task runs next; start or cancel them.', Rights.viewTasks),
            page('schedules', 'Schedules', 'When the tasks start by themselves.', Rights.scheduleTasks),
            page('history', 'History', 'The past runs of the tasks, with their logs.', Rights.viewTasks),
            settingsPage('tasks', 'How long runs and their logs are kept.'),
        ],
    },
    {
        id: 'logs',
        group: 'Server',
        title: 'Logs',
        icon: 'docs',
        description: 'The messages, warnings and errors the server logged, searchable by text, date and severity, and how long they are kept.',
        subpages: [
            page('entries', 'Log', 'The messages, warnings and errors the server logged, searchable by text, date and severity.', Rights.viewLogs),
            settingsPage('logs', 'How long the log is kept, and whether it is also saved to files.'),
        ],
    },
    {
        id: 'system',
        group: 'Server',
        title: 'System',
        icon: 'power',
        description: 'The server\'s version, library folder and ffmpeg, what it does at start, its API, backups and the server commands.',
        subpages: [
            page('about', 'About', 'The server\'s version, its library folder, the ffmpeg it found, and its API reference.', Rights.serverSettings, Rights.apiDocs),
            page('installation', 'Installation', 'The first space\'s library folder, who can open SFE, and ffmpeg, behind the setup code.', Rights.serverSettings),
            settingsPage('startup', 'What SFE does when it starts on this computer.', 'Start-up', 'startup'),
            page('maintenance', 'Maintenance', 'A backup of the databases and configuration files, and the commands of the server\'s computer.',
                Rights.backup, Rights.shutdown, Rights.dismount),
        ],
    },
];

/**
 * Sub-pages that moved, from "section/page" to where they are now: addresses kept in bookmarks and history still open
 * them. Never remove an entry.
 */
export const ADMIN_MOVED: Readonly<Record<string, { section: string; page: string }>> = {
    'system/streams': { section: 'playback', page: 'streams' },
    'playback/settings': { section: 'playback', page: 'player' },
};

/** Where the sub-page of an address that moved is now, if it moved. */
export function movedPage(section: string | null, page: string | null): { section: string; page: string } | undefined {
    return section && page ? ADMIN_MOVED[`${section}/${page}`] : undefined;
}

/** The sections by group, in order, for the sidebar. */
export function groupsOf(sections: AdminSection[]): { title: string; sections: AdminSection[] }[] {
    return [...new Set(sections.map(s => s.group))].map(title => ({ title, sections: sections.filter(s => s.group === title) }));
}

export const ADMIN_GROUPS = groupsOf(ADMIN_SECTIONS);

/** The id of a group's heading on the overview, the breadcrumbs' link to it. */
export const groupId = (group: string) => `admin-group-${group.toLowerCase()}`;

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
