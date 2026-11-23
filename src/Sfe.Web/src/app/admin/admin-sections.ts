/*
 * The sections of the administration page (`admin?section=&page=`), listed in its sidebar under their `group`, each
 * with its sub-pages under it (on phones, as chips). A sub-page with `settings` shows that tab of settings-schema.ts
 * (each tab belongs to exactly one sub-page); the others show the section's tools. A sub-page shows to those with one
 * of its `rights` (/api/config); a section with none of its sub-pages left is not listed. My account comes first:
 * everyone has it. `/admin` without a section is the overview of the sections (on phones, their list).
 */

import { Right, Rights, SETTINGS_RIGHTS } from '../core/rights';
import { ALL_TOKENS_PAGE, MY_TOKENS_PAGE, TOKEN_SETTINGS_PAGE } from '../tokens/token-pages';

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

/** The groups of sections in the sidebar, by key (the key makes the overview's anchors: never translated). */
export const ADMIN_GROUP_TITLES = {
    account: $localize`:administration group:Account`,
    viewing: $localize`:administration group:Viewing`,
    library: $localize`:administration group:Library`,
    media: $localize`:administration group:Media`,
    access: $localize`:administration group:Access`,
    server: $localize`:administration group:Server`,
} as const;

export type AdminGroup = keyof typeof ADMIN_GROUP_TITLES;

export interface AdminSection {
    id: string;
    group: AdminGroup;
    title: string;
    icon: string;
    description: string;
    /** The first one opens by default; with a single one, nothing is listed under the section. */
    subpages: AdminSubpage[];
}

/** The settings of a tab: those with its section's right in the library, and server administrators. */
const settingsPage = (
    settings: string,
    description?: string,
    title = $localize`:administration page:Settings`,
    id = 'settings',
): AdminSubpage => ({
    id,
    title,
    description,
    settings,
    rights: [SETTINGS_RIGHTS[settings], Rights.serverSettings],
});
const page = (id: string, title: string, description: string, ...rights: Right[]): AdminSubpage => ({
    id,
    title,
    description,
    rights,
});

