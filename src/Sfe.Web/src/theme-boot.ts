// Classic script loaded in <head>, before the page is drawn, so that it never flashes the wrong theme while the
// server's configuration loads. It uses this browser's pick, else the server default seen last time;
// ThemeService (app/core/theme.service.ts) takes over once the configuration is loaded. Keep the keys and values
// in step with it. The video player page (player?path=...) is always dark.
//
// Not part of the Angular bundle (which only runs after the page is drawn): `tsc -p tsconfig.boot.json` compiles it
// on its own into out-tsc/boot, copied next to index.html (see angular.json). No import or export: it stays a script.
(() => {
    let theme: string | null = null;
    try {
        theme = localStorage.getItem('sfe.theme') || localStorage.getItem('sfe.theme.default');
    } catch {
        // Storage unavailable (private mode): follow the device.
    }
    if (/\/player$/.test(location.pathname))
        theme = 'dark';
    else if (theme !== 'light' && theme !== 'dark')
        theme = matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
    document.documentElement.dataset['theme'] = theme;
})();
