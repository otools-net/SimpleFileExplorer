import { inject, InjectionToken, Signal, WritableSignal } from '@angular/core';

/**
 * What a step's component gets from the shell: the draft it reads and changes, its fields' refusals and whether a
 * request runs. Steps don't know the shell; the shell doesn't know a step's fields.
 */
export interface WizardContext<D> {
    readonly draft: WritableSignal<D>;
    /** Refusals by field name (the request's names), from the step's check or the Finish. */
    readonly errors: Signal<Record<string, string>>;
    readonly busy: Signal<boolean>;
    /** Changes some of the draft's values. */
    change(changes: Partial<D>): void;
}

export const WIZARD_CONTEXT = new InjectionToken<WizardContext<unknown>>('WIZARD_CONTEXT');

/** A step's context, with the type of its wizard's draft. */
export function injectWizard<D>(): WizardContext<D> {
    return inject(WIZARD_CONTEXT) as WizardContext<D>;
}
