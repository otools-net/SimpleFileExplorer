import { inject } from '@angular/core';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { defineWizard } from '../wizard-definition';
import { NewSpaceDraft, spaceInput } from './new-space-draft';
import { NewSpaceFolderStep } from './new-space-folder.step';
import { NewSpaceNameStep } from './new-space-name.step';
import { NewSpaceVisitorsStep } from './new-space-visitors.step';

/** The space's checks on the server, nothing created (`validateOnly`): a step's Next. */
async function checkSpace(draft: NewSpaceDraft): Promise<void> {
    await inject(Api).checkTenant(spaceInput(draft));
}

/**
 * A new space: its library folder, its name, whether visitors come in. One request at the end (`POST tenants`), which
 * also makes its index; the end leads to its page, where accounts are added.
 */
export const NEW_SPACE_WIZARD = defineWizard<NewSpaceDraft>({
    id: 'new-space',
    title: $localize`:wizard:New space`,
    helpPage: 'administration/wizards.md',
    initialDraft: () => ({ root: '', name: '', slug: '', guestAccess: false }),
    steps: [
        {
            id: 'folder',
            title: $localize`:wizard step:Library folder`,
            component: NewSpaceFolderStep,
            explanation: $localize`The folder whose files the space shows, with everything below it. The space keeps \
its own index, tags and settings for it.`,
            helpAnchor: 'new-space-folder',
            fields: ['root'],
            valid: (draft) => draft.root !== '',
            check: checkSpace,
            review: (draft) => [{ label: $localize`Folder`, value: draft.root }],
        },
        {
            id: 'name',
            title: $localize`:wizard step:Name`,
            component: NewSpaceNameStep,
            explanation: $localize`What people see when they pick the space. The short name names its files on the \
server.`,
            helpAnchor: 'new-space-name',
            fields: ['name', 'slug'],
            valid: (draft) => draft.name.trim() !== '',
            check: checkSpace,
            review: (draft) => [
                { label: $localize`Name`, value: draft.name.trim() },
                { label: $localize`Short name`, value: draft.slug.trim() || $localize`Made from the name` },
            ],
        },
        {
            id: 'visitors',
            title: $localize`:wizard step:Visitors`,
            component: NewSpaceVisitorsStep,
            explanation: $localize`Visitors are people without an account. Let them in only if anyone who can reach \
the server may see this library.`,
            helpAnchor: 'new-space-visitors',
            fields: ['guestAccess'],
            valid: () => true,
            review: (draft) => [
                {
                    label: $localize`Visitors`,
                    value: draft.guestAccess
                        ? $localize`Let in, with the Guests group's rights`
                        : $localize`Not let in`,
                },
            ],
        },
    ],
    async finish(draft) {
        const api = inject(Api);
        const config = inject(ConfigService);
        const space = await api.createTenant(spaceInput(draft));
        // The ⋮ menu lists the spaces.
        await config.load();
        return {
            message: $localize`The space “${space.name}:name:” is ready, on ${space.root}:folder:. It has the groups \
Administrators, Users and Guests; add accounts to it on its page.`,
            links: [
                {
                    label: $localize`Open the space's page`,
                    title: $localize`Its groups, accounts, folder rules and visitors`,
                    route: 'admin',
                    query: { section: 'access', page: 'spaces', space: space.id },
                },
            ],
        };
    },
});
