import { CanDeactivateFn } from '@angular/router';

/**
 * Asks before leaving a wizard with something chosen and not saved. Apart from the page, so that the routes can name
 * it without loading the page.
 */
export const leaveWizard: CanDeactivateFn<{ canLeave(): boolean }> = (page) => page.canLeave();
