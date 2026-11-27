import type { ServerMode } from '../core/access.models';
import { Right, Rights } from '../core/rights';
import type { WizardDefinition } from './wizard-definition';

/*
 * Every wizard, one line each: what the rest of the app reads (the `wizard?id=…` page, the administration search, the
 * getting-started checklist, menus). A wizard's code loads only when it is opened (`load`): never import a wizard's
 * folder from here or from anything eager.
 */

export interface WizardEntry {
    id: string;
    title: string;
    /** What it does, for the search results. */
    description: string;
    /** More words the administration search finds it by. */
    words: string;
    /** Who may open it. */
    right: Right;
    /** Only in this server mode (agents/docs/features/server-modes.md). */
    onlyIn?: ServerMode;
    /** The administration page it belongs to (`admin?section=…&page=…`): Cancel and the end go back there. */
    home: { section: string; page?: string };
    load: () => Promise<WizardDefinition<unknown>>;
}

export const WIZARDS: readonly WizardEntry[] = [
    {
        id: 'new-space',
        title: $localize`:wizard:New space`,
        description: $localize`Step by step: a folder of the server, a name, and whether visitors may come in.`,
        words: $localize`wizard library tenant create add folder guests visitors`,
        right: Rights.manageTenants,
        home: { section: 'access', page: 'spaces' },
        load: () => import('./new-space/new-space.wizard').then((m) => m.NEW_SPACE_WIZARD),
    },
];

/** The wizard `id`, if there is one. */
export function findWizard(id: string | null): WizardEntry | undefined {
    return WIZARDS.find((wizard) => wizard.id === id);
}

/** The session may open `wizard`: it has the right, and the server is in the mode the wizard needs. */
export function canOpenWizard(
    wizard: WizardEntry,
    can: (right: string) => boolean,
    mode: ServerMode | undefined,
): boolean {
    return can(wizard.right) && (!wizard.onlyIn || !mode || mode === wizard.onlyIn);
}
