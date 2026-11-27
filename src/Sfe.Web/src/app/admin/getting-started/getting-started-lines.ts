import type { Params } from '@angular/router';
import type { ServerMode } from '../../core/access.models';
import { canOpenWizard, findWizard } from '../../wizards/wizard-registry';
import { GettingStartedView } from '../admin.models';
import { GETTING_STARTED, GettingStartedEntry } from './getting-started-items';

export interface GettingStartedLine extends GettingStartedEntry {
    done: boolean;
    /** Where it leads: its wizard when one is registered and may be opened, else its page. */
    link: { path: string; params: Params };
}

/** The server's items with their names and links, those of the other server mode left out. */
export function gettingStartedLines(
    view: GettingStartedView | null,
    can: (right: string) => boolean,
    mode: ServerMode | undefined,
): GettingStartedLine[] {
    const lines: GettingStartedLine[] = [];
    for (const item of view?.items ?? []) {
        const entry = GETTING_STARTED.find((known) => known.id === item.id);
        if (!entry || (entry.onlyIn && mode && entry.onlyIn !== mode)) continue;
        const wizard = findWizard(entry.wizard ?? null);
        const link =
            wizard && canOpenWizard(wizard, can, mode)
                ? { path: '/wizard', params: { id: wizard.id } }
                : { path: '/admin', params: { ...entry.home } };
        lines.push({ ...entry, done: item.done, link });
    }
    return lines;
}
