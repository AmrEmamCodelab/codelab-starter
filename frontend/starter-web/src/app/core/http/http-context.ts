import { HttpContext, HttpContextToken } from '@angular/common/http';

/** Set on a request whose failure the caller handles itself, so the error dialog stays closed. */
export const SKIP_ERROR_DIALOG = new HttpContextToken<boolean>(() => false);

/** Set on a request whose 401 is an answer, not an expired session (login, refresh, logout). */
export const SKIP_SESSION_EXPIRY = new HttpContextToken<boolean>(() => false);

/** Context for a call that handles its own failures and is not part of an established session. */
export function selfHandledContext(): HttpContext {
  return new HttpContext().set(SKIP_ERROR_DIALOG, true).set(SKIP_SESSION_EXPIRY, true);
}

/** Context for a call that handles its own failures inside a session. */
export function quietContext(): HttpContext {
  return new HttpContext().set(SKIP_ERROR_DIALOG, true);
}
