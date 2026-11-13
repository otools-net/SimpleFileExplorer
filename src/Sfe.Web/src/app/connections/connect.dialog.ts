import { KeyValuePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, output, signal, viewChild } from '@angular/core';
import { Api, ApiError } from '../core/api.service';
import { ConnectionKind, ConnectionLevelName } from '../core/config.models';
import { ConfigService } from '../core/config.service';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { valueOf } from '../shared/events';
import { SheetComponent } from '../shared/sheet.component';
import { ConnectionInfo, LEVEL_NAMES, SignInBegun, SignInField, SignInKindName } from '../core/connection.models';

/** What the dialog does: make a connection at a level, or sign one in again. */
export type ConnectPurpose = { level: ConnectionLevelName } | { reconnect: ConnectionInfo };

/** The words of the ways to sign in. */
export const METHOD_NAMES: Record<string, string> = {
    oAuthRedirect: 'Sign in at the provider and come back here',
    oAuthCode: 'Sign in at the provider and copy the code it shows',
    password: 'Name and password',
    key: 'Name and key',
};

/**
 * Add a connection (or sign one in again): the kind, then a form built from what the server says the kind asks
 * (`config.connections.kinds[].signIn`), no code per provider. OAuth sends the browser to the provider's page (the
 * address comes from the server: the client names none, agents/docs/development/fully-local.md); with the code
 * flow the page opens in a new tab and the person pastes the code back here.
 */
@Component({
    selector: 'sfe-connect-dialog',
    imports: [BusyDirective, KeyValuePipe, SheetComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './connect.dialog.html',
})
export class ConnectDialog {
    private readonly config = inject(ConfigService);
    private readonly api = inject(Api);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');

    /** A connection made or signed in again. */
    readonly done = output<ConnectionInfo>();

    protected readonly purpose = signal<ConnectPurpose | null>(null);
    protected readonly kindId = signal('');
    protected readonly method = signal<SignInKindName>('oAuthRedirect');
    protected readonly name = signal('');
    protected readonly rootPath = signal('');
    protected readonly fields = signal<Record<string, string>>({});
    /** Waiting for the code the provider shows (code flow). */
    protected readonly waiting = signal<{ state: string; address: string | null; fields: SignInField[] } | null>(null);
    protected readonly busy = signal(false);
    protected readonly errors = signal<Record<string, string>>({});
    protected readonly problem = signal<string | null>(null);

    protected readonly kinds = computed(() => this.config.config()?.connections?.kinds ?? []);
    protected readonly kind = computed<ConnectionKind | null>(
        () => this.kinds().find((k) => k.kind === this.kindId()) ?? null,
    );
    protected readonly methods = computed(() => this.kind()?.signIn ?? []);
    protected readonly methodFields = computed(
        () => this.methods().find((m) => m.kind === this.method())?.fields ?? [],
    );
    protected readonly reconnecting = computed(() => {
        const purpose = this.purpose();
        return purpose && 'reconnect' in purpose ? purpose.reconnect : null;
    });
    protected readonly heading = computed(() => {
        const again = this.reconnecting();
        if (again) return `Sign “${again.name}” in again`;
        const purpose = this.purpose();
        return purpose && 'level' in purpose ? `Add a connection: ${LEVEL_NAMES[purpose.level].toLowerCase()}` : '';
    });

    protected readonly methodNames = METHOD_NAMES;
    protected readonly value = valueOf;

    open(purpose: ConnectPurpose): void {
        this.purpose.set(purpose);
        const again = 'reconnect' in purpose ? purpose.reconnect : null;
        this.kindId.set(again?.kind ?? this.kinds()[0]?.kind ?? '');
        this.method.set(this.methods()[0]?.kind as SignInKindName);
        this.name.set(again?.name ?? this.kind()?.name ?? '');
        this.rootPath.set(again?.rootPath ?? '');
        this.fields.set({});
        this.waiting.set(null);
        this.errors.set({});
        this.problem.set(null);
        this.sheet().open();
    }

    protected chooseKind(kind: string): void {
        this.kindId.set(kind);
        this.method.set(this.methods()[0]?.kind as SignInKindName);
        if (!this.name()) this.name.set(this.kind()?.name ?? '');
    }

    protected setField(id: string, value: string): void {
        this.fields.update((fields) => ({ ...fields, [id]: value }));
    }

    /** Begins the sign-in: done at once, off to the provider, or waiting for the code. */
    protected async start(): Promise<void> {
        const begun = await this.call(() => this.begin());
        if (!begun) return;
        if (begun.connection) return this.finish(begun.connection);
        if (this.method() === 'oAuthRedirect' && begun.address) return this.navigate(begun.address);
        if (begun.address) this.openTab(begun.address);
        this.fields.set({});
        this.waiting.set({ state: begun.state ?? '', address: begun.address, fields: begun.fields ?? [] });
    }

    /** Ends the code flow with what the person pasted. */
    protected async complete(): Promise<void> {
        const waiting = this.waiting();
        if (!waiting) return;
        const done = await this.call(() => this.api.completeConnection(waiting.state, this.fields()));
        if (done) this.finish(done.connection);
    }

    protected close(): void {
        this.sheet().close();
    }

    /** The browser leaves for the provider's page (the server sends it back to `connections?made=`). */
    protected navigate(address: string): void {
        window.location.assign(address);
    }

    protected openTab(address: string): void {
        window.open(address, '_blank', 'noopener');
    }

    private begin(): Promise<SignInBegun> {
        const again = this.reconnecting();
        if (again) return this.api.reauthorizeConnection(again.id, this.method(), this.fields());
        const purpose = this.purpose() as { level: ConnectionLevelName };
        return this.api.authorizeConnection({
            kind: this.kindId(),
            level: purpose.level,
            name: this.name(),
            method: this.method(),
            rootPath: this.rootPath() || undefined,
            fields: this.fields(),
        });
    }

    private finish(connection: ConnectionInfo): void {
        this.done.emit(connection);
        this.sheet().close();
    }

    /** Runs a request, showing what the server refused: the field's message, or the problem. */
    private async call<T extends object>(request: () => Promise<T>): Promise<T | null> {
        this.errors.set({});
        this.problem.set(null);
        try {
            const result = await whileBusy(this.busy, request);
            const problem = problemOf(result);
            if (problem === null) return result ?? null;
            this.problem.set(problem);
        } catch (error) {
            if (error instanceof ApiError && error.errors) this.errors.set(messagesOf(error.errors));
            else if (error instanceof ApiError && error.status === 404)
                this.problem.set('This sign-in is over (too late, or already used): start again.');
            else this.problem.set((error as Error).message);
        }
        return null;
    }
}

/** A 409 resolves with its problem (`Api`): its words, or null for an answer that is not one. */
export function problemOf(result: unknown): string | null {
    if (!result || typeof result !== 'object' || !('status' in result) || !('title' in result)) return null;
    const problem = result as { detail?: string; title?: string };
    return problem.detail ?? problem.title ?? 'The server refused.';
}

function messagesOf(errors: Record<string, string[]>): Record<string, string> {
    return Object.fromEntries(Object.entries(errors).map(([key, messages]) => [key, messages.join(' ')]));
}
