// @diagnose (SFE_E2E_DIAGNOSE=1, Chromium, after `node scripts/profile-client.mjs`): a CPU profile of opening the item
// menu, with the shipped client rebuilt with source maps and served in place of the app's: the functions taking the
// time, by source file and line. Run on android-slow for a phone's proportions. Mobile e2e plan, Phase 0.
import fs from 'node:fs';
import { SourceMap } from 'node:module';
import path from 'node:path';
import { expect, item, openFolder, test } from '../../fixtures/test';
import { work } from '../../paths';

type Node = { id: number; callFrame: { functionName: string; url: string; lineNumber: number; columnNumber: number }; children?: number[] };
type Profile = { nodes: Node[]; samples: number[]; timeDeltas: number[] };

const client = path.join(work, 'profile-client');

for (const [label, folder, file] of [
    ['6 photos', 'Photos/2025 Mountains', 'Photos/2025 Mountains/Summit.jpg'],
    ['600 photos', 'Many photos', 'Many photos/Photo 002.jpg'],
] as const) {
    test(`@diagnose @timing profile of the item menu, ${label}`, async ({ page, browserName }) => {
        test.skip(process.env.SFE_E2E_DIAGNOSE !== '1', 'diagnostics: SFE_E2E_DIAGNOSE=1');
        test.skip(browserName !== 'chromium', 'Chromium profiles');
        test.skip(!fs.existsSync(path.join(client, 'index.html')), 'run node scripts/profile-client.mjs first');

        // The client from the profiling build; the API from the app.
        await page.route(url => !new URL(url).pathname.startsWith('/api/'), async route => {
            const name = decodeURIComponent(new URL(route.request().url()).pathname).replace(/^\//, '');
            const own = name && fs.existsSync(path.join(client, name)) && fs.statSync(path.join(client, name)).isFile();
            await route.fulfill({ path: path.join(client, own ? name : 'index.html') });
        });
        await openFolder(page, folder);
        const tile = item(page, file);
        await tile.locator('.select-box').tap();
        await expect(tile).toHaveClass(/selected/);
        await page.waitForTimeout(800);

        const cdp = await page.context().newCDPSession(page);
        await cdp.send('Profiler.enable');
        await cdp.send('Profiler.setSamplingInterval', { interval: 100 });
        await cdp.send('Profiler.start');
        await tile.locator('.item-menu-button').tap();
        await expect(page.locator('.item-menu[role="menu"]')).toBeVisible();
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        const { profile } = await cdp.send('Profiler.stop') as unknown as { profile: Profile };

        console.log(`[profile] ${test.info().project.name} | ${label}\n${summarise(profile)}`);
    });
}

/** Self and total time per source location (source-mapped), top entries; idle and the profiler itself left out. */
function summarise(profile: Profile): string {
    const byId = new Map(profile.nodes.map(node => [node.id, node]));
    const parent = new Map<number, number>();
    for (const node of profile.nodes)
        for (const child of node.children ?? [])
            parent.set(child, node.id);
    const self = new Map<number, number>();
    profile.samples.forEach((id, i) => self.set(id, (self.get(id) ?? 0) + (profile.timeDeltas[i] ?? 0) / 1000));

    const maps = new Map<string, SourceMap | null>();
    const where = (frame: Node['callFrame']): string => {
        if (!frame.url)
            return `(${frame.functionName || 'native'})`;
        const file = new URL(frame.url).pathname.replace(/^\//, '');
        if (!maps.has(file)) {
            const map = path.join(client, file + '.map');
            maps.set(file, fs.existsSync(map) ? new SourceMap(JSON.parse(fs.readFileSync(map, 'utf8'))) : null);
        }
        const entry = maps.get(file)?.findEntry(frame.lineNumber, frame.columnNumber) as
            { originalSource?: string; originalLine?: number; name?: string } | undefined;
        if (!entry?.originalSource)
            return `${file}:${frame.lineNumber + 1} ${frame.functionName}`;
        const source = entry.originalSource.replace(/^.*?(src\/app\/|node_modules\/)/, '$1');
        return `${source}:${(entry.originalLine ?? 0) + 1} ${entry.name ?? frame.functionName}`;
    };

    const selfBy = new Map<string, number>();
    const totalBy = new Map<string, number>();
    for (const [id, ms] of self) {
        const node = byId.get(id)!;
        if (['(idle)', '(program)', '(garbage collector)'].includes(node.callFrame.functionName) && !node.callFrame.url) {
            selfBy.set(node.callFrame.functionName, (selfBy.get(node.callFrame.functionName) ?? 0) + ms);
            continue;
        }
        const key = where(node.callFrame);
        selfBy.set(key, (selfBy.get(key) ?? 0) + ms);
        // Total: once per distinct location up the stack (recursion counted once).
        const seen = new Set<string>();
        for (let at: number | undefined = id; at !== undefined; at = parent.get(at)) {
            const up = where(byId.get(at)!.callFrame);
            if (!seen.has(up)) {
                seen.add(up);
                totalBy.set(up, (totalBy.get(up) ?? 0) + ms);
            }
        }
    }
    const top = (map: Map<string, number>, n: number, filter: (key: string) => boolean = () => true) =>
        [...map].filter(([key]) => filter(key)).sort((a, b) => b[1] - a[1]).slice(0, n)
            .map(([key, ms]) => `    ${ms.toFixed(1).padStart(7)} ms  ${key}`).join('\n');
    return `  self:\n${top(selfBy, 20)}\n  total, app code:\n${top(totalBy, 25, key => key.startsWith('src/app/'))}`
        + `\n  total, all:\n${top(totalBy, 15, key => !key.startsWith('('))}`;
}
