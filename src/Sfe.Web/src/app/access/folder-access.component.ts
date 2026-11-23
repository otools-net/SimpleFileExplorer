import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Api } from '../core/api.service';
import { FolderLevel } from '../core/access.models';
import { FolderPicker } from '../core/models';
import { FolderAccessReport, FolderRuleView } from './access.models';
import { FolderRulesSource, libraryRules, RuleGroup, RuleMember } from './folder-rules-source';
import { ToastService } from '../core/toast.service';
import { valueOf } from '../shared/events';
import { HelpComponent } from '../shared/help.component';
import { IconComponent } from '../shared/icon.component';

/** The levels a rule sets, as the page names them. */
export const FOLDER_LEVELS: { level: FolderLevel; label: string; hint: string }[] = [
    {
        level: 'hidden',
        label: $localize`:folder access level:Hidden`,
        hint: $localize`They don't see the folder (unless another of their groups does)`,
    },
    {
        level: 'read',
        label: $localize`:folder access level:Read only`,
        hint: $localize`They see and use its files with their rights, but may not change the files`,
    },
    {
        level: 'write',
        label: $localize`:folder access level:Read & write`,
        hint: $localize`Their rights apply in full, those that change files too`,
    },
];

/** The rules of one folder, as listed. */
interface FolderRules {
    path: string;
    exists: boolean;
    rules: FolderRuleView[];
}

/** Who a new rule is for: `g12` (a group) or `u5` (an account). */
type PrincipalKey = `g${number}` | `u${number}`;

/**
 * The folder rules of a space: what a group, or one account, may do in a folder and below it. The nearest rule above
 * a folder decides; groups add up (an account sees a folder when one of its groups does), and an account's own rule
 * only adds to what its groups give. The rules are the space's library's, or a connection's (`source`).
 */
