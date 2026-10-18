import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { Dialogs } from '../dialogs.service';

/**
 * Asks before the listing goes elsewhere (another folder, a search, back) while the viewer shows a file with unsaved
 * changes: the route runs it on every change of its query too (`runGuardsAndResolvers`).
 */
export const leaveViewer: CanDeactivateFn<unknown> = () => inject(Dialogs).viewer()?.confirmLeave() ?? true;

/** Asks before the page of a file (`view?path=`) goes elsewhere in the app with unsaved changes. */
export const leaveViewPage: CanDeactivateFn<{ canLeave(): boolean }> = page => page.canLeave();
