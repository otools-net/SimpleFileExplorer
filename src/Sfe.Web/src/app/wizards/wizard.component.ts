import { NgComponentOutlet } from '@angular/common';
import {
    afterNextRender,
    ChangeDetectionStrategy,
    Component,
    computed,
    ElementRef,
    inject,
    Injector,
    input,
    OnInit,
    output,
    runInInjectionContext,
    signal,
    viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { helpLink } from '../help/help-files';
import { HelpComponent } from '../shared/help.component';
import { IconComponent } from '../shared/icon.component';
import { WIZARD_CONTEXT, WizardContext } from './wizard-context';
import { WizardDefinition, WizardEnd, WizardStep } from './wizard-definition';
import { requestFieldErrors, requestProblem } from './wizard-run';

/** Where the wizard stands: one of its steps, the review before Finish, or the end once saved. */
export type WizardStage = { kind: 'step'; id: string } | { kind: 'review' } | { kind: 'end' };

type Step = WizardStep<unknown>;

/**
 * Runs a wizard (wizard-definition.ts): its steps one after the other ("Step n of m", the steps left out by earlier
 * answers not counted), Back keeping what was typed, each step's check on Next, the review, the one save on Finish and
 * the end. A refusal is shown by its field on the step that asks for it. The draft lives here only: a reload starts
 * again (the page's guard says so), nothing is kept in the browser.
 */
@Component({
    selector: 'sfe-wizard',
    imports: [NgComponentOutlet, RouterLink, HelpComponent, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'wizard', '(window:beforeunload)': 'onBeforeUnload($event)' },
    templateUrl: './wizard.component.html',
})
export class WizardComponent implements OnInit {
    readonly definition = input.required<WizardDefinition<unknown>>();
    /** Cancel, or Done at the end: the page goes back to where the wizard belongs. */
    readonly closed = output<void>();

    private readonly injector = inject(Injector);
    private readonly title = viewChild<ElementRef<HTMLElement>>('stageTitle');

    readonly draft = signal<unknown>(null);
    readonly stage = signal<WizardStage>({ kind: 'review' });
    readonly busy = signal(false);
    readonly error = signal<string | null>(null);
    readonly errors = signal<Record<string, string>>({});
    readonly ended = signal<WizardEnd | null>(null);
    /** The draft as it started, to tell whether anything was typed. */
    private initial = '';

    /** The steps not left out by the answers so far. */
    readonly steps = computed(() => this.definition().steps.filter((step) => !step.skip?.(this.draft())));
    readonly current = computed(() => {
        const stage = this.stage();
        return stage.kind === 'step' ? (this.steps().find((step) => step.id === stage.id) ?? null) : null;
    });
    /** "Step n of m": the review counts as the last one. */
    readonly number = computed(() => {
        const stage = this.stage();
        if (stage.kind === 'end') return null;
        const index = stage.kind === 'review' ? this.steps().length : this.steps().indexOf(this.current()!);
        return { at: index + 1, of: this.steps().length + 1 };
    });
    readonly canGoOn = computed(() => {
        const step = this.current();
        return !this.busy() && (step ? step.valid(this.draft()) : this.stage().kind === 'review');
    });
    /** Something was typed and not saved yet. */
    readonly dirty = computed(() => !this.ended() && JSON.stringify(this.draft()) !== this.initial);
    readonly review = computed(() => this.steps().map((step) => ({ step, lines: step.review(this.draft()) })));

    /** What the steps' components get. */
    private readonly context: WizardContext<unknown> = {
        draft: this.draft,
        errors: this.errors.asReadonly(),
        busy: this.busy.asReadonly(),
        change: (changes) => this.draft.update((draft) => ({ ...(draft as object), ...changes })),
    };
    readonly stepInjector = Injector.create({
        providers: [{ provide: WIZARD_CONTEXT, useValue: this.context }],
        parent: this.injector,
    });

    protected readonly helpHref = (step: Step) => helpLink(this.definition().helpPage, step.helpAnchor);

    ngOnInit(): void {
        this.start();
    }

    /** A new draft, at the first step (also "Run again" at the end). */
    start(): void {
        const draft = this.definition().initialDraft();
        this.draft.set(draft);
        this.initial = JSON.stringify(draft);
        this.ended.set(null);
        this.go({ kind: 'step', id: this.steps()[0].id });
    }

    /** Next (Enter in a field too): the step's check first; Finish on the review. */
    async next(event?: Event): Promise<void> {
        event?.preventDefault();
        if (!this.canGoOn()) return;
        if (this.stage().kind === 'review') return this.finish();
        const step = this.current()!;
        if (step.check && !(await this.check(step))) return;
        const following = this.steps()[this.steps().indexOf(step) + 1];
        this.go(following ? { kind: 'step', id: following.id } : { kind: 'review' });
    }

    back(): void {
        const steps = this.steps();
        if (this.stage().kind === 'review') this.go({ kind: 'step', id: steps[steps.length - 1].id });
        else {
            const index = steps.indexOf(this.current()!);
            if (index > 0) this.go({ kind: 'step', id: steps[index - 1].id });
        }
    }

    /** Goes to a step from the review ("Change"). */
    open(step: Step): void {
        this.go({ kind: 'step', id: step.id });
    }

    /** May the page be left: nothing typed, or the person agrees to lose it. */
    canLeave(): boolean {
        return !this.dirty() || confirm($localize`Leave the wizard? What you have chosen so far is lost.`);
    }

    protected onBeforeUnload(event: BeforeUnloadEvent): void {
        if (this.dirty()) event.preventDefault();
    }

    /** The step's check: refusals of its fields (or earlier steps') keep it; those of later steps wait for them. */
    private async check(step: Step): Promise<boolean> {
        const steps = this.steps();
        const fields = new Set(steps.slice(0, steps.indexOf(step) + 1).flatMap((s) => s.fields));
        try {
            await this.request(() => step.check!(this.draft()));
            return true;
        } catch (error) {
            const refused = requestFieldErrors(error);
            if (!refused) this.error.set(requestProblem(error));
            else {
                const own = Object.entries(refused).filter(([field]) => fields.has(field));
                if (!own.length) return true;
                this.showOn(refused);
            }
            return false;
        }
    }

    /** The one save: the end, or back to the step whose field was refused. */
    private async finish(): Promise<void> {
        try {
            const end = await this.request(() => this.definition().finish(this.draft()));
            this.ended.set(end);
            this.go({ kind: 'end' });
        } catch (error) {
            const refused = requestFieldErrors(error);
            if (refused) this.showOn(refused);
            else this.error.set(requestProblem(error));
        }
    }

    /** Runs a check or the save in an injection context, busy meanwhile. */
    private async request<T>(action: () => Promise<T>): Promise<T> {
        this.busy.set(true);
        this.error.set(null);
        this.errors.set({});
        try {
            return await runInInjectionContext(this.injector, action);
        } finally {
            this.busy.set(false);
        }
    }

    /** Refusals shown on the first step asking for one of their fields; those of no step above the buttons. */
    private showOn(refused: Record<string, string>): void {
        const step = this.steps().find((s) => s.fields.some((field) => field in refused));
        if (!step) {
            this.error.set(Object.values(refused).join(' '));
            return;
        }
        if (this.current() !== step) this.go({ kind: 'step', id: step.id });
        this.errors.set(refused);
    }

    private go(stage: WizardStage): void {
        this.stage.set(stage);
        this.error.set(null);
        this.errors.set({});
        // The new step's title takes the focus, so that screen readers say where the person is.
        afterNextRender(() => this.title()?.nativeElement.focus(), { injector: this.injector });
    }
}
