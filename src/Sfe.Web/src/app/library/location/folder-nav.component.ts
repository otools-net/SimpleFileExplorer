import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../../shared/icon.component';
import { ContextMenuDirective } from '../../menus/context-menu';
import { FolderMenu } from '../folder-menu';
import { ListingStore } from '../listing.store';
import { FolderLink } from './location-links.service';

/** The parent, previous and next folder buttons, in the location header and its compact bar. */
@Component({
    selector: 'nav[sfeFolderNav]',
    imports: [IconComponent, RouterLink, ContextMenuDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'folder-nav', 'aria-label': 'Nearby folders' },
    template: `
        @for (link of links(); track $index) {
            @if (link.path !== null) {
                <a class="icon-button small ghost" routerLink="/" [queryParams]="store.browseParams(link.path)" aria-disabled="false"
                   [title]="link.title" [attr.aria-label]="labels[$index]" [sfeContextMenu]="folder.linkTarget(link.path)">
                    <svg [sfeIcon]="icons[$index]"></svg>
                </a>
            } @else {
                <a class="icon-button small ghost" aria-disabled="true" [title]="link.title" [attr.aria-label]="labels[$index]">
                    <svg [sfeIcon]="icons[$index]"></svg>
                </a>
            }
        }
    `,
})
export class FolderNavComponent {
    protected readonly store = inject(ListingStore);
    protected readonly folder = inject(FolderMenu);
    readonly links = input.required<FolderLink[]>({ alias: 'sfeFolderNav' });
    protected readonly icons = ['up', 'left', 'right'];
    protected readonly labels = ['Parent folder', 'Previous folder', 'Next folder'];
}