export const ADMIN_SECTIONS: AdminSection[] = [
    {
        id: 'account',
        group: 'account',
        title: $localize`My account`,
        icon: 'user',
        description: $localize`Your own settings, kept by the server for every device you sign in on, and your \
password.`,
        subpages: [
            page(
                'preferences',
                $localize`:administration page:Settings`,
                $localize`Your own settings, used instead of the space's, on every device you sign in on.`,
                Rights.ownPreferences,
            ),
            page('password', $localize`Password`, $localize`The password you sign in with.`, Rights.ownPassword),
            page(
                'connections',
                $localize`Connections`,
                $localize`Your own storage elsewhere (Dropbox), seen only by you.`,
                Rights.ownConnections,
            ),
            MY_TOKENS_PAGE,
        ],
    },
    {
        id: 'interface',
        group: 'viewing',
        title: $localize`Interface`,
        icon: 'theme',
        description: $localize`Colours, listings and the search box: the defaults of the space and of the server.`,
        subpages: [
            settingsPage(
                'appearance',
                $localize`The default theme, this browser's own theme, and how text files read.`,
                $localize`Appearance`,
                'appearance',
            ),
            settingsPage(
                'browsing',
                $localize`How many items a listing loads, the pictures on folders, and the resolutions and durations \
shown.`,
                $localize`Browsing`,
                'browsing',
            ),
            settingsPage(
                'search',
                $localize`How many results a search loads, how it's put into words as you type, and which searches \
are remembered.`,
                $localize`Search`,
                'search',
            ),
        ],
    },
    {
        id: 'playback',
        group: 'viewing',
        title: $localize`Playback`,
        icon: 'play',
        description: $localize`The video player, an app to open videos in, and how videos reach the browser: from a \
data server, or converted by this one.`,
        subpages: [
            settingsPage(
                'player',
                $localize`The player's jumps and loop, and the app the viewer's ▶ button opens videos and audio in.`,
                $localize`Player`,
                'player',
            ),
            settingsPage(
                'streaming',
                $localize`Where files are streamed from, and how the server converts the videos a browser can't play.`,
                $localize`Streaming`,
                'streaming',
            ),
            page(
                'streams',
                $localize`Video streams`,
                $localize`The videos the server is converting now, in every space, and who is watching them.`,
                Rights.viewStreams,
            ),
        ],
    },
    {
        id: 'scanning',
        group: 'library',
        title: $localize`Scanning`,
        icon: 'scan',
        description: $localize`What scans leave out, which files are photos, videos and audio, and what scans read \
from them.`,
        subpages: [
            settingsPage(
                'skipped',
                $localize`The folders and files scans leave out, and hidden items.`,
                $localize`What to scan`,
                'skipped',
            ),
            page(
                'exclusions',
                $localize`Excluded folders`,
                $localize`The folders kept out of scans, trickplay or thumbnails: add, change and remove them.`,
                Rights.excludeFolders,
            ),
            settingsPage(
                'fileTypes',
                $localize`Which extensions are photos, videos and audio.`,
                $localize`File types`,
                'types',
            ),
            settingsPage(
                'indexing',
                $localize`What scans read from files (fingerprints, resolutions, durations), and how many folders \
they read at once.`,
                $localize`Indexing`,
                'indexing',
            ),
        ],
    },
    {
        id: 'files',
        group: 'library',
        title: $localize`File management`,
        icon: 'folder',
        description: $localize`The trash, uploads in progress, the names new files and folders may have, and where \
SFE keeps its work files.`,
        subpages: [
            page(
                'trash',
                $localize`Trash`,
                $localize`What was moved to the trash: restore it, or delete it for good.`,
                Rights.deleteFiles,
                Rights.deletePermanently,
            ),
            page(
                'uploads',
                $localize`Uploads`,
                $localize`Everyone's unfinished uploads: how far they got, and cancelling them.`,
                Rights.fileSettings,
            ),
            settingsPage(
                'files',
                $localize`How deleting works, how long the trash keeps things, limits of copies, uploads and zips, \
names, and where SFE keeps its work files.`,
            ),
        ],
    },
    {
        id: 'keepAlive',
        group: 'library',
        title: $localize`Keep alive`,
        icon: 'refresh',
        description: $localize`Stops the file server from dropping the library folder's connection while nobody uses \
it.`,
        subpages: [
            page(
                'status',
                $localize`Status`,
                $localize`Whether the library folder is on a network share and what the last pings found; ping it \
now.`,
                Rights.keepAliveSettings,
            ),
            settingsPage(
                'keepAlive',
                $localize`Whether the library folder is kept alive, how often, and how long a ping may take.`,
            ),
        ],
    },
    {
        id: 'tags',
        group: 'library',
        title: $localize`Tags`,
        icon: 'tag',
        description: $localize`Create, edit and delete tags; the tag sidebar and the tag history.`,
        subpages: [
            page('list', $localize`All tags`, $localize`Create, edit and delete the space's tags.`, Rights.manageTags),
            settingsPage('tags', $localize`How many tags the sidebar shows, and the history of the tags given.`),
        ],
    },
    {
        id: 'tracking',
        group: 'library',
        title: $localize`Duplicates & moves`,
        icon: 'copy',
        description: $localize`Identical files, similar videos, and tagged files that were moved or renamed.`,
        subpages: [
            page(
                'duplicates',
                $localize`Duplicates`,
                $localize`Groups of identical files, and removing the extra copies.`,
                Rights.duplicates,
            ),
            page(
                'similar',
                $localize`Similar videos`,
                $localize`Videos showing the same pictures as other files: another resolution, quality or format, or \
trimmed.`,
                Rights.similarVideos,
            ),
            page(
                'missing',
                $localize`Moved files`,
                $localize`Tagged files that are no longer where they were, and where they went.`,
                Rights.missingFiles,
            ),
            settingsPage(
                'tracking',
                $localize`The smallest duplicates, how alike similar videos must be, and how moved files keep their \
tags.`,
            ),
        ],
    },
    {
        id: 'thumbnails',
        group: 'media',
        title: $localize`Thumbnails`,
        icon: 'image',
        description: $localize`The thumbnails of photos and videos in the listings and the viewer.`,
        subpages: [settingsPage('thumbnails')],
    },
    {
        id: 'trickplay',
        group: 'media',
        title: $localize`Trickplay`,
        icon: 'trickplay',
        description: $localize`Frames along each video, shown above the player's seek bar and when the pointer is over \
a video.`,
        subpages: [
            page(
                'activity',
                $localize`Activity`,
                $localize`What the trickplay task is working on, what is waiting, and what it did last.`,
                Rights.manageTrickplay,
            ),
            settingsPage(
                'trickplay',
                $localize`Which videos get trickplay, its frames, and how much of the server it uses.`,
            ),
        ],
    },
    {
        id: 'editor',
        group: 'media',
        title: $localize`Picture editor`,
        icon: 'crop',
        description: $localize`Where the pictures edited in the viewer are saved, and their quality.`,
        subpages: [settingsPage('editor')],
    },
    {
        id: 'access',
        group: 'access',
        title: $localize`Spaces & accounts`,
        icon: 'group',
        description: $localize`Who can sign in, which spaces they see, and what they may do in each. A space is one \
library with its own groups and accounts.`,
        subpages: [
            page(
                'spaces',
                $localize`Spaces`,
                $localize`Each space's library, and the way to its groups, accounts, folder rules and visitors.`,
                Rights.manageTenants,
                Rights.manageMembers,
            ),
            page(
                'accounts',
                $localize`Accounts`,
                $localize`Everyone who can sign in, with the spaces each one sees and their groups there.`,
                Rights.manageUsers,
                Rights.manageMembers,
            ),
            page(
                'groups',
                $localize`Groups`,
                $localize`The groups of a space, each a set of rights: what its members may do there.`,
                Rights.manageMembers,
            ),
            page(
                'folders',
                $localize`Folder access`,
                $localize`What a group or one account may do in a folder: hidden, read only, or read and write.`,
                Rights.manageMembers,
            ),
            page(
                'visitors',
                $localize`Visitors`,
                $localize`Whether people without an account may browse a space, with the rights of its Guests group.`,
                Rights.manageMembers,
            ),
            ALL_TOKENS_PAGE,
            TOKEN_SETTINGS_PAGE,
        ],
    },
    {
        id: 'connections',
        group: 'server',
        title: $localize`Connections`,
        icon: 'globe',
        description: $localize`Storage outside the libraries (Dropbox): whether the server may connect to it, and \
how.`,
        subpages: [
            page(
                'connections',
                $localize`Connections`,
                $localize`The space's and the server's connections, and who uses them.`,
                Rights.libraryConnections,
                Rights.serverConnections,
            ),
            page(
                'setup',
                $localize`Dropbox`,
                $localize`The app this server signs in to Dropbox through.`,
                Rights.serverSettings,
            ),
            settingsPage('connections', $localize`Which kinds and levels of connections this server allows.`),
        ],
    },
    {
        id: 'tasks',
        group: 'server',
        title: $localize`Background tasks`,
        icon: 'clock',
        description: $localize`What is running now, when each task runs next, the schedules that start them, and \
their logs.`,
        subpages: [
            page(
                'tasks',
                $localize`Tasks`,
                $localize`What is running now and when each task runs next; start or cancel them.`,
                Rights.viewTasks,
            ),
            page(
                'schedules',
                $localize`Schedules`,
                $localize`When the tasks start by themselves.`,
                Rights.scheduleTasks,
            ),
            page('history', $localize`History`, $localize`The tasks' past runs, with their logs.`, Rights.viewTasks),
            settingsPage('tasks', $localize`How long runs and their logs are kept.`),
        ],
    },
    {
        id: 'logs',
        group: 'server',
        title: $localize`Logs`,
        icon: 'docs',
        description: $localize`The messages, warnings and errors the server logged, searchable by text, date and \
severity, and how long they are kept.`,
        subpages: [
            page(
                'entries',
                $localize`Log`,
                $localize`The messages, warnings and errors the server logged, searchable by text, date and severity.`,
                Rights.viewLogs,
            ),
            settingsPage('logs', $localize`How long the log is kept, and whether it's also saved to files.`),
        ],
    },
    {
        id: 'system',
        group: 'server',
        title: $localize`System`,
        icon: 'power',
        description: $localize`The server's version, library folder and ffmpeg, what it does when it starts, its API \
and backups.`,
        subpages: [
            page(
                'about',
                $localize`About`,
                $localize`The server's version and mode, its library folder, the ffmpeg it found, and its API \
reference.`,
                Rights.serverSettings,
                Rights.apiDocs,
            ),
            page(
                'installation',
                $localize`Installation`,
                $localize`The first space's library folder, who can open SFE, and ffmpeg, behind the setup code.`,
                Rights.serverSettings,
            ),
            settingsPage(
                'startup',
                $localize`What SFE does when it starts on this computer.`,
                $localize`Start-up`,
                'startup',
            ),
            page(
                'maintenance',
                $localize`Maintenance`,
                $localize`A backup of the databases and configuration files.`,
                Rights.backup,
            ),
        ],
    },
];

