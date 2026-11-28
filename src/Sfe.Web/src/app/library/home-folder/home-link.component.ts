import { ChangeDetectionStrategy, Component, computed, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MenuEntry, MenuTarget, ContextMenuDirective } from '../../menus/context-menu';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { FolderMenu } from '../folder-menu';
import { ListingStore } from '../listing.store';
import { SidebarService } from '../sidebar/sidebar.service';
import { HomeFolderService } from './home-folder.service';

/**
 * The sidebar's Home: the account's home folder in the library, above the quick links; shown only when it has one.
 * A home folder not there (not made yet, moved, deleted or hidden) stays shown, faded: SFE never makes it.
 */
@Component({
    selector: 'section[sfeHomeLink]',
    imports: [IconComponent, RouterLink, ContextMenuDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'sidebar-section home-link',
        '[attr.aria-label]': 'label',
        '[hidden]': 'path() === null',
    },
    template: `
        @let home = homes.home();
        @if (home && home.path !== null) {
            <ul class="quick-link-list">
                <li
                    class="quick-link"
                    [class.missing]="!home.exists"
                    [class.current]="current()"
                    data-home-folder
                    [sfeContextMenu]="menuTarget()"
                >
                    <a
                        class="quick-link-target"
                        routerLink="/"
                        [queryParams]="store.libraryParams(home.path)"
                        draggable="false"
                        [attr.aria-current]="current() ? 'page' : null"
                        [title]="hint()"
                        (click)="open($event)"
                    >
                        <svg sfeIcon="home"></svg><span class="quick-link-name" i18n>Home</span>
                    </a>
                </li>
            </ul>
        }
    `,
})
export class HomeLinkComponent implements OnInit {
    protected readonly homes = inject(HomeFolderService);
    protected readonly store = inject(ListingStore);
    private readonly sidebar = inject(SidebarService);
    private readonly toasts = inject(ToastService);
    private readonly folders = inject(FolderMenu);

    protected readonly label = $localize`Home folder`;
    protected readonly path = computed(() => (this.homes.choosable() ? this.homes.path() : null));
    protected readonly current = computed(() => {
        const route = this.store.route();
        return !route.q && route.path === this.path();
    });

    ngOnInit(): void {
        void this.homes.load();
    }

    protected hint(): string {
        const home = this.homes.home()!;
        const where = home.path ? `/${home.path}` : $localize`${home.name}:name: (the top of the library)`;
        if (!home.exists)
            return $localize`Your home folder, ${where}:place:, is not there: not made yet, moved or deleted.`;
        return home.chosen
            ? $localize`Open your home folder, ${where}:place:.`
            : $localize`Open your home folder, ${where}:place: (given by the group “${home.group}:group:”).`;
    }

    protected open(event: MouseEvent): void {
        if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const home = this.homes.home()!;
        if (!home.exists) {
            event.preventDefault();
            this.toasts.warning(
                $localize`Your home folder “${home.name}:name:” is not there: not made yet, moved or deleted.`,
            );
            return;
        }
        this.sidebar.closeIfOverlay();
    }

    /** The folder's own entries (when it is there), then going back to the group's home folder. */
    protected menuTarget(): MenuTarget {
        const home = this.homes.home()!;
        return {
            kind: 'folderLink',
            key: 'homeFolder',
            name: home?.name ?? '',
            entries: () => [...(home.exists ? this.folderEntries(home.path!) : []), ...this.choiceEntries()],
        };
    }

    private folderEntries(path: string): MenuEntry[] {
        return this.folders
            .linkEntries(path)
            .filter((entry) => entry.id !== 'homeFolder')
            .map((entry) =>
                entry.id === 'open'
                    ? {
                          ...entry,
                          run: () => {
                              this.sidebar.closeIfOverlay();
                              entry.run?.();
                          },
                      }
                    : entry,
            );
    }

    private choiceEntries(): MenuEntry[] {
        const home = this.homes.home()!;
        if (!home.chosen) return [];
        return [
            {
                id: 'clearHomeFolder',
                label:
                    home.groupPath !== null
                        ? $localize`Use your group's home folder`
                        : $localize`Stop using a home folder`,
                icon: 'home',
                title:
                    home.groupPath !== null
                        ? $localize`Make your home folder the one the group “${home.group}:group:” gives, \
/${home.groupPath}:place:, again.`
                        : $localize`Forget your home folder: the sidebar shows no Home until you choose one again.`,
                run: () => void this.homes.clear(),
                group: 'organise',
            },
        ];
    }
}
