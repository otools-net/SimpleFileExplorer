import { ChangeDetectionStrategy, Component, input, OnInit, output, signal } from '@angular/core';
import { FolderPicker } from '../core/models';
import { IconComponent } from './icon.component';

/** Lists a folder of the server: `null` for the top (the allowed roots, or the drives and usual places). */
export type FolderSource = (path: string | null) => Promise<FolderPicker>;

/**
 * Picks a folder of the server by clicking through it: a folder opened is the one chosen (`picked`), ‥ goes up. Used
 * for a new space's folder (inside the allowed roots) and on the installation page (anywhere on the server's disks).
 */
@Component({
    selector: 'sfe-folder-picker',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'folder-picker' },
    template: `
        <p class="folder-picker-path">
            <svg sfeIcon="folder"></svg><span>{{ chosen() || placeholder() }}</span>
        </p>
        @if (listing(); as listing) {
            <ul>
                @if (listing.path) {
                    <li><button type="button" class="link-button" title="The folder above" (click)="browse(listing.parent)">‥</button></li>
                }
                @for (place of listing.places ?? []; track place.path) {
                    <li class="folder-picker-place">
                        <button type="button" class="link-button" [title]="'Use ' + place.path + ', or open it'" (click)="browse(place.path)">{{ place.name }}</button>
                    </li>
                }
                @for (folder of listing.folders; track folder.path) {
                    <li>
                        <button type="button" class="link-button" [title]="'Use ' + folder.path + ', or open it'" (click)="browse(folder.path)">{{ folder.name }}</button>
                    </li>
                } @empty {
                    <li class="muted">No folders here.</li>
                }
                @if (listing.truncated) {
                    <li class="muted">Only the first folders are listed.</li>
                }
            </ul>
        }
    `,
})
export class FolderPickerComponent implements OnInit {
    readonly source = input.required<FolderSource>();
    /** The folder chosen so far (shown until another is opened). */
    readonly initial = input<string | null>(null);
    readonly placeholder = input('Pick a folder below');

    /** A folder was opened: it is the one chosen. */
    readonly picked = output<string>();
    /** The folders could not be listed. */
    readonly failed = output<string>();

    protected readonly listing = signal<FolderPicker | null>(null);
    protected readonly chosen = signal<string | null>(null);

    ngOnInit(): Promise<void> {
        this.chosen.set(this.initial());
        return this.browse(null);
    }

    protected async browse(path: string | null): Promise<void> {
        try {
            const listing = await this.source()(path);
            this.listing.set(listing);
            if (listing.path) {
                this.chosen.set(listing.path);
                this.picked.emit(listing.path);
            }
        } catch (error) {
            this.failed.emit((error as Error).message);
        }
    }
}
