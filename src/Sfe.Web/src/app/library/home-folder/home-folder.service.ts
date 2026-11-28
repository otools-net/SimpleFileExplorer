import { computed, inject, Injectable, signal } from '@angular/core';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { icu } from '../../core/i18n/icu';
import { Rights } from '../../core/rights';
import { ToastService } from '../../core/toast.service';
import { HomeFolder } from '../library.models';

/**
 * The signed-in account's home folder in the library (the sidebar's Home), kept on the server: the folder it chose,
 * else its groups' default. Hidden without `account.homeFolder` (visitors without an account never have it), and in
 * a connection (homes are folders of the library).
 */
@Injectable({ providedIn: 'root' })
export class HomeFolderService {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    private sequence = 0;

    readonly enabled = computed(() => this.config.rights().has(Rights.homeFolder));
    /** Folders of the listing may become the home folder: not a connection's. */
    readonly choosable = computed(() => this.enabled() && !this.config.place());
    readonly home = signal<HomeFolder | null>(null);
    /** The path of the home folder in effect; null for none (or not loaded yet). */
    readonly path = computed(() => this.home()?.path ?? null);

    async load(): Promise<void> {
        if (!this.enabled()) return;
        await this.change(() => this.api.homeFolder(), null, 'load');
    }

    /** Makes the folder at `path` the home folder, over the groups' default. */
    set(path: string, name: string): Promise<boolean> {
        return this.change(
            () => this.api.setHomeFolder(path),
            $localize`“${name}:name:” is now your home folder.`,
            'set',
        );
    }

    /** Forgets the account's choice: the groups' default (if any) is the home folder again. */
    clear(): Promise<boolean> {
        const back = this.home()?.groupPath;
        return this.change(
            () => this.api.clearHomeFolder(),
            back != null
                ? $localize`Your home folder is your group's again.`
                : $localize`You have no home folder any more.`,
            'clear',
        );
    }

    /** Runs a request answering the home folder; only the last one sent sets it. */
    private async change(
        request: () => Promise<HomeFolder>,
        done: string | null,
        action: 'load' | 'set' | 'clear',
    ): Promise<boolean> {
        const sequence = ++this.sequence;
        try {
            const home = await request();
            if (sequence === this.sequence) this.home.set(home);
            if (done) this.toasts.success(done);
            return true;
        } catch (error) {
            this.toasts.error(notChanged(action, (error as Error).message));
            return false;
        }
    }
}

/** The error toast of a change of the home folder: what could not be done, then the server's reason. */
function notChanged(action: 'load' | 'set' | 'clear', reason: string): string {
    return icu(
        $localize`{action, select, \
load {Could not read your home folder: {reason}} \
set {Could not change your home folder: {reason}} \
other {Could not go back to your group's home folder: {reason}}}`,
        { action, reason },
    );
}
