import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { readPreference, writePreference } from '../../core/preferences';
import { IconComponent } from '../../shared/icon.component';
import { GettingStartedView } from '../admin.models';
import { gettingStartedLines } from './getting-started-lines';

const KEY = 'sfe.gettingStarted.hidden';

/**
 * The getting-started checklist on the administration page's overview (on phones, above the list of sections): the
 * first things to set up in the space, each ticked once done (the server says which), each opening its wizard or its
 * page. Hidden by its ×, per browser, account and space; a link takes its place while something is left to do.
 */
@Component({
    selector: 'sfe-getting-started',
    imports: [IconComponent, RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './getting-started.component.html',
})
export class GettingStartedComponent {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);

    /** The history state the administration page's links carry. */
    readonly state = input<Record<string, unknown>>({});
    /** Its page links replace the address (the overview's do; the phones' list of sections pushes one). */
    readonly replaceUrl = input(true);

    private readonly view = signal<GettingStartedView | null>(null);
    protected readonly lines = computed(() =>
        gettingStartedLines(this.view(), (right) => this.config.can(right), this.config.config()?.mode),
    );
    protected readonly doneCount = computed(() => this.lines().filter((line) => line.done).length);
    protected readonly allDone = computed(() => this.doneCount() === this.lines().length);
    private readonly key = computed(() => {
        const config = this.config.config();
        return `${KEY}.${config?.tenant?.id ?? 0}.${config?.user?.id ?? 0}`;
    });
    protected readonly hidden = linkedSignal(() => readPreference(this.key()) === '1');

    constructor() {
        // Optional: a refusal or a server not answering leaves the overview as it was.
        this.api.gettingStarted().then(
            (view) => this.view.set(view),
            () => undefined,
        );
    }

    protected hide(hidden: boolean): void {
        writePreference(this.key(), hidden ? '1' : '0');
        this.hidden.set(hidden);
    }
}
