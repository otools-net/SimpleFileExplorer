// SFE end-to-end tests (README.md): the real app, built and started by scripts/server.mjs on a fresh copy of the
// sample library, driven in phone-sized browsers with touch. Reports and traces go to the work folder, not the repo.
import { defineConfig, devices } from '@playwright/test';
import path from 'node:path';
import { auth, baseURL, here, work, type Account } from './paths';

// Chromium: the installed Edge on Windows (no download), Playwright's own elsewhere (CI). WebKit: `npm run browsers`.
const channel = process.env.SFE_E2E_CHANNEL ?? (process.platform === 'win32' ? 'msedge' : undefined);
const android = { ...devices['Pixel 7'], channel };

type Options = { account: Account; allowedErrors: RegExp | null; cpuSlowdown: number };

export default defineConfig<Options>({
    testDir: path.join(here, 'specs'),
    outputDir: path.join(work, 'results'),
    globalSetup: path.join(here, 'setup/global-setup.ts'),
    fullyParallel: true,
    forbidOnly: !!process.env.CI,
    retries: process.env.CI ? 1 : 0,
    workers: process.env.SFE_E2E_WORKERS ? Number(process.env.SFE_E2E_WORKERS) : '50%',
    timeout: 60_000,
    expect: { timeout: 10_000 },
    reporter: [['list'], ['html', { outputFolder: path.join(work, 'report'), open: 'never' }]],
    use: {
        baseURL,
        storageState: auth('admin'),
        colorScheme: 'dark',
        trace: 'retain-on-failure',
        video: 'retain-on-failure',
        screenshot: 'only-on-failure',
    },
    // The target sizes of the overflow menus epic (agents/docs/features/overflow-menus.md). `@desktop` specs run on a
    // desktop too, so phone fixes don't break the mouse; phone specs never run there.
    projects: [
        { name: 'android', use: android, grepInvert: /@desktop-only/ },
        {
            name: 'android-small',
            use: { ...android, viewport: { width: 360, height: 740 }, screen: { width: 360, height: 800 }, deviceScaleFactor: 3 },
            grepInvert: /@desktop-only/,
        },
        { name: 'iphone', use: { ...devices['iPhone 13'] }, grepInvert: /@desktop-only/ },
        {
            name: 'landscape',
            use: { ...android, viewport: { width: 800, height: 360 }, screen: { width: 800, height: 412 } },
            grepInvert: /@desktop-only/,
        },
        { name: 'desktop', use: { ...devices['Desktop Chrome'], channel, viewport: { width: 1280, height: 800 } }, grep: /@desktop/ },
        // A mid-range Android phone's speed (CPU 4× slower than this computer): timings, not layout. Measured last, one
        // test at a time, once the other devices are done: their load on this computer swung the medians past the
        // budgets (107 ms against 100 under the full run, ~70 alone). Alone: `--project=android-slow --no-deps`.
        {
            name: 'android-slow', use: { ...android, cpuSlowdown: 4 }, grep: /@timing/, fullyParallel: false,
            dependencies: ['android', 'android-small', 'iphone', 'landscape', 'desktop'],
        },
    ],
    webServer: {
        command: 'node scripts/server.mjs',
        cwd: here,
        url: `${baseURL}api/migration`,
        // The first run builds the app (client included): minutes.
        timeout: 15 * 60_000,
        reuseExistingServer: false,
        stdout: 'pipe',
        stderr: 'pipe',
    },
});
