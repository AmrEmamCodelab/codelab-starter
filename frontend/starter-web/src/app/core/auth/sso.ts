import { parseFragmentParams } from '../session/browser-location';

/**
 * The single sign-on contract the app shares with the API's login-modes endpoints.
 *
 * The browser never talks to the identity provider itself. It NAVIGATES (top level) to `api/auth/sso/login`; the
 * API redirects to the provider (code + PKCE), receives the code at `signin-oidc`, matches the account, and sends
 * the browser to `/auth/sso/callback` with a one-time ticket in the URL FRAGMENT (`#ticket=`), or `#error=<code>`.
 * The fragment never reaches a server log; the callback page clears it at once and swaps the ticket at
 * `api/auth/sso/exchange` for the same session body every other login returns.
 */
export const SsoFragmentParams = {
  /** The sealed one-time exchange ticket (valid for minutes). Never logged, never stored. */
  Ticket: 'ticket',
  /** The refusal code when the sign-in did not complete. */
  Error: 'error',
  /** Optional: the app-relative URL to land on, when the API echoes it back. */
  ReturnUrl: 'returnUrl',
} as const;

/** What the callback fragment carried. At most one of `ticket` and `error` is set. */
export interface SsoCallbackFragment {
  readonly ticket: string | null;
  readonly error: string | null;
  readonly returnUrl: string | null;
}

/** Reads the raw (still percent-encoded) callback fragment. A fragment with both a ticket and an error is an error. */
export function parseSsoCallbackFragment(fragment: string): SsoCallbackFragment {
  const params = parseFragmentParams(fragment);
  const error = nonEmpty(params.get(SsoFragmentParams.Error));
  return {
    ticket: error === null ? nonEmpty(params.get(SsoFragmentParams.Ticket)) : null,
    error,
    returnUrl: nonEmpty(params.get(SsoFragmentParams.ReturnUrl)),
  };
}

function nonEmpty(value: string | undefined): string | null {
  return value === undefined || value.trim() === '' ? null : value;
}
