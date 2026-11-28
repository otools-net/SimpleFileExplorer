/*
 * A wizard's draft kept in the tab while its Finish has sent the browser to another site (a provider's sign-in page)
 * that sends it back to the wizard (`WizardResume`). `sessionStorage` only: this tab, gone when it closes; the
 * definition leaves secrets out before (`WizardResume.keep`). Taken once: a reload after the return starts again.
 */

/** Older than this, a kept draft is left: the sign-in it waited for has ended (10 minutes on the server). */
export const KEPT_LIFETIME_MS = 15 * 60 * 1000;

const PREFIX = 'sfe.wizard.';

interface Kept {
    draft: unknown;
    at: number;
}

/** Keeps `draft` of wizard `id` until the browser comes back. */
export function keepDraft(id: string, draft: unknown): void {
    try {
        sessionStorage.setItem(PREFIX + id, JSON.stringify({ draft, at: Date.now() } satisfies Kept));
    } catch {
        // Storage refused (private window, full): the return starts the wizard again.
    }
}

/** The draft of wizard `id` kept before leaving, removed; null when there is none, or too old. */
export function takeDraft(id: string): unknown {
    try {
        const text = sessionStorage.getItem(PREFIX + id);
        sessionStorage.removeItem(PREFIX + id);
        if (!text) return null;
        const kept = JSON.parse(text) as Kept;
        return Date.now() - kept.at < KEPT_LIFETIME_MS ? kept.draft : null;
    } catch {
        return null;
    }
}
