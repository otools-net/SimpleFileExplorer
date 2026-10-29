// Checks of a screen's layout on a phone (mobile e2e plan, Phase 3): no sideways scrolling, buttons big enough for a
// finger.
import { expect, test, type Page } from '@playwright/test';

/** The page is no wider than the screen: nothing to scroll sideways. */
export async function expectFitsScreen(page: Page): Promise<void> {
    const { scroll, client } = await page.evaluate(() => ({ scroll: document.documentElement.scrollWidth, client: document.documentElement.clientWidth }));
    expect(scroll, 'page width (scrollWidth) against the screen').toBeLessThanOrEqual(client);
}

/**
 * The buttons, links and fields shown in `scope` (a CSS selector, default the page) are at least `min` px each way
 * (24: WCAG 2.2 AA, 2.5.8), counting what they can be hit through (a link's box); smaller ones fail. Those under 44
 * (Apple, WCAG AAA) are listed in the report as a note.
 */
export async function expectTapTargets(page: Page, scope = 'body', min = 24): Promise<void> {
    const targets = await page.evaluate(scope => {
        const width = innerWidth;
        const height = innerHeight;
        return [...document.querySelectorAll(`${scope} :is(button, a[href], input:not([type=hidden]), select, textarea, [role=button], [role=menuitem], label.select-box)`)]
            .filter(element => {
                const box = element.getBoundingClientRect();
                const style = getComputedStyle(element);
                return box.width > 0 && box.height > 0 && style.visibility !== 'hidden' && Number(style.opacity) > 0
                    && box.bottom > 0 && box.top < height && box.right > 0 && box.left < width
                    && !element.closest('[inert], [aria-hidden=true]');
            })
            .map(element => {
                const box = element.getBoundingClientRect();
                const name = element.getAttribute('aria-label') ?? element.getAttribute('title') ?? element.textContent?.trim().slice(0, 30) ?? '';
                return { what: `${element.tagName.toLowerCase()}.${String(element.className).split(' ')[0]} "${name}"`, width: Math.round(box.width), height: Math.round(box.height) };
            });
    }, scope);
    const small = (limit: number) => targets.filter(t => t.width < limit || t.height < limit).map(t => `${t.what} ${t.width}×${t.height}`);
    const under44 = [...new Set(small(44))];
    if (under44.length)
        test.info().annotations.push({ type: 'tap targets under 44 px', description: under44.join('; ') });
    expect([...new Set(small(min))], `tap targets under ${min} px`).toEqual([]);
}

/** iOS zooms the whole page into a field whose text is under 16 px when it gets the focus: none of those. */
export async function expectNoZoomOnFocus(page: Page, selector: string): Promise<void> {
    const small = await page.evaluate(selector => [...document.querySelectorAll<HTMLElement>(selector)]
        .filter(element => element.getClientRects().length > 0 && parseFloat(getComputedStyle(element).fontSize) < 16)
        .map(element => `${element.tagName.toLowerCase()}${element.id ? '#' + element.id : ''}.${String(element.className).split(' ')[0]} ${getComputedStyle(element).fontSize}`), selector);
    expect(small, 'fields with text under 16 px (iOS zooms into them)').toEqual([]);
}
