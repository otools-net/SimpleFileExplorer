import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { IconComponent } from '../shared/icon.component';
import { TrickplayMarksService } from './trickplay-marks.service';

// Users read these: "previews along the timeline" is their word for trickplay (ui-texts glossary).
const STATE_TITLES = {
    pending: $localize`Its previews along the timeline are waiting to be made.`,
    running: $localize`Its previews along the timeline are being made…`,
};

/** The mark of a listed video waiting for its trickplay (clock) or being processed (spinner); nothing otherwise. */
@Component({
    selector: 'sfe-trickplay-mark',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'trickplay-mark', '[attr.data-state]': 'state()', '[attr.title]': 'title()' },
    template: `<span class="trickplay-spinner"></span><svg sfeIcon="clock"></svg>`,
})
export class TrickplayMarkComponent {
    readonly path = input.required<string>();
    private readonly marks = inject(TrickplayMarksService);
    protected readonly state = computed(() => this.marks.states().get(this.path()) ?? null);
    protected readonly title = computed(() => {
        const state = this.state();
        return state ? STATE_TITLES[state] : null;
    });
}
