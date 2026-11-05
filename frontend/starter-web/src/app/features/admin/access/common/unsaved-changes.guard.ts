import type { CanDeactivateFn } from '@angular/router';

/** A page that may hold unsaved edits. */
export interface LeaveAware {
  /** True (or a promise of true) when the page may be left now; it asks the user when there are unsaved edits. */
  canLeave(): boolean | Promise<boolean>;
}

/** The leave guard of the editors: asks the page, which asks the user only when something is unsaved. */
export const unsavedChangesGuard: CanDeactivateFn<LeaveAware> = (page) => page?.canLeave() ?? true;
