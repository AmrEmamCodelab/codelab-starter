# Web sign-in (`features/auth`)

## Purpose

The public pages that start a session: the login page with its method chooser (basic user name and password, Active
Directory, single sign-on), the SSO callback that turns the identity provider's answer into a session, and the account
activation page that sets an account's first password. Every page is outside the shell and needs no permission
(`PUBLIC_ROUTE_PATHS`).

| Piece | What it does | Files |
| --- | --- | --- |
| `LoginPageComponent` | Route `RoutePaths.Login`. Loads the enabled methods, shows `dga-tabs` only when more than one is enabled, the shared user name and password form for basic and AD, the SSO button, session-ended and "password set" notices, refusals inline | `login-page/` |
| `LoginModesService` | `GET api/auth/modes` once per visit: `state` (loading, ready, failed), `methods` (fixed order basic, AD, SSO), `hasChoice`, `failure`. Provided per page | `login-modes.service.ts` |
| `SignInService` | One sign-in at a time; `signIn(credentials, method, returnUrl)` picks `AuthService.login` or `loginWithDirectory`; `startSso(returnUrl)` leaves for the identity provider; refusals through the core `loginRejectionFrom()`. Provided per page | `sign-in.service.ts` |
| Form rules | `credentialErrors()`, `sessionEndedNoticeKey()`, `loginNoticeKey()`, `loginMethodsFrom()`, `usesPasswordForm()`, `LoginTextKeys`, `LoginMethodTextKeys` | `login-form.ts` |
| `SsoCallbackPageComponent` + `SsoCallbackService` | Route `RoutePaths.SsoCallback` (`/auth/sso/callback`): reads `#ticket=` or `#error=`, clears the fragment, exchanges the ticket once, lands on the safe return URL (replacing the history entry); shows the refusal with a way back to the login page | `sso-callback/` |
| `ActivatePageComponent` + `ActivationService` | Route `RoutePaths.Activate` (`/activate#ticket=`): password and confirmation, checked against the server's length floor, `POST api/auth/activate`, then the login page with `?notice=activated` | `activate/` |
| Activation rules | `newPasswordErrors()`, `serverPasswordErrorKey()`, `ACTIVATION_PASSWORD_MIN_LENGTH` (mirrors `IdentityLimits.PasswordMinLengthFloor`) and `ACTIVATION_PASSWORD_MAX_LENGTH` | `activate/activation-form.ts` |

## Use

The three routes are registered in `app.routes.ts`; nothing else imports this feature. Core sends signed-out users to
the login page with `?returnUrl=` (and `?reason=expired|idle` when a session ended). The API sends the browser to
`/auth/sso/callback#ticket=<one-time ticket>` (or `#error=<code>`, optionally `&returnUrl=`), and an administrator or the
user import hands out `/activate#ticket=<activation ticket>` links.

The methods follow the API's `Auth:Modes` (`basic`, `ad`, `sso`, any combination):

| `Auth:Modes` | The login page shows |
| --- | --- |
| one method | that method alone, no tabs (an SSO-only deployment shows just the SSO button) |
| two or three | one tab per enabled method; the first is selected |
| none (incoherent config) or the modes call failed | "sign-in is not available" or the failure with a retry; never a guessed form |

## Extend

- **Another sign-in method:** a `SignInMode` member and `AuthService` method in core (see core MODULE.md), a flag in
  `AuthModesResponse`, a line in `loginMethodsFrom()`, its tab and subtitle in `LoginMethodTextKeys`, and the panel in
  the login template. Add its texts to both bundles and a case to `login-form.spec.ts` and the page spec.
- **A new client-side field check:** extend `credentialErrors()` or `newPasswordErrors()` with a message key and a test.
- **A new server refusal:** add the code to core's `IdentityErrorCodes` and `errors.<CODE>` to both bundles;
  `auth-translations.spec.ts` checks the bundles hold every key this feature renders.

## Rules

- Every call is self-handled (`selfHandledContext()` in core's `AuthClient`): refusals show on these pages, never in the
  app-wide error dialog.
- A refusal never says which credential was wrong, and a locked-out account reads exactly like a wrong password (the
  server answers the same `IDENTITY_INVALID_CREDENTIALS`).
- Post-sign-in targets come only from `safeReturnUrl()`; an absolute or protocol-relative URL is ignored, from the query,
  the fragment or storage alike.
- Every `dga-button` here carries `data-public-action` (these pages run without a session); the permission-gates check
  enforces it.
- No raw text; every message is a key in both `ar.json` and `en.json`.
- No pages, tabs or calls for any national-identity sign-in; only the three methods above exist.

## Gotchas

- Tickets travel in the URL FRAGMENT, which no server log records. The pages read the raw fragment through
  `BrowserLocation`, clear it at once (`history.replaceState`), and never log, store or render the ticket; a reload or
  Back finds no ticket. The SSO exchange runs once per page; a replayed ticket is refused by the server
  (`IDENTITY_SSO_TICKET_REJECTED`).
- The SSO start is a top-level navigation to `{apiBaseUrl}/api/auth/sso/login?returnUrl=`, never an XHR: the API sets
  its binding cookie and redirects to the identity provider. The button stays busy until the browser leaves.
- Logout of an SSO session ends at the identity provider's end-session page (a top-level GET with `id_token_hint`);
  that is core's `AuthService.logout()`, not this feature.
- The activation length check is the server's FLOOR; a deployment may require more (`Identity:Password:MinLength`, and
  a distinct-character minimum). The server's `PASSWORD_TOO_SHORT` / `PASSWORD_TOO_SIMPLE` field errors show under the
  password field.
- `AuthService.login` and friends throw the `HttpErrorResponse` on refusal; a non-HTTP failure (a request-signing error)
  shows as `errors.INTERNAL_ERROR` with a client-minted reference code.
- The pages are outside the shell, so the login and activation pages render their own language switcher.
