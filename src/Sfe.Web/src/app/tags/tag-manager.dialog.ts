import { ChangeDetectionStrategy, Component, viewChild } from '@angular/core';
import { registerDialog } from '../library/dialogs.service';
import { SheetComponent } from '../shared/sheet.component';
import { TagListComponent } from './tag-list.component';

/**
 * The tag list in a dialog, for the listing page (its sidebar and the tag dialogs open it) without leaving it. The
 * administration page shows the same list in its Tags section.
 */
@Component({
    selector: 'sfe-tag-manager',
    imports: [SheetComponent, TagListComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet heading="Tags" [wide]="true">
            <sfe-tag-list />
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button" title="Close (Esc)" (click)="sheet.close()">Close</button>
        </sfe-sheet>
    `,
})
export class TagManagerDialog {
    private readonly sheet = viewChild.required<SheetComponent>('sheet');
    private readonly list = viewChild.required(TagListComponent);

    constructor() {
        registerDialog(dialogs => dialogs.tagManager, this);
    }

    open(): void {
        this.sheet().open();
        this.list().focus();
        void this.list().load();
    }
}
