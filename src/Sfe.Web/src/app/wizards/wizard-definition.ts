import { Type } from '@angular/core';

/*
 * What a wizard declares, and nothing else: its draft, its steps and its one save. Everything wizards share (moving
 * between steps, errors, the review, the end, the guard) is the shell's (wizard.component.ts); who may open a wizard
 * and where it is found are its line in the registry (wizard-registry.ts). How to add one:
 * agents/docs/features/wizards.md.
 */

/** A line of the review before Finish: what was chosen, under the title of its step. */
export interface WizardReviewLine {
    label: string;
    value: string;
}

/** A link of the end step, to what the wizard made: an address of the app (`admin`, `wizard`...) and its query. */
export interface WizardLink {
    label: string;
    title: string;
    route: string;
    query?: Record<string, string | number>;
}

/** What the end step shows once the wizard has saved. */
export interface WizardEnd {
    message: string;
    /** Lines under a heading, between the message and the links (who has access now...). */
    details?: { title: string; lines: WizardReviewLine[] };
    links: WizardLink[];
}

export interface WizardStep<D> {
    id: string;
    title: string;
    /** The step's fields: a component that reads and changes the draft through `WIZARD_CONTEXT`. */
    component: Type<unknown>;
    /** What the step is for, in a sentence or two (under its title, behind the "?"). */
    explanation: string;
    /** The heading of the wizard's help page that explains this step (its anchor). */
    helpAnchor: string;
    /**
     * The names of the request's fields this step fills (`root`, `name`...): a refusal naming one is shown on this
     * step, and a refused Finish comes back to it.
     */
    fields: readonly string[];
    /** The step is filled in enough to go on (Next is greyed out otherwise). */
    valid(draft: D): boolean;
    /** Left out by the answers of earlier steps (never its own): neither shown nor counted. */
    skip?(draft: D): boolean;
    /**
     * Asked on Next before going on (the save's own checks run with `validateOnly`): throws the server's refusal.
     * Runs in an injection context: `inject()` works before its first `await`.
     */
    check?(draft: D): Promise<void>;
    /** What the review shows of this step. */
    review(draft: D): WizardReviewLine[];
}

export interface WizardDefinition<D> {
    /** Its registry id: the address `wizard?id=…`. */
    id: string;
    title: string;
    /** Its page in the help (docs/), whose headings the steps' `helpAnchor` name. */
    helpPage: string;
    /** A new draft: nothing typed yet. */
    initialDraft(): D;
    steps: readonly WizardStep<D>[];
    /**
     * The one save, after the review: a single request (a route doing everything in one transaction when it writes
     * several things). Throws the server's refusal. Runs in an injection context, as `check`.
     */
    finish(draft: D): Promise<WizardEnd>;
}

/**
 * A wizard's definition, checked against its own draft's type, then handed to the registry and the shell, which don't
 * know that type.
 */
export function defineWizard<D>(definition: WizardDefinition<D>): WizardDefinition<unknown> {
    return definition as unknown as WizardDefinition<unknown>;
}
