import { inject } from '@angular/core';
import { ApiError } from '../../core/api-error';
import { Api } from '../../core/api.service';
import { ConnectRequest } from '../../core/connection.models';
import { LANDING_ERRORS } from '../../connections/connections-landing';
import { METHOD_NAMES, problemOf } from '../../connections/sign-in';
import { defineWizard, WizardEnd } from '../wizard-definition';
import {
    AddConnectionDraft,
    connectRequest,
    initialDraft,
    signInReady,
    usesLines,
    withoutSecrets,
} from './add-connection-draft';
import { AddConnectionKindStep } from './add-connection-kind.step';
import { AddConnectionNameStep } from './add-connection-name.step';
import { AddConnectionSignInStep } from './add-connection-sign-in.step';
import { AddConnectionWhoStep } from './add-connection-who.step';

/** A 409 answers its problem instead of throwing (`Api`): thrown here, for the shell to show. */
function refuseOn(answer: unknown): void {
    const problem = problemOf(answer);
    if (problem !== null) throw new Error(problem);
}

/**
 * The sign-in's checks on the server, nothing made (`validateOnly`): a step's Next. Before the sign-in step, a way
 * needing no browser is not tried yet (its fields are empty): the checks run as for a redirect.
 */
function check(signIn: boolean) {
    return async (draft: AddConnectionDraft): Promise<void> => {
        const request = connectRequest(draft);
        const asked: ConnectRequest =
            signIn || request.method.startsWith('oAuth')
                ? request
                : { ...request, method: 'oAuthRedirect', fields: {} };
        refuseOn(await inject(Api).authorizeConnection(asked, true));
    };
}

/** What the end shows of the connection `id` made. */
function endFor(draft: AddConnectionDraft, id: number): WizardEnd {
    const mine = draft.level === 'account';
    return {
        message: $localize`“${draft.name}:name:” is connected: it is in the sidebar, under the quick links.`,
        details: { title: $localize`Who may use it`, lines: usesLines(draft) },
        links: [
            {
                label: $localize`Open the connection`,
                title: $localize`Its files, at the folder chosen as its top`,
                route: '',
                query: { c: id },
            },
            {
                label: $localize`Open the Connections page`,
                title: $localize`Rename it, give it to more groups or spaces, sign it in again`,
                route: 'admin',
                query: mine
                    ? { section: 'account', page: 'connections' }
                    : { section: 'connections', page: 'connections' },
            },
        ],
    };
}

/**
 * A connection to a storage outside the libraries: its kind, who uses it (one's own account, or the space's groups),
 * its name and folder, then the sign-in at the provider, which is the one save: the connection and its groups are
 * made together when the provider's page sends the browser back here (`resume`), or when the code or the password
 * goes through.
 */
export const ADD_CONNECTION_WIZARD = defineWizard<AddConnectionDraft>({
    id: 'add-connection',
    title: $localize`:wizard:Add a connection`,
    helpPage: 'administration/wizards.md',
    initialDraft,
    steps: [
        {
            id: 'kind',
            title: $localize`:wizard step:Kind`,
            component: AddConnectionKindStep,
            explanation: $localize`The storage to reach: its files are browsed like the library's, beside it.`,
            helpAnchor: 'add-a-connection-kind',
            fields: ['kind'],
            valid: (draft) => draft.kind !== '',
            review: (draft) => [{ label: $localize`Kind`, value: draft.kindName }],
        },
        {
            id: 'who',
            title: $localize`:wizard step:Who uses it`,
            component: AddConnectionWhoStep,
            explanation: $localize`Only you: your own storage, nobody else opens it. This space or the server: the \
groups you choose use it, to read only or to change its files too.`,
            helpAnchor: 'add-a-connection-who-uses-it',
            fields: ['level', 'groups', 'ownerId', 'tenantId'],
            valid: (draft) => draft.level !== null,
            check: check(false),
            review: usesLines,
        },
        {
            id: 'name',
            title: $localize`:wizard step:Name and folder`,
            component: AddConnectionNameStep,
            explanation: $localize`The name people see it by, and the folder of the storage shown as its top (all of \
it when left empty).`,
            helpAnchor: 'add-a-connection-name-and-folder',
            fields: ['name', 'rootPath'],
            valid: (draft) => draft.name.trim() !== '',
            check: check(false),
            review: (draft) => [
                { label: $localize`Name`, value: draft.name },
                { label: $localize`Folder`, value: draft.rootPath || $localize`All of it` },
            ],
        },
        {
            id: 'sign-in',
            title: $localize`:wizard step:Sign in`,
            component: AddConnectionSignInStep,
            explanation: $localize`SFE signs in to the storage on your behalf and keeps that sign-in, protected, on \
the server: never your password at the provider.`,
            helpAnchor: 'add-a-connection-sign-in',
            fields: ['method', 'name', 'password', 'key', 'address'],
            valid: signInReady,
            check: check(true),
            review: (draft) => [{ label: $localize`How`, value: METHOD_NAMES[draft.method ?? ''] ?? '' }],
        },
    ],
    async finish(draft) {
        const api = inject(Api);
        if (draft.method === 'oAuthCode') {
            const done = await ended(() => api.completeConnection(draft.state, { code: draft.code.trim() }));
            refuseOn(done);
            return endFor(draft, done.connection.id);
        }
        const begun = await api.authorizeConnection(connectRequest(draft));
        refuseOn(begun);
        if (begun.connection) return endFor(draft, begun.connection.id);
        return { redirect: begun.address ?? '' };
    },
    resume: {
        keep: withoutSecrets,
        async run(draft, query) {
            const made = Number(query['made']);
            if (made) return endFor(draft, made);
            throw new Error(LANDING_ERRORS[query['error'] ?? ''] ?? LANDING_ERRORS['failed']);
        },
    },
});

/** A code-flow sign-in gone (too late, used): said as the landing page says it. */
async function ended<T>(request: () => Promise<T>): Promise<T> {
    try {
        return await request();
    } catch (error) {
        if (error instanceof ApiError && error.status === 404) throw new Error(LANDING_ERRORS['expired']);
        throw error;
    }
}
