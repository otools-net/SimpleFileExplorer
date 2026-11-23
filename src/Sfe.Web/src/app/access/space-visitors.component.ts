import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { ToastService } from '../core/toast.service';
import { checkedOf } from '../shared/events';
import { HelpComponent } from '../shared/help.component';
import { IconComponent } from '../shared/icon.component';
import { SpaceView } from './space-view';

/** Whether a space lets visitors without an account in (with the rights of its Guests group). */
@Component({
    selector: 'sfe-space-visitors',
    imports: [IconComponent, HelpComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './space-visitors.component.html',
})
export class SpaceVisitorsComponent {
    private readonly api = inject(Api);
    protected readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);

    readonly space = input.required<SpaceView>();
    /** The space's guest access changed (the list of spaces shows it). */
    readonly changed = output<SpaceView>();

    protected readonly anonymousAllowed = signal(true);
    protected readonly busy = signal(false);
    protected readonly checkedOf = checkedOf;

    constructor() {
        // Whether the server lets visitors in at all: the same for every space, asked about the session's.
        this.api.tenantAccess().then(
            (access) => this.anonymousAllowed.set(access.anonymousGuestsAllowed),
            () => {},
        );
    }

    protected async setGuestAccess(open: boolean): Promise<void> {
        const space = this.space();
        this.busy.set(true);
        try {
            if (space.current) await this.api.setGuestAccess(open);
            else
                await this.api.updateTenant(space.id, {
                    name: space.name,
                    guestAccess: open,
                    disabled: space.disabled,
                    disabledRights: null,
                });
            this.changed.emit({ ...space, guestAccess: open });
            this.toasts.show(
                open
                    ? $localize`Visitors without an account may now browse “${space.name}:space:”.`
                    : $localize`Only accounts may open “${space.name}:space:” now.`,
            );
        } catch (error) {
            this.toasts.error($localize`Could not change the visitors' access: ${(error as Error).message}:reason:`);
        } finally {
            this.busy.set(false);
        }
    }
}
