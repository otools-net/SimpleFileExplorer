import { ComponentFixture, TestBed } from '@angular/core/testing';
import { WizardComponent, WizardStage } from '../app/wizards/wizard.component';
import { WizardDefinition } from '../app/wizards/wizard-definition';
import { buttonNamed, settle } from './fakes';

/**
 * Drives a wizard's shell (`sfe-wizard`) in a component test, for any definition: what is on screen, and the buttons
 * a person presses. Configure TestBed (`testProviders`) before `open`.
 */
export class WizardHarness {
    private constructor(
        readonly fixture: ComponentFixture<WizardComponent>,
        /** Times the wizard asked to be closed (Cancel, Done). */
        readonly closed: { count: number },
    ) {}

    static async open(definition: WizardDefinition<unknown>): Promise<WizardHarness> {
        const fixture = TestBed.createComponent(WizardComponent);
        fixture.componentRef.setInput('definition', definition);
        const closed = { count: 0 };
        fixture.componentInstance.closed.subscribe(() => closed.count++);
        fixture.autoDetectChanges();
        await settle();
        return new WizardHarness(fixture, closed);
    }

    get element(): HTMLElement {
        return this.fixture.nativeElement;
    }

    get wizard(): WizardComponent {
        return this.fixture.componentInstance;
    }

    /** The title of the step, the review or the end. */
    title(): string {
        return this.element.querySelector('.wizard-stage h2')?.textContent?.trim() ?? '';
    }

    /** "Step n of m", empty at the end. */
    progress(): string {
        return this.element.querySelector('.wizard-progress')?.textContent?.trim() ?? '';
    }

    stage(): WizardStage {
        return this.wizard.stage();
    }

    /** The titles of the steps listed (the review last). */
    steps(): string[] {
        return [...this.element.querySelectorAll('.wizard-steps li')].map((li) => li.textContent!.trim());
    }

    draft<D>(): D {
        return this.wizard.draft() as D;
    }

    /** Changes the draft as the step's fields would. */
    async fill(changes: object): Promise<void> {
        this.wizard.draft.update((draft) => ({ ...(draft as object), ...changes }));
        await settle();
    }

    /** The messages under the fields, and the one above the buttons. */
    errors(): string[] {
        return [...this.element.querySelectorAll('.wizard-stage .field-error, .wizard-stage .form-error')].map(
            (error) => error.textContent!.trim(),
        );
    }

    /** The button `label` of the actions row, if shown. */
    button(label: string): HTMLButtonElement | undefined {
        return buttonNamed(this.element.querySelector('.wizard-actions')!, label);
    }

    /** Next, or Finish on the review: the main button, as Enter in a field would. */
    async next(): Promise<void> {
        this.element.querySelector<HTMLButtonElement>('.wizard-actions [type="submit"]')!.click();
        await settle();
        await settle();
    }

    async press(label: string): Promise<void> {
        const button = this.button(label);
        const row = this.element.querySelector('.wizard-actions')!.textContent;
        if (!button) throw new Error(`No button “${label}” in: ${row}`);
        button.click();
        await settle();
    }
}
