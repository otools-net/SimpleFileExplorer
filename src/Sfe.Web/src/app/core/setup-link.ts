/** The query parameter of the link SFE opens at its first start (`http://localhost:5080/?setup=…`). */
export const SETUP_LINK_PARAMETER = 'setup';

const STORAGE_KEY = 'sfe-setup-link';

/**
 * The token of the first start's link, which lets a new install be set up without the setup code: taken from the
 * address the browser was opened with, then removed from it (the address bar, the history and a copied link no
 * longer hold it) and kept for this tab, so that a reload still has it. Null when the page was opened without it.
 */
export function takeSetupLink(view: Pick<Window, 'location' | 'history'> = window, storage: Storage | null = tabStorage()): string | null {
    const query = new URLSearchParams(view.location.search);
    const given = query.get(SETUP_LINK_PARAMETER);
    if (!given)
        return read(storage);

    query.delete(SETUP_LINK_PARAMETER);
    const rest = query.toString();
    view.history.replaceState(view.history.state, '', view.location.pathname + (rest ? `?${rest}` : '') + view.location.hash);
    write(storage, given);
    return given;
}

/** Private browsing or blocked site data: the token then lives as long as the page. */
function tabStorage(): Storage | null {
    try {
        return window.sessionStorage;
    } catch {
        return null;
    }
}

function read(storage: Storage | null): string | null {
    try {
        return storage?.getItem(STORAGE_KEY) ?? null;
    } catch {
        return null;
    }
}

function write(storage: Storage | null, token: string): void {
    try {
        storage?.setItem(STORAGE_KEY, token);
    } catch {
        // Kept in memory by the caller.
    }
}
