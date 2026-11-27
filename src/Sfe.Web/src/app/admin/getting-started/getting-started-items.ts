import type { ServerMode } from '../../core/access.models';

/*
 * The items of the getting-started checklist, in order. The server says which ones the session sees (by the right of
 * each page) and which are done (`api/access/getting-started`, Sfe.Core/Access/GettingStarted.cs); this says what
 * each is called and where it leads. An item names its wizard by its registry id (`wizards/wizard-registry.ts`): while
 * no wizard of that id is registered, or the session may not open it, the item opens its page instead.
 */

export interface GettingStartedEntry {
    /** The server's id of the item (`GettingStartedItems`). */
    id: string;
    title: string;
    /** What to do, in a sentence. */
    text: string;
    icon: string;
    /** The administration page doing it: `admin?section=…&page=…`. */
    home: { section: string; page?: string };
    /** The wizard doing it, once one is registered. */
    wizard?: string;
    /** Only in this server mode (agents/docs/features/server-modes.md). */
    onlyIn?: ServerMode;
}

export const GETTING_STARTED: readonly GettingStartedEntry[] = [
    {
        id: 'library',
        title: $localize`:getting started:Library folder`,
        text: $localize`The folder of the server whose files SFE shows.`,
        icon: 'folder',
        home: { section: 'system', page: 'installation' },
    },
    {
        id: 'groups',
        title: $localize`:getting started:A group and its rights`,
        text: $localize`A group for people who may do the same things, and what they may do.`,
        icon: 'group',
        home: { section: 'access', page: 'groups' },
    },
    {
        id: 'folders',
        title: $localize`:getting started:Folder access`,
        text: $localize`Folders a group may only read, or doesn't see at all.`,
        icon: 'lock',
        home: { section: 'access', page: 'folders' },
        wizard: 'share-folder',
    },
    {
        id: 'accounts',
        title: $localize`:getting started:An account`,
        text: $localize`An account for each person who will use SFE, in their group.`,
        icon: 'user',
        home: { section: 'access', page: 'accounts' },
        onlyIn: 'external',
    },
    {
        id: 'connection',
        title: $localize`:getting started:A connection`,
        text: $localize`Storage elsewhere (Dropbox), browsed like the library.`,
        icon: 'globe',
        home: { section: 'connections', page: 'connections' },
        wizard: 'add-connection',
    },
];
