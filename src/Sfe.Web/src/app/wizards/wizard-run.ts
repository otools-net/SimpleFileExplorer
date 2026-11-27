import { WritableSignal } from '@angular/core';
import { ApiError } from '../core/api-error';

/** What a page made of steps shows while one of its requests runs, and when the server refuses it. */
export interface RunState {
    readonly busy: WritableSignal<boolean>;
    /** The refusal that names no field: shown above the buttons. */
    readonly error: WritableSignal<string | null>;
    /** The refusals by field (the request's names: `name`, `root`...): shown under each field. */
    readonly errors: WritableSignal<Record<string, string>>;
}

/** A refusal's messages by field, one text each; null when the error names no field. */
export function requestFieldErrors(error: unknown): Record<string, string> | null {
    return error instanceof ApiError && error.errors
        ? Object.fromEntries(Object.entries(error.errors).map(([key, messages]) => [key, messages.join(' ')]))
        : null;
}

/** What to say about an error that names no field. */
export function requestProblem(error: unknown): string {
    return error instanceof ApiError && error.status === 429
        ? $localize`Too many attempts: wait a minute, then try again.`
        : (error as Error).message;
}

/**
 * Runs a request of the setup page or a wizard: busy meanwhile, its refusals shown by their fields or above the
 * buttons. True when it went through.
 */
export async function runRequest(state: RunState, action: () => Promise<void>): Promise<boolean> {
    state.busy.set(true);
    state.error.set(null);
    state.errors.set({});
    try {
        await action();
        return true;
    } catch (error) {
        const fields = requestFieldErrors(error);
        if (fields) state.errors.set(fields);
        else state.error.set(requestProblem(error));
        return false;
    } finally {
        state.busy.set(false);
    }
}
