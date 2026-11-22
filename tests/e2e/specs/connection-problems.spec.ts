// A connection that can't be used, on a phone (agents/docs/features/connection-files.md): its browse route answers a
// problem (409 `connection-needs-reconnecting`), which once was read as a listing and broke the page. The listing says
// to sign in again, with a way back to the library. The suite's server has no connection: the configuration, the
// connections and the browse answer are made by the test (page.route).
import { Page } from '@playwright/test';
import { expect, test } from '../fixtures/test';
import { expectFitsScreen } from '../fixtures/layout';

const DROPBOX = { kind: 'dropbox', name: 'Dropbox', icon: 'globe', signIn: [], capabilities: {} };

/** An account-level connection whose provider no longer accepts its sign-in. */
async function connectionToSignInAgain(page: Page): Promise<void> {
    await page.route('**/api/v1/config', async (route) => {
        const response = await route.fetch();
        const json = await response.json();
        json.connections = { levels: ['account'], kinds: [DROPBOX] };
        json.rights = [...json.rights, 'connections.use', 'connections.own'];
        await route.fulfill({ response, json });
    });
    const usable = {
        id: 3,
        kind: 'dropbox',
        name: 'Holiday Dropbox',
        level: 'account',
        state: 'needsReconnecting',
        access: 'read',
    };
    await page.route('**/api/v1/connections', (route) =>
        route.fulfill({ json: { usable: [usable], managed: [], groups: [], libraries: [] } }),
    );
    await page.route('**/api/v1/connections/3/browse?*', (route) =>
        route.fulfill({
            status: 409,
            contentType: 'application/problem+json',
            json: {
                type: 'connection-needs-reconnecting',
                title: 'Conflict',
                status: 409,
                detail: 'The connection must be signed in to again.',
            },
        }),
    );
}

test.describe('@reported a connection to sign in to again', () => {
    // The browser logs the 409 the test answers on purpose.
    test.use({ allowedErrors: /\/api\/v1\/connections\/3\/browse/ });

    test('its listing says so and leads to the connections page, not to a broken page', async ({ page }) => {
        await connectionToSignInAgain(page);

        await page.goto('./?c=3');
        const problem = page.locator('[data-connection-problem]');
        await expect(problem).toBeVisible();
        await expect(problem).toContainText('This connection needs signing in again');
        await expect(problem.getByRole('link', { name: 'Open connections' })).toHaveAttribute(
            'href',
            /section=account&page=connections/,
        );
        await expectFitsScreen(page);

        await problem.getByRole('link', { name: 'Go to the top of the library' }).tap();
        await expect(page).not.toHaveURL(/c=3/);
        await expect(page.locator('sfe-listing [data-path]').first()).toBeVisible();
    });
});
