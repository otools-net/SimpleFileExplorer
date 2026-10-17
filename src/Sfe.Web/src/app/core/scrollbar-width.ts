/**
 * Keeps the width of the browser's scrollbar in `--scrollbar-w` on the root (0 where scrollbars overlay the page, as
 * on most phones). A bottom sheet takes the scrollbar away while it is open, and the page keeps that width as padding,
 * so nothing behind it moves (base.css). Measured on a probe, whatever the page's scrollbar is doing; again on resize,
 * since zooming changes it.
 */
export function trackScrollbarWidth(): () => void {
    const measure = () => {
        const probe = document.createElement('div');
        probe.style.cssText = 'position: absolute; top: -9999px; width: 100px; height: 100px; overflow: scroll; visibility: hidden;';
        document.body.appendChild(probe);
        const width = probe.offsetWidth - probe.clientWidth;
        probe.remove();
        document.documentElement.style.setProperty('--scrollbar-w', `${width}px`);
    };
    measure();
    window.addEventListener('resize', measure);
    return () => window.removeEventListener('resize', measure);
}
