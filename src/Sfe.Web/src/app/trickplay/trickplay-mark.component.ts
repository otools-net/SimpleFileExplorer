import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { IconComponent } from '../shared/icon.component';
import { TrickplayMarksService } from './trickplay-marks.service';

const STATE_TITLES = {
    pending: 'Waiting for its trickplay',
    running: 'Creating its trickplay…',
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
