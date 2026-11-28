import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { Api } from '../../core/api.service';
import { injectWizard } from '../wizard-context';
import { ShareFolderDraft, Who } from './share-folder-draft';

/** Someone the rule can be for: a group of the space or one of its accounts. */
interface Choice {
    who: Who;
    name: string;
}

/** Who the rule is for: a group of the space (everyone in it), or one account. */
@Component({
    selector: 'sfe-share-folder-who',
    imports: [NgTemplateOutlet],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (failed(); as reason) {
            <span class="field-error" i18n>Could not list the groups and accounts: {{ reason }}</span>
        }
        <ng-template #radio let-choice>
            <label class="check">
                <input
                    type="radio"
                    name="share-folder-who"
                    [checked]="wizard.draft().who === choice.who"
                    (change)="pick(choice)"
                >
                <span>{{ choice.name }}</span>
            </label>
        </ng-template>
        <fieldset class="access-choices">
            <legend class="field-label" i18n>A group</legend>
            @for (choice of groups(); track choice.who) {
                <ng-container *ngTemplateOutlet="radio; context: { $implicit: choice }" />
            }
            <span class="field-hint" i18n>The rule decides for everyone in the group.</span>
        </fieldset>
        @if (accounts().length) {
            <fieldset class="access-choices">
                <legend class="field-label" i18n>Or one account</legend>
                @for (choice of accounts(); track choice.who) {
                    <ng-container *ngTemplateOutlet="radio; context: { $implicit: choice }" />
                }
                <span class="field-hint" i18n>An account's own rule only adds to what its groups allow: to keep a
                    folder from someone, set the rule on their groups.</span>
            </fieldset>
        }
        @if (wizard.errors()['groupId'] ?? wizard.errors()['userId']; as error) {
            <span class="field-error">{{ error }}</span>
        }
    `,
})
export class ShareFolderWhoStep implements OnInit {
    private readonly api = inject(Api);
    protected readonly wizard = injectWizard<ShareFolderDraft>();
    protected readonly groups = signal<Choice[]>([]);
    protected readonly accounts = signal<Choice[]>([]);
    protected readonly failed = signal<string | null>(null);

    async ngOnInit(): Promise<void> {
        try {
            const [groups, users] = await Promise.all([this.api.groups(), this.api.users()]);
            this.groups.set(groups.map((group) => ({ who: `g${group.id}`, name: group.name })));
            this.accounts.set(users.map((user) => ({ who: `u${user.id}`, name: user.displayName ?? user.userName })));
        } catch (error) {
            this.failed.set((error as Error).message);
        }
    }

    protected pick(choice: Choice): void {
        this.wizard.change({ who: choice.who, whoName: choice.name });
    }
}
