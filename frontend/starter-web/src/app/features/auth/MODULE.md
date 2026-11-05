# Web sign-in (`features/auth`)

## Purpose

The basic sign-in page: user name and password against `POST api/auth/login`. It shows a session-ended notice, field
checks, and the server's refusal inline, then sends the user to a safe return URL or the dashboard.

| Piece | What it does | Files |
| --- | --- | --- |
| `LoginPageComponent` | The page (route `RoutePaths.Login`): `dga-card`, two `dga-text-input`s, a `dga-button`, the language switcher | `login-page/` |
| `SignInService` | The flow: one sign-in at a time, `busy` and `failure` signals, navigation on success. Provided per page | `sign-in.service.ts` |
| `signInFailureFrom()` | Reads a login failure through the ng-security problem+json parser: the server code `IDENTITY_INVALID_CREDENTIALS` (`SignInErrorCodes`, sent with 400) becomes `auth.login.invalid-credentials` with no reference code, anything else `errors.<CODE>` plus the reference code | `sign-in.service.ts` |
| Form rules | `credentialErrors()`, `hasCredentialErrors()`, `sessionEndedNoticeKey()` | `login-form.ts` |

## Use

The route is registered in `app.routes.ts`; nothing else imports this feature. Core sends signed-out users here with
`?returnUrl=` (and `?reason=expired|idle` when a session ended), and the page reads both from `ActivatedRoute`.

## Extend

- **Another sign-in method** (for example a single sign-on button): a new component in this feature calling a new
  `AuthService` method in core; keep the basic form as is. Add its texts to both bundles.
- **A new client-side field check:** extend `credentialErrors()` with a message key in `LoginFieldMessageKeys` and a
  test in `login-form.spec.ts`.

## Rules

- The login call is self-handled (`selfHandledContext()` in core's `AuthClient`): its failures show on this page, never
  in the app-wide error dialog.
- A refusal never says which credential was wrong.
- The post-login target comes only from `safeReturnUrl()`; an absolute or protocol-relative URL is ignored.
- No raw text; every message is a key in both `ar.json` and `en.json`.

## Gotchas

- `AuthService.login` throws the `HttpErrorResponse` on refusal; a non-HTTP failure (a request-signing error) is shown
  as `errors.INTERNAL_ERROR` with a client-minted reference code.
- The page is outside the shell, so it renders its own language switcher.
- Wrong credentials are recognised by the problem+json **code**, never the status: the server answers 400, and a 401
  would mean an expired session. `SignInErrorCodes` mirrors the backend's `IdentityErrorCodes`; change both together.
