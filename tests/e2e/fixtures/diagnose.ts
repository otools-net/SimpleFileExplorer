// Helpers of the @diagnose specs: what-if changes made in the page before measuring.
import type { Page } from '@playwright/test';

/** Removes the CSS rules whose selector contains `text` from the page's style sheets; returns how many. */
export async function removeCssRules(page: Page, text: string): Promise<number> {
    return page.evaluate(text => {
        let removed = 0;
        const strip = (rules: CSSRuleList) => {
            for (let i = rules.length - 1; i >= 0; i--) {
                const rule = rules[i];
                if (rule instanceof CSSStyleRule && rule.selectorText.includes(text)) {
                    (rule.parentRule as CSSGroupingRule | null ?? rule.parentStyleSheet!).deleteRule(i);
                    removed++;
                } else if ('cssRules' in rule) {
                    strip((rule as CSSGroupingRule).cssRules);
                }
            }
        };
        for (const sheet of document.styleSheets)
            try { strip(sheet.cssRules); } catch { /* another origin */ }
        return removed;
    }, text);
}

/** The sheet's page lock (styles/phone-sheets.css) out of the page. */
export const withoutPageLock = (page: Page) => removeCssRules(page, ':has(.sheet-backdrop)');
