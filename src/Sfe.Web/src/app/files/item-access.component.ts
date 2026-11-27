import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ItemAccess } from '../core/models';
import { IconComponent } from '../shared/icon.component';
import { accessLines, placeText, ruleFolderText } from './access-texts';

/**
 * The properties sheet's Access section (agents/docs/features/item-access.md): what the session may do with an item
 * and why not, the folder a rule is set on, and what the disk (on a connection, its storage) allows.
 */
@Component({
    selector: 'sfe-item-access',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { style: 'display: contents' },
    templateUrl: './item-access.component.html',
})
export class ItemAccessComponent {
    readonly access = input.required<ItemAccess>();
    readonly folder = input.required<boolean>();
    /** The name of the top a rule's folder is told from: the library's, or the connection's. */
    readonly root = input.required<string>();
    /** On a connection, the provider's name ("Dropbox"): its storage's side instead of the disk's. */
    readonly provider = input<string | null>(null);

    protected readonly yes = $localize`:an action allowed, for screen readers:Allowed:`;
    protected readonly no = $localize`:an action refused, for screen readers:Not allowed:`;
    protected readonly lines = computed(() => accessLines(this.access(), this.folder()));
    protected readonly rule = computed(() => {
        const rule = this.access().ruleFolder;
        return rule === null || rule === undefined ? null : ruleFolderText(rule, this.root());
    });
    protected readonly sideLabel = computed(() => this.provider() ?? $localize`:the disk's side of access:Disk`);
    protected readonly place = computed(() => placeText(this.access(), this.folder(), this.provider() !== null));
}
