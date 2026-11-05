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

/**
 * Set on a request the server never checks for a signature and never signs the answer to: an anonymous call (the
 * server verifies proofs only on authenticated requests, so a refresh or login is never bound to a session key) and
 * the signing-key issuance (exempt, unsigned answer). Such a request is sent unsigned, so its response is not
 * verified; signing it would make the verifier refuse the server's unsigned 200 (`response_signature_missing`).
 * `ApiService` sets it on every anonymous call.
 */
export const UNSIGNED_REQUEST = new HttpContextToken<boolean>(() => false);

/** {@link selfHandledContext} for a call the server neither verifies nor signs (see {@link UNSIGNED_REQUEST}). */
export function unsignedSelfHandledContext(): HttpContext {
  return selfHandledContext().set(UNSIGNED_REQUEST, true);
}
