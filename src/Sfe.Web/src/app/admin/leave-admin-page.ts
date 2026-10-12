import { CanDeactivateFn } from '@angular/router';

/**
 * Asks before leaving the administration page with settings not saved. Apart from the page, so that the routes can
 * name it without loading the page (it loads when first opened).
 */
export const leaveAdminPage: CanDeactivateFn<{ canLeave(): boolean }> = page => page.canLeave();
