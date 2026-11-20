import { NgTemplateOutlet } from '@angular/common';
import {
    afterNextRender,
    ChangeDetectionStrategy,
    Component,
    computed,
    effect,
    inject,
    Injector,
    input,
    output,
    untracked,
    viewChild,
} from '@angular/core';
import { ThemeChoice, ThemeService } from '../core/theme.service';
import { checkedOf, valueOf } from '../shared/events';
import { HelpComponent } from '../shared/help.component';
import { IconComponent } from '../shared/icon.component';
import { TrickplayStatusComponent } from '../trickplay/trickplay-status.component';
import { isSimpleGroup, settingFieldId, SettingsForm } from './settings-form.service';
import { SettingField, TABS } from './settings-schema';

/**
 * One tab of settings-schema.ts, as a sub-page of its section of the administration page: its grouped fields,
 * bound to the page's SettingsForm. Submitting (Enter in a field) asks the page to save (`submitted`).
 */
@Component({
    selector: 'sfe-settings-panel',
    imports: [NgTemplateOutlet, HelpComponent, IconComponent, TrickplayStatusComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './settings-panel.component.html',
})
export class SettingsPanelComponent {
    /** The id of a tab of settings-schema.ts. */
    readonly tab = input.required<string>();
    /** Trickplay's "Show activity". */
    readonly activity = output<void>();
    readonly submitted = output<void>();

    protected readonly form = inject(SettingsForm);
    protected readonly theme = inject(ThemeService);
    private readonly injector = inject(Injector);
    private readonly trickplayStatus = viewChild(TrickplayStatusComponent);

    protected readonly schema = computed(() => TABS.find((tab) => tab.id === this.tab())!);
    /** In the simple view, a tab without any of its settings shows a way to the full view instead. */
    protected readonly onlyAdvanced = computed(
        () => this.form.level() !== 'user' && !this.form.showAll() && !this.schema().groups.some(isSimpleGroup),
    );
    protected readonly lockLabel = computed(() =>
        this.form.level() === 'server' ? $localize`Lock for spaces and users` : $localize`Lock for users`,
    );
    protected readonly lockHint = computed(() =>
        this.form.level() === 'server'
            ? $localize`Spaces and users can't change it: the value above applies everywhere.`
            : $localize`Users of this space can't change it for themselves.`,
    );

    protected readonly valueOf = valueOf;
    protected readonly checkedOf = checkedOf;
    protected readonly fieldIdOf = settingFieldId;

    constructor() {
        // The trickplay job's state is refreshed while it is shown (it stops itself when destroyed).
        effect(() => {
            const status = this.trickplayStatus();
            untracked(() => status?.start());
        });
    }

    protected submit(event: Event): void {
        event.preventDefault();
        this.submitted.emit();
    }

    protected chooseTheme(choice: string): void {
        this.theme.choose(choice as ThemeChoice);
    }

    protected applyPreset(field: SettingField, value: string): void {
        const focus = this.form.applyPreset(field, value);
        if (focus)
            afterNextRender(() => document.getElementById(settingFieldId(focus))?.focus(), { injector: this.injector });
    }
}
