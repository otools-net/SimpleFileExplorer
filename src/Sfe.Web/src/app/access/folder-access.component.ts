import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Api } from '../core/api.service';
import { FolderAccessReport, FolderLevel, FolderPicker, FolderRuleView, GroupInfo, UserInfo } from '../core/models';
import { ToastService } from '../core/toast.service';
import { valueOf } from '../shared/events';
import { HelpComponent } from '../shared/help.component';
import { IconComponent } from '../shared/icon.component';

/** The levels a rule sets, as the page names them. */
export const FOLDER_LEVELS: { level: FolderLevel; label: string; hint: string }[] = [
    { level: 'hidden', label: 'Hidden', hint: 'They don\'t see the folder (unless another of their groups does)' },
    { level: 'read', label: 'Read only', hint: 'They see and use its files with their rights, but may not change the files' },
    { level: 'write', label: 'Read & write', hint: 'Their rights apply in full, those that change files too' },
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
 * only adds to what its groups give.
 */
@Component({
    selector: 'sfe-folder-access',
    imports: [IconComponent, HelpComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <section class="access-card" aria-labelledby="space-folders">
            <header class="access-card-head help-row">
                <svg sfeIcon="lock"></svg>
                <h3 id="space-folders">Folder access <span class="access-count">{{ rules().length }}</span></h3>
                <sfe-help about="Folder access">
                    <p>Without rules, every group's rights apply in every folder. A <strong>rule</strong> narrows that for one folder and
                       everything below it, for a group or one account:</p>
                    <ul>
                        <li><strong>Hidden</strong>: they don't see the folder at all; it answers as if it weren't there.</li>
                        <li><strong>Read only</strong>: they see its files and use them with their rights (play, download, tag), but
                            nothing that changes the files themselves (editing pictures, deleting duplicates).</li>
                        <li><strong>Read &amp; write</strong>: their rights apply in full.</li>
                    </ul>
                    <p>The nearest rule above a folder decides: hide <em>Private</em> and open <em>Private/Shared</em> again, and
                       <em>Private</em> only shows the way to <em>Shared</em>.</p>
                    <p>Groups add up: an account sees a folder when <strong>any</strong> of its groups does. To hide a folder from the
                       kids, hide it from every group they are in. A rule for one account only adds to what its groups give.</p>
                </sfe-help>
                <span class="spacer"></span>
                <button type="button" class="button small" title="Set what a group or an account may do in a folder" (click)="startNew()">
                    <svg sfeIcon="plus"></svg>New rule
                </button>
            </header>

            @if (draft(); as draft) {
                <form class="access-form folder-rule-form" novalidate (submit)="addRule($event)">
                    <div class="field">
                        <span class="field-label">{{ repointing() ? 'Folder the rules now apply to' : 'Folder' }}</span>
                        <div class="folder-picker">
                            <p class="folder-picker-path"><svg sfeIcon="folder"></svg><span>{{ draft.path || 'Library root' }}</span></p>
                            @if (picker(); as picker) {
                                <ul>
                                    @if (picker.parent !== null) {
                                        <li><button type="button" class="link-button" title="The folder above" (click)="browse(picker.parent)">‥</button></li>
                                    }
                                    @for (folder of picker.folders; track folder.path) {
                                        <li><button type="button" class="link-button" [title]="'Choose ' + folder.path + ', or open it'" (click)="browse(folder.path)">{{ folder.name }}</button></li>
                                    } @empty {
                                        <li class="muted">No folders here.</li>
                                    }
                                </ul>
                            }
                        </div>
                        <span class="field-hint">Click a folder to choose it; click again inside it to go deeper.</span>
                    </div>
                    @if (!repointing()) {
                        <div class="access-columns">
                            <div class="field">
                                <label class="field-label" for="folder-rule-principal">For</label>
                                <select id="folder-rule-principal" (change)="changeDraft({ principal: $any(valueOf($event)) })">
                                    <optgroup label="Groups">
                                        @for (group of groups(); track group.id) {
                                            <option [value]="'g' + group.id" [selected]="draft.principal === 'g' + group.id">{{ group.builtIn === 'guests' ? group.name + ' (visitors)' : group.name }}</option>
                                        }
                                    </optgroup>
                                    <optgroup label="Accounts">
                                        @for (user of members(); track user.id) {
                                            <option [value]="'u' + user.id" [selected]="draft.principal === 'u' + user.id">{{ user.displayName || user.userName }}</option>
                                        }
                                    </optgroup>
                                </select>
                            </div>
                            <div class="field">
                                <label class="field-label" for="folder-rule-level">Level</label>
                                <select id="folder-rule-level" (change)="changeDraft({ level: $any(valueOf($event)) })">
                                    @for (option of levels; track option.level) {
                                        <option [value]="option.level" [title]="option.hint" [selected]="option.level === draft.level">{{ option.label }}</option>
                                    }
                                </select>
                            </div>
                        </div>
                    }
                    @if (problem(); as problem) {
                        <p class="form-error" role="alert">{{ problem }}</p>
                    }
                    <div class="access-row-actions">
                        <button type="button" class="button small" title="Close without changing anything" (click)="cancel()">Cancel</button>
                        <button type="submit" class="button small primary" [disabled]="busy()"
                                [title]="repointing() ? 'Move the rules of the folder that is gone to this one' : 'Set the rule'">{{ repointing() ? 'Move the rules here' : 'Set the rule' }}</button>
                    </div>
                </form>
            }

            <ul class="access-list folder-rules">
                @for (folder of folders(); track folder.path) {
                    <li class="access-row">
                        <div class="access-row-main">
                            <svg sfeIcon="folder"></svg>
                            <strong class="access-path">{{ folder.path || 'Library root' }}</strong>
                            @if (!folder.exists) {
                                <span class="access-badge danger" title="Moved, renamed or deleted outside SFE: these rules apply to nothing now">Folder not found</span>
                            }
                        </div>
                        <ul class="folder-rule-principals">
                            @for (rule of folder.rules; track rule.id) {
                                <li>
                                    <span>{{ rule.name }}<span class="field-hint">{{ rule.groupId !== null ? 'group' : 'account' }}</span></span>
                                    <select [attr.aria-label]="'Level of ' + rule.name + ' at ' + (folder.path || 'the library root')"
                                            [disabled]="busy()" (change)="changeLevel(rule, valueOf($event))">
                                        @for (option of levels; track option.level) {
                                            <option [value]="option.level" [title]="option.hint" [selected]="option.level === rule.level">{{ option.label }}</option>
                                        }
                                        <option value="" title="Remove the rule: the folder follows the folder above again">Inherit (remove)</option>
                                    </select>
                                </li>
                            }
                        </ul>
                        <div class="access-row-actions">
                            @if (folder.exists) {
                                <button type="button" class="button small" [title]="'Who may see and change ' + (folder.path || 'the library root')" (click)="toggleReport(folder.path)">
                                    {{ report()?.path === folder.path ? 'Hide who has access' : 'Who has access' }}
                                </button>
                            } @else {
                                <button type="button" class="button small" title="Point these rules to the folder's new place" (click)="startRepoint(folder.path)">Re-point…</button>
                            }
                        </div>
                        @if (report(); as report) {
                            @if (report.path === folder.path) {
                                <div class="folder-access-report">
                                    <table>
                                        <thead><tr><th scope="col">Account</th><th scope="col">Sees it</th><th scope="col">Changes its files</th></tr></thead>
                                        <tbody>
                                            @if (report.visitors; as visitors) {
                                                <tr><td>{{ visitors.name }}</td><td>{{ visitors.sees ? 'Yes' : 'No' }}</td><td>{{ visitors.changesFiles ? 'Yes' : 'No' }}</td></tr>
                                            }
                                            @for (account of report.accounts; track account.userId) {
                                                <tr><td>{{ account.name }}</td><td>{{ account.sees ? 'Yes' : 'No' }}</td><td>{{ account.changesFiles ? 'Yes' : 'No' }}</td></tr>
                                            }
                                        </tbody>
                                    </table>
                                </div>
                            }
                        }
                    </li>
                } @empty {
                    <li class="access-empty muted">No folder rules: every group's rights apply in every folder.</li>
                }
            </ul>
        </section>
    `,
})
export class FolderAccessComponent {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);

    /** The space's id for another space than the session's (server administrators), null for the session's. */
    readonly space = input<number | null>(null);
    readonly groups = input.required<GroupInfo[]>();
    /** The space's accounts. */
    readonly members = input.required<UserInfo[]>();

    protected readonly rules = signal<FolderRuleView[]>([]);
    protected readonly draft = signal<{ path: string; principal: PrincipalKey; level: FolderLevel } | null>(null);
    /** The folder (gone) whose rules the form moves; null when it adds a rule. */
    protected readonly repointing = signal<string | null>(null);
    protected readonly picker = signal<FolderPicker | null>(null);
    protected readonly report = signal<FolderAccessReport | null>(null);
    protected readonly problem = signal<string | null>(null);
    protected readonly busy = signal(false);

    protected readonly levels = FOLDER_LEVELS;
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
            this.space();
            untracked(() => void this.load());
        });
    }

    async load(): Promise<void> {
        try {
            this.rules.set(await this.api.folderRules(this.space()));
        } catch (error) {
            this.toasts.error(`Could not load the folder rules: ${(error as Error).message}`);
        }
    }

    protected startNew(): void {
        const first = this.groups().find(group => group.builtIn === 'users') ?? this.groups()[0];
        this.repointing.set(null);
        this.problem.set(null);
        this.draft.set({ path: '', principal: first ? `g${first.id}` : `u${this.members()[0]?.id ?? 0}`, level: 'hidden' });
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
        this.draft.update(draft => draft && { ...draft, ...changes });
    }

    /** Opens a folder of the picker; the folder opened is the one chosen. */
    protected async browse(path: string | null): Promise<void> {
        try {
            const picker = await this.api.spaceFolders(path, this.space());
            this.picker.set(picker);
            this.draft.update(draft => draft && { ...draft, path: picker.path ?? '' });
        } catch (error) {
            this.problem.set(`Could not list the folders: ${(error as Error).message}`);
        }
    }

    protected async addRule(event: Event): Promise<void> {
        event.preventDefault();
        const draft = this.draft();
        if (!draft)
            return;
        const from = this.repointing();
        await this.run(async () => {
            if (from !== null) {
                this.rules.set(await this.api.repointFolderRules(from, draft.path, this.space()));
                this.toasts.show(`The rules of ${from || 'the library root'} now apply to ${draft.path || 'the library root'}.`);
            } else {
                const id = Number(draft.principal.slice(1));
                this.rules.set(await this.api.setFolderRule({
                    path: draft.path,
                    groupId: draft.principal.startsWith('g') ? id : null,
                    userId: draft.principal.startsWith('u') ? id : null,
                    level: draft.level,
                }, this.space()));
                this.toasts.show(`Rule set on ${draft.path || 'the library root'}.`);
            }
            this.cancel();
        });
    }

    protected async changeLevel(rule: FolderRuleView, value: string): Promise<void> {
        const level = value === '' ? null : value as FolderLevel;
        await this.run(async () => {
            this.rules.set(await this.api.setFolderRule({ path: rule.path, groupId: rule.groupId, userId: rule.userId, level }, this.space()));
            this.toasts.show(level === null ? `${rule.name} follows the folder above again at ${rule.path || 'the library root'}.` : `Rule changed for ${rule.name}.`);
            if (this.report()?.path === rule.path)
                this.report.set(await this.api.folderAccess(rule.path, this.space()));
        });
    }

    protected async toggleReport(path: string): Promise<void> {
        if (this.report()?.path === path) {
            this.report.set(null);
            return;
        }
        try {
            this.report.set(await this.api.folderAccess(path, this.space()));
        } catch (error) {
            this.toasts.error(`Could not tell who has access: ${(error as Error).message}`);
        }
    }

    private async run(action: () => Promise<void>): Promise<void> {
        this.busy.set(true);
        this.problem.set(null);
        try {
            await action();
        } catch (error) {
            this.problem.set((error as Error).message);
            if (!this.draft())
                this.toasts.error((error as Error).message);
        } finally {
            this.busy.set(false);
        }
    }
}
