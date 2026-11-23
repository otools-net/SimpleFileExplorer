import { ChangeDetectionStrategy, Component, computed, inject, input, output, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ToastService } from '../core/toast.service';
import { HelpComponent } from '../shared/help.component';
import { IconComponent } from '../shared/icon.component';
import { GroupInfo, RightsCatalog, UserInfo } from './access.models';
import { BUILT_IN_PURPOSE } from './access-parts';
import { GroupEditorComponent } from './group-editor.component';
import { SpaceView } from './space-view';

/** The groups of a space, each a set of rights: create, change and delete them. */
@Component({
    selector: 'sfe-space-groups',
    imports: [IconComponent, HelpComponent, GroupEditorComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './space-groups.component.html',
})
export class SpaceGroupsComponent {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    protected readonly editor = viewChild.required(GroupEditorComponent);

    readonly space = input.required<SpaceView>();
    readonly groups = input.required<GroupInfo[]>();
    /** The accounts a group may have as members. */
    readonly candidates = input.required<UserInfo[]>();
    readonly catalog = input.required<RightsCatalog | null>();
    /** A group was created, changed or deleted. */
    readonly changed = output<void>();

    protected readonly tenantParam = computed(() => (this.space().current ? null : this.space().id));

    protected purpose(group: GroupInfo): string {
        return group.builtIn ? BUILT_IN_PURPOSE[group.builtIn] : '';
    }

    protected async remove(group: GroupInfo): Promise<void> {
        if (
            !confirm(
                $localize`Delete the group “${group.name}:name:”? Its members lose the rights it gave them; \
an account in no other group here loses access to “${this.space().name}:space:”. This cannot be undone.`,
            )
        )
            return;
        try {
            await this.api.deleteGroup(group.id, this.tenantParam());
            this.toasts.show($localize`Group “${group.name}:name:” deleted.`);
            this.changed.emit();
        } catch (error) {
            const reason = (error as Error).message;
            this.toasts.error($localize`Could not delete the group “${group.name}:name:”: ${reason}:reason:`);
        }
    }
}
