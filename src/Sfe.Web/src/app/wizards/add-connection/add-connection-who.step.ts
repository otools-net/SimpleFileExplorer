import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { Api } from '../../core/api.service';
import { ConnectionLevelName } from '../../core/config.models';
import { ConfigService } from '../../core/config.service';
import { LEVEL_NAMES, NamedItem } from '../../core/connection.models';
import { Rights } from '../../core/rights';
import { valueOf } from '../../shared/events';
import { injectWizard } from '../wizard-context';
import { AddConnectionDraft, GROUP_USES, GroupUse } from './add-connection-draft';

/** Who may make a connection of each level. */
const LEVEL_RIGHTS: [ConnectionLevelName, string][] = [
    ['account', Rights.ownConnections],
    ['library', Rights.libraryConnections],
    ['server', Rights.serverConnections],
];

/** Who it is for: one's own account, this space or the server; then which of the space's groups use it, how far. */
@Component({
    selector: 'sfe-add-connection-who',
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './add-connection-who.step.html',
})
export class AddConnectionWhoStep implements OnInit {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    protected readonly wizard = injectWizard<AddConnectionDraft>();
    protected readonly groups = signal<NamedItem[]>([]);
    protected readonly failed = signal<string | null>(null);
    protected readonly levelNames = LEVEL_NAMES;
    protected readonly levelHints: Record<ConnectionLevelName, string> = {
        account: $localize`Your own storage: nobody else sees it, administrators neither.`,
        library: $localize`A storage of this space, for the groups you choose.`,
        server: $localize`A storage of the whole server, offered to spaces; here, for the groups you choose.`,
    };
    protected readonly uses: GroupUse[] = ['read', 'write'];
    protected readonly useNames = GROUP_USES;
    protected readonly value = valueOf;

    /** The levels the server allows and the session may make. */
    protected readonly levels = computed(() => {
        const allowed = this.config.config()?.connections?.levels ?? [];
        return LEVEL_RIGHTS.filter(([level, right]) => allowed.includes(level) && this.config.can(right)).map(
            ([level]) => level,
        );
    });
    /** Groups are chosen for a space's or the server's connection, by whoever may give them one. */
    protected readonly shared = computed(() => {
        const level = this.wizard.draft().level;
        return (level === 'library' || level === 'server') && this.config.can(Rights.libraryConnections);
    });

    async ngOnInit(): Promise<void> {
        if (!this.config.can(Rights.libraryConnections)) return;
        try {
            this.groups.set((await this.api.connections()).groups);
        } catch (error) {
            this.failed.set((error as Error).message);
        }
    }

    protected use(group: NamedItem, use: string): void {
        const { groups, groupNames } = this.wizard.draft();
        const next = { ...groups };
        if (use === 'read' || use === 'write') next[group.id] = use;
        else delete next[group.id];
        this.wizard.change({ groups: next, groupNames: { ...groupNames, [group.id]: group.name } });
    }
}