@Component({
    selector: 'sfe-folder-access',
    imports: [IconComponent, HelpComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './folder-access.component.html',
})
export class FolderAccessComponent {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);

    /** The space's id for another space than the session's (server administrators), null for the session's. */
    readonly space = input<number | null>(null);
    /** Where the rules are: the space's library without it. */
    readonly source = input<FolderRulesSource | null>(null);
    readonly groups = input.required<RuleGroup[]>();
    /** The space's accounts. */
    readonly members = input.required<RuleMember[]>();

    protected readonly rules = signal<FolderRuleView[]>([]);
    protected readonly draft = signal<{ path: string; principal: PrincipalKey; level: FolderLevel } | null>(null);
    /** The folder (gone) whose rules the form moves; null when it adds a rule. */
    protected readonly repointing = signal<string | null>(null);
    protected readonly picker = signal<FolderPicker | null>(null);
    protected readonly report = signal<FolderAccessReport | null>(null);
    protected readonly problem = signal<string | null>(null);
    protected readonly busy = signal(false);

    protected readonly levels = FOLDER_LEVELS;
    protected readonly rulesSource = computed(() => this.source() ?? libraryRules(this.api, this.space()));
    protected readonly rootName = computed(() => this.rulesSource().rootName);
    protected readonly moveTitle = $localize`Move the rules of the folder that is gone to this one`;
    protected readonly setTitle = $localize`Set the rule`;
    protected readonly valueOf = valueOf;

    /** The rules by folder, in path order. */
    protected readonly folders = computed<FolderRules[]>(() => {
        const byPath = new Map<string, FolderRules>();
        for (const rule of this.rules()) {
            const folder = byPath.get(rule.path) ?? { path: rule.path, exists: rule.exists, rules: [] };
            folder.rules.push(rule);
            byPath.set(rule.path, folder);
        }
        return [...byPath.values()].sort((a, b) => a.path.localeCompare(b.path));
    });

    constructor() {
        effect(() => {
            this.rulesSource();
            untracked(() => void this.load());
        });
    }

    async load(): Promise<void> {
        try {
            this.rules.set(await this.rulesSource().load());
        } catch (error) {
            this.toasts.error($localize`Could not load the folder rules: ${(error as Error).message}:reason:`);
        }
    }

    protected startNew(): void {
        const first = this.groups().find((group) => group.builtIn === 'users') ?? this.groups()[0];
        this.repointing.set(null);
        this.problem.set(null);
        this.draft.set({
            path: '',
            principal: first ? `g${first.id}` : `u${this.members()[0]?.id ?? 0}`,
            level: 'hidden',
        });
        void this.browse('');
    }

    protected startRepoint(path: string): void {
        this.repointing.set(path);
        this.problem.set(null);
        this.draft.set({ path: '', principal: 'g0', level: 'hidden' });
        void this.browse('');
    }

    protected cancel(): void {
        this.draft.set(null);
        this.repointing.set(null);
    }

    protected changeDraft(changes: Partial<{ principal: PrincipalKey; level: FolderLevel }>): void {
        this.draft.update((draft) => draft && { ...draft, ...changes });
    }

    /** Opens a folder of the picker; the folder opened is the one chosen. */
    protected async browse(path: string | null): Promise<void> {
        try {
            const picker = await this.rulesSource().folders(path);
            this.picker.set(picker);
            this.draft.update((draft) => draft && { ...draft, path: picker.path ?? '' });
        } catch (error) {
            this.problem.set($localize`Could not list the folders: ${(error as Error).message}:reason:`);
        }
    }

    protected async addRule(event: Event): Promise<void> {
        event.preventDefault();
        const draft = this.draft();
        if (!draft) return;
        const from = this.repointing();
        const source = this.rulesSource();
        await this.run(async () => {
            if (from !== null && source.repoint) {
                this.rules.set(await source.repoint(from, draft.path));
                this.toasts.show(
                    $localize`The rules of ${this.named(from)}:from: now apply to ${this.named(draft.path)}:to:.`,
                );
            } else {
                const id = Number(draft.principal.slice(1));
                this.rules.set(
                    await source.set({
                        path: draft.path,
                        groupId: draft.principal.startsWith('g') ? id : null,
                        userId: draft.principal.startsWith('u') ? id : null,
                        level: draft.level,
                    }),
                );
                this.toasts.show($localize`Rule set on ${this.named(draft.path)}:folder:.`);
            }
            this.cancel();
        });
    }

    protected async changeLevel(rule: FolderRuleView, value: string): Promise<void> {
        const level = value === '' ? null : (value as FolderLevel);
        const source = this.rulesSource();
        await this.run(async () => {
            this.rules.set(await source.set({ path: rule.path, groupId: rule.groupId, userId: rule.userId, level }));
            const folder = this.named(rule.path);
            this.toasts.show(
                level === null
                    ? $localize`“${rule.name}:name:” follows the folder above again at ${folder}:folder:.`
                    : $localize`Rule changed for “${rule.name}:name:” at ${folder}:folder:.`,
            );
            if (this.report()?.path === rule.path && source.report) this.report.set(await source.report(rule.path));
        });
    }

    protected async toggleReport(path: string): Promise<void> {
        if (this.report()?.path === path) {
            this.report.set(null);
            return;
        }
        const source = this.rulesSource();
        if (!source.report) return;
        try {
            this.report.set(await source.report(path));
        } catch (error) {
            const [folder, reason] = [this.named(path), (error as Error).message];
            this.toasts.error($localize`Could not find who has access to ${folder}:folder:: ${reason}:reason:`);
        }
    }

    protected levelLabel(name: string, path: string): string {
        return $localize`Level of ${name}:name: at ${path || this.rootName()}:folder:`;
    }

    protected yesNo(value: boolean): string {
        return value ? $localize`Yes` : $localize`No`;
    }

    /** A folder as the toasts name it, quoted: its path, or the name of the top folder. */
    private named(path: string): string {
        return $localize`:a folder quoted in a sentence:“${path || this.rootName()}:folder:”`;
    }

    private async run(action: () => Promise<void>): Promise<void> {
        this.busy.set(true);
        this.problem.set(null);
        try {
            await action();
        } catch (error) {
            const reason = (error as Error).message;
            this.problem.set(reason);
            if (!this.draft()) this.toasts.error($localize`Could not change the rule: ${reason}:reason:`);
        } finally {
            this.busy.set(false);
        }
    }
}
