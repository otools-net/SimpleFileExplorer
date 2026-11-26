# Bundle size

What the client's initial bundle (the JavaScript and CSS the app downloads before the first page shows) is made of,
and why a file is in it. Rules and numbers: [bundle-size](../../docs/development/bundle-size.md).

```sh
# a production build with its stats file, outside wwwroot (from src/Sfe.Web)
npx ng build --stats-json --output-path <tmp>/sfe-bundle

# bytes per package / source folder and per file, biggest first
node agents/tools/bundle-size/breakdown.mjs <tmp>/sfe-bundle/browser-stats.json --top 40

# the chain of static imports from main.ts to a file ("not initial" when only import() or @defer reach it)
node agents/tools/bundle-size/why.mjs <tmp>/sfe-bundle/browser-stats.json files/properties.dialog.ts @use-gesture/core
```

- The sizes are esbuild's `bytesInOutput`, before Angular's last minification: compare them with one another, don't
  add them up to the size `ng build` prints.
- A target is matched by the end of its path (`includes`): give enough of it to be unique.
