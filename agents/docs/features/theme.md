# Theme (light / dark)

Code: `src/Sfe.Web/src/theme-boot.ts`, `core/theme.service.ts`, `styles/base.css`; server `Sfe:Appearance:Theme`
(enum `Theme`). Build of the boot script: [build.md](../environment/build.md).

- `styles/base.css` has the dark palette on `:root`, the light one on `:root[data-theme="light"]`; there is no
  `prefers-color-scheme` rule, because JS always sets `data-theme` to `light`/`dark`.
- The classic script `src/theme-boot.ts` (in `<head>`, before the CSS) sets it before first paint from localStorage:
  `sfe.theme` (the browser's pick, Administration › Interface › Appearance) or `sfe.theme.default` (the last server
  default it saw).
- Then `ThemeService` takes over from `/api/config` `theme` (`Sfe:Appearance:Theme`, enum `Theme`) and follows device
  changes. Keep the two in step.
- The player page stays dark: `theme-boot.ts` forces it for `/player`, and nothing there injects `ThemeService`.
- `Theme` has a `JsonStringEnumConverter` attribute, so `settings.json` stores the name; the API's camelCase converter
  wins over it, so the API sends `light`.
