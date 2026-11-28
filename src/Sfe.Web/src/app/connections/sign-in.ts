/*
 * What the ways to sign in to a connection share between the add dialog (connect.dialog.ts) and the add-a-connection
 * wizard (wizards/add-connection/): their names, and the problems the sign-in routes answer.
 */

/** The words of the ways to sign in. */
export const METHOD_NAMES: Record<string, string> = {
    oAuthRedirect: $localize`:a way to sign in:Sign in at the provider and come back here`,
    oAuthCode: $localize`:a way to sign in:Sign in at the provider and copy the code it shows`,
    password: $localize`:a way to sign in:Name and password`,
    key: $localize`:a way to sign in:Name and key`,
};

/** A 409 resolves with its problem (`Api`): its words, or null for an answer that is not one. */
export function problemOf(result: unknown): string | null {
    if (!result || typeof result !== 'object' || !('status' in result) || !('title' in result)) return null;
    const problem = result as { detail?: string; title?: string };
    return problem.detail ?? problem.title ?? $localize`The server refused.`;
}
