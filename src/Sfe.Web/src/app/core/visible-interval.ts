/**
 * Calls `tick` every `ms` while the page is visible, and not at all while it is hidden: a phone keeps a tab in the
 * background for hours, and each wake-up (a request above all) costs battery. Shown again, `tick` runs at once, then
 * at its pace. Returns the way to stop.
 */
export function visibleInterval(tick: () => void, ms: number): () => void {
    let timer: ReturnType<typeof setInterval> | undefined;
    const schedule = () => {
        clearInterval(timer);
        timer = document.hidden ? undefined : setInterval(tick, ms);
    };
    const changed = () => {
        if (!document.hidden)
            tick();
        schedule();
    };
    document.addEventListener('visibilitychange', changed);
    schedule();
    return () => {
        clearInterval(timer);
        document.removeEventListener('visibilitychange', changed);
    };
}
