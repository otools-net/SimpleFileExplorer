import { HttpErrorResponse } from '@angular/common/http';

/** A failed request: the message of the problem response, and its validation messages by field. */
export class ApiError extends Error {
    /** @param type The problem's `type` (`setup-required`, `tenant-changed`...), when it has one. */
    constructor(
        readonly status: number,
        message: string,
        readonly errors: Record<string, string[]> | null = null,
        readonly type: string | null = null,
    ) {
        super(message);
    }
}

/** The `type` of a problem response (`setup-required`, `tenant-changed`...). */
export function problemType(error: unknown): string | null {
    return error instanceof HttpErrorResponse &&
        error.error &&
        typeof error.error === 'object' &&
        typeof error.error.type === 'string'
        ? error.error.type
        : null;
}

/** A failed response as an `ApiError`: the problem's words (a validation message first), its status and `type`. */
export function toApiError(error: unknown): ApiError {
    if (!(error instanceof HttpErrorResponse))
        return new ApiError(0, error instanceof Error ? error.message : String(error));
    const body =
        error.error && typeof error.error === 'object'
            ? (error.error as { errors?: Record<string, string[]>; detail?: string; title?: string })
            : null;
    // Validation problems carry the useful message in `errors`, the title is generic.
    const validation = body?.errors ? Object.values(body.errors).flat()[0] : null;
    const message =
        validation ||
        body?.detail ||
        body?.title ||
        (error.status ? `${error.status} ${error.statusText}` : 'The server is not reachable');
    return new ApiError(error.status, message, body?.errors ?? null, problemType(error));
}

/**
 * The request was refused for want of a session, of the setup or of the update, or by a Local server to another
 * computer: the Api has opened the sign-in, setup, update or Local mode page.
 */
export function redirected(error: unknown): boolean {
    return (
        error instanceof ApiError &&
        (error.status === 401 ||
            (error.status === 503 && (error.type === 'setup-required' || error.type === 'migration-required')) ||
            (error.status === 403 && error.type === 'local-only'))
    );
}