/** Sub-pages that moved, "section/page" to where they are now: old bookmarks still open them. Never remove one. */
export const ADMIN_MOVED: Readonly<Record<string, { section: string; page: string }>> = {
    'system/streams': { section: 'playback', page: 'streams' },
    'playback/settings': { section: 'playback', page: 'player' },
};

/** Where the sub-page of an address that moved is now, if it moved. */
export function movedPage(section: string | null, page: string | null): { section: string; page: string } | undefined {
    return section && page ? ADMIN_MOVED[`${section}/${page}`] : undefined;
}

/** The sections by group, in order, for the sidebar. */
export function groupsOf(sections: AdminSection[]): { id: AdminGroup; title: string; sections: AdminSection[] }[] {
    return [...new Set(sections.map((s) => s.group))].map((id) => ({
        id,
        title: ADMIN_GROUP_TITLES[id],
        sections: sections.filter((s) => s.group === id),
    }));
}

export const ADMIN_GROUPS = groupsOf(ADMIN_SECTIONS);

/** The id of a group's heading on the overview, the breadcrumbs' link to it. */
export const groupId = (group: AdminGroup) => `admin-group-${group}`;

/** The sections and sub-pages a session sees: those with one of their rights (`canAny`). */
export function visibleSections(canAny: (rights: readonly string[]) => boolean): AdminSection[] {
    return ADMIN_SECTIONS.map((section) => ({
        ...section,
        subpages: section.subpages.filter((page) => canAny(page.rights)),
    })).filter((section) => section.subpages.length > 0);
}

/** The section with `id` among `sections` (all of them by default), the first one otherwise. */
export function findSection(id: string | null, sections: AdminSection[] = ADMIN_SECTIONS): AdminSection {
    return sections.find((section) => section.id === id) ?? sections[0];
}

/** The sub-page of `section` with `id`, its first one otherwise. */
export function findSubpage(section: AdminSection, id: string | null): AdminSubpage {
    return section.subpages.find((page) => page.id === id) ?? section.subpages[0];
}

/** The section and sub-page showing the tab `settings` of settings-schema.ts. */
export function locateSettings(settings: string): { section: AdminSection; subpage: AdminSubpage } | undefined {
    for (const section of ADMIN_SECTIONS) {
        const subpage = section.subpages.find((page) => page.settings === settings);
        if (subpage) return { section, subpage };
    }
    return undefined;
}
