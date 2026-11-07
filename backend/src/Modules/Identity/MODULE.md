# Identity

## Purpose
Module 1: who the caller is, how a session lives, and how an account is kept safe.

- **Basic login:** user name and password, hashed with the ASP.NET Core Identity `PasswordHasher` (PBKDF2, from the shared framework). Every credential failure (unknown user, wrong password, deactivated, locked out, not yet activated, bound to another sign-in mode) answers the same `IDENTITY_INVALID_CREDENTIALS`, and every path costs one hash check (a decoy hash when there is no usable one), so neither the answer nor its timing tells them apart. The auth event keeps the real reason.
- **Lockout** (`Application/PasswordCheck.cs`, rule in `UserAccount.RecordFailedSignIn`): `Identity:Lockout:MaxFailedAttempts` consecutive wrong passwords lock the account for `Identity:Lockout:LockoutMinutes`. A locked account is refused with the same code even with the right password; an attempt during a lockout is not counted and never extends it; a success resets the counter; an administrator unlocks through `IAccountAdministration.UnlockAsync`. Only a wrong password of a password-bound account counts (directory and SSO credentials are locked out by their own authority).
- **One session issuer** (`Application/SessionIssuer.cs`): a JWT access token (HMAC-SHA256) carrying `sub` (the account's `SubjectId`), `sid`, `sver` (the session version), `jti` and, while request signing is on, `skid`; never a permission or a role. A refresh token `<sid>.<secret>` (only the SHA-256 of the secret is stored) that every refresh rotates. The idle timeout travels in the login response. Each session records its sign-in mode (`SignInMode`: basic 1, Active Directory 2, SSO 3) and, for SSO, the identity provider's id_token as the logout hint.
- **Logout** ends only the caller's session: it is revoked, its `sid` goes into the Codelab.Security revocation list until its last access token expires, and the bearer scheme refuses that `sid` on the next request (401). The user's other sessions keep working.
- **Account administration** for other modules: the kernel port `IAccountAdministration` (implemented by `Application/Accounts/AccountAdministration.cs`, consumed by the Users module). Accounts carry user name, display name, optional e-mail, login name and UPN (each unique when present, compared normalized), kind (internal 1 / external 2), binding (password 1 / Active Directory 2 / SSO 3) and the organization id. An account is created **without a password**; a password account gets an activation link. There is no default or shared password anywhere.
- **Activation links** (`Application/Activation/`): a Codelab.Security sealed one-time ticket (purpose `account-activation`) for the account's subject, with a random ticket id of this module stored on the account. Redeeming it (`POST /api/auth/activate`) sets the password; issuing a new link (an administrator's password reset) clears the current password and supersedes every older link.
- **Auth event trail** (`identity.AuthEvents`, `Application/Auditing/IAuthEventSink.cs`, `Infrastructure/Auditing/EfAuthEventSink.cs`): one append-only row per decision: login succeeded or failed (any mode), lockout, logout, refresh rejected, activation issued, redeemed or rejected, deactivation, reactivation, unlock. A row holds the event type and failure reason (integers), the user's internal id, the keyed Codelab.Observability `UserIdHash` of the typed identifier, the sign-in mode, the time and the correlation id; never a password, token, ticket or raw user name.
- **Accounts from configuration:** the break-glass administrator (holds every permission) and, in Development only, low-privilege Development users that hold `dashboard.view`.
- **`IUserDirectory`** for the other modules: internal id, active flag, break-glass flag, kind, organization and display name, looked up by token subject.
- **`INotificationRecipients`** for the Notifications module (`Infrastructure/Notifications/NotificationRecipients.cs`): an account's e-mail address, mobile number and language by internal user id, read at delivery time. Accounts store no mobile number and no preferred language yet, so `Mobile` is always null and the language is `NotificationRecipients.DefaultLanguage` (Arabic). A deactivated account is still returned (the sender decides); an unknown id is null.

Tables (schema `identity`; migrations `InitialCreate`, `AccountAdministration`): `UserAccounts`, `UserSessions`, `AuthEvents`.

## Use
HTTP (all under `/api/auth`, JSON, errors as Codelab.Errors problem+json):

| Endpoint | Access | Body | Answer |
| --- | --- | --- | --- |
| `POST /api/auth/login` | anonymous | `{userName, password}` | 200 `{accessToken, refreshToken, accessTokenExpiresAt, idleTimeoutMinutes, user:{id (PublicId), displayName}}`; 400 `VALIDATION_FAILED` or `IDENTITY_INVALID_CREDENTIALS` |
| `POST /api/auth/refresh` | anonymous | `{refreshToken}` | 200, same shape, new refresh token; 400 `IDENTITY_REFRESH_REJECTED` |
| `POST /api/auth/logout` | `[AuthenticatedUser]` | none | 204; later requests with that access token get 401 |
| `POST /api/auth/activate` | anonymous | `{ticket, password}` | 204; 400 `VALIDATION_FAILED` (field codes `REQUIRED`, `TOO_LONG`, `PASSWORD_TOO_SHORT`, `PASSWORD_TOO_SIMPLE`) or `IDENTITY_ACTIVATION_REJECTED` |

Every `/api/auth` action is under the Codelab.Security per-client rate limit (`[EnableRateLimiting(ClientRateLimitOptions.PolicyName)]`, tuned by `Codelab:Security:RateLimiting`); past the limit the answer is 429 `rate_limited`. A contract test (`AllAnonymousWritesAreRateLimited`) fails any anonymous write without it. Every anonymous route is listed in `backend/tests/Starter.Contract.Tests/EndpointAllowlist.cs`.

The activation link the administration screens show is `<Users:Activation:LinkBaseUrl>/activate#ticket=<ticket>` (built by the Users module); the ticket travels in the fragment, so it never reaches a server log.

In code (inject the kernel ports; never reference this project from another module):

| Port | Use |
| --- | --- |
| `IUserDirectory.FindBySubjectAsync(subjectId)` | Who the token subject is (Access resolves permissions with it). |
| `IAccountAdministration` | `CreateAsync`, `UpdateAsync`, `SetActiveAsync`, `UnlockAsync`, `FindAsync`, `ListAsync` (page size capped at `AccountAdministration.MaxPageSize`, 100), `IssueActivationAsync`. Authorization is the caller's job. |
| `INotificationRecipients.FindAsync(userId)` | Where to reach a user: `{userId, email, mobile (null), language (Arabic)}`, or null for an unknown id. Used by the Notifications module only. |

Error codes (`Application/IdentityErrorCodes.cs`; the client renders `errors.<CODE>`):

| Code | Status | When |
| --- | --- | --- |
| `IDENTITY_INVALID_CREDENTIALS` | 400 | Any sign-in refusal (basic, directory, SSO). |
| `IDENTITY_REFRESH_REJECTED` | 400 | Any refresh refusal. |
| `IDENTITY_SESSION_REQUIRED` | 403 | Logout with a token that names no session. |
| `IDENTITY_ACTIVATION_REJECTED` | 400 | Any activation refusal: malformed, expired, used, superseded, or the account deactivated or no longer password bound. |
| `IDENTITY_ACCOUNT_NOT_FOUND` | 404 | Administration of an unknown account id. |
| `IDENTITY_USER_NAME_TAKEN`, `IDENTITY_EMAIL_TAKEN`, `IDENTITY_LOGIN_NAME_TAKEN`, `IDENTITY_UPN_TAKEN` | 409 | Another account holds the identifier. |
| `IDENTITY_BREAK_GLASS_PROTECTED` | 409 | Update, deactivation or activation of the break-glass account. |
| `IDENTITY_ACTIVATION_NOT_APPLICABLE` | 422 | Activation for an Active Directory or SSO account. |
| `IDENTITY_ACCOUNT_INACTIVE` | 422 | Activation for a deactivated account. |
| `IDENTITY_ACTIVATION_UNAVAILABLE` | 422 | Sealed tickets are off (`Codelab:Security:Tickets:Enabled`). |

Administration validation field codes: `REQUIRED`, `TOO_LONG`, `INVALID_FORMAT` (an e-mail or UPN not shaped like `name@example.test`, a non-ASCII login name, an organization id below 1) and `UNKNOWN_VALUE` (an undefined kind or binding).

Configuration (every key through an options class with `SectionName`, validated on start):

| Key | Notes |
| --- | --- |
| `Identity:Session:AccessTokenMinutes` / `RefreshTokenDays` / `IdleTimeoutMinutes` | Defaults 15 / 7 / 30. The access token may not outlive the idle timeout. |
| `Identity:Jwt:SigningKey` | Base64 of at least 32 random bytes. Local secrets file or environment only. |
| `Identity:Jwt:Issuer` / `Audience` | Optional; default to the host's service name (`starter-api`). |
| `Identity:BreakGlass:UserName` / `DisplayName` / `Password` | User name defaults to `admin`. The password has no default: the host does not start without it, nor with one shorter than 16 characters or with fewer than 8 distinct characters (`IdentityLimits.BreakGlassPasswordMinLength`, `BreakGlassPasswordMinDistinctCharacters`; every environment). |
| `Identity:DevUsers:<n>:UserName` / `Password` / `DisplayName` | Development only; ignored in every other environment. |
| `Identity:Lockout:MaxFailedAttempts` / `LockoutMinutes` | Defaults 5 / 15 (on without the section); 1 to 100 attempts, 1 to 1440 minutes. |
| `Identity:Password:MinLength` / `MinDistinctCharacters` | The activation password rule. Defaults 12 / 5; the minimum length never goes below `IdentityLimits.PasswordMinLengthFloor` (8). |
| `Identity:Activation:TicketLifetimeHours` | Default 72, at most 168; also capped by `Codelab:Security:Tickets:MaxLifetimeSeconds`. |
| `Codelab:Security:Tickets:Enabled` + its key ring | Required for activation links (Codelab.Security). Off: issuing answers `IDENTITY_ACTIVATION_UNAVAILABLE` and every redemption is rejected. |

### Login modes (basic, Active Directory, SSO)

`Auth:Modes` switches on one or more of `basic`, `ad` and `sso`. With no key the deployment offers basic only. A scalar value such as `basic,sso` (for example the environment variable `Auth__Modes=basic,sso`) is read as a comma-separated list. Changing the modes needs a restart.

A listed mode registers its Codelab.Identity provider and forces that section's `Enabled` on, so the provider's deployment guard runs at start. The start fails on:
- an empty, unknown or repeated mode;
- `ad` without a usable `Codelab:Identity:ActiveDirectory`;
- `sso` without Authority, ClientId, RedirectUri, the explicit endpoints or signing keys;
- `sso` without `Auth:WebBaseUrl` (https outside Development);
- `sso` without `Codelab:Security:Tickets:Enabled` and a ring whose MaxLifetimeSeconds is at least 600.

Every endpoint of a mode that is off answers 404 `IDENTITY_LOGIN_MODE_DISABLED` (`Api/LoginModes/LoginModeGate.cs`). Basic login is the exception: `POST /api/auth/login` stays routed for the **break-glass administrator** (`Application/LoginModes/BasicLoginAccounts.cs`), so an AD-only or SSO-only deployment keeps its emergency account usable while the directory or the identity provider is down. With basic off, every other account is excluded before its password is checked (no lockout count, auth event reason `LoginModeDisabled`), and every refusal, including a wrong break-glass password and an invalid body, is the same 404 `IDENTITY_LOGIN_MODE_DISABLED`. The SPA hides the password form when basic is off, so break-glass signs in by calling `POST /api/auth/login` directly (or through a hidden route, if a fork adds one).

AD and SSO end in `SignInExternalAccount`. Only an existing account with the matching binding is accepted; nothing is provisioned. All three modes end in the one `SessionIssuer`.

| Endpoint | Access | Body | Answer |
| --- | --- | --- | --- |
| `GET /api/auth/modes` | anonymous | none | 200 `{basic, activeDirectory, sso}` |
| `POST /api/auth/ad/login` | anonymous | `{userName, password}` | 200 session body (as `/login`); 400 `VALIDATION_FAILED` or `IDENTITY_INVALID_CREDENTIALS`; 503 `IDENTITY_DIRECTORY_UNAVAILABLE` |
| `GET /api/auth/sso/login?returnUrl=&binding=` | anonymous | none | 302 to the identity provider (code + PKCE S256 + state + nonce) and sets the sign-in cookie; 400 `VALIDATION_FAILED` for a missing or malformed `binding` (required) |
| `GET /api/auth/sso/signin-oidc?code&state` | anonymous | none | always 302 to `{Auth:WebBaseUrl}/auth/sso/callback#ticket=<ticket>&returnUrl=<path>` or `#error=<CODE>&returnUrl=<path>` (returnUrl is left out when it is `/`) |
| `POST /api/auth/sso/exchange` | anonymous | `{ticket, bindingProof}` | 200 session body; 400 `IDENTITY_SSO_TICKET_REJECTED` (also for a missing or wrong `bindingProof`); 503 `IDENTITY_SSO_UNAVAILABLE` |
| `POST /api/auth/sso/logout` | `[AuthenticatedUser]` | none | ends the session like `/logout`; 200 `{endSessionUrl}` for an SSO session that kept its id_token, otherwise 204 |

Error codes (`Application/LoginModes/LoginModeErrorCodes.cs`):
- `IDENTITY_LOGIN_MODE_DISABLED` (404)
- `IDENTITY_DIRECTORY_UNAVAILABLE` (503: no password was checked)
- `IDENTITY_SSO_REJECTED` (fragment only)
- `IDENTITY_SSO_UNAVAILABLE` (fragment, or 503 from the exchange)
- `IDENTITY_SSO_TICKET_REJECTED` (400)

A refused directory password, a disabled directory account and an unknown, deactivated or wrongly bound local account all answer `IDENTITY_INVALID_CREDENTIALS`.

Configuration:
- `Auth:Modes:<n>`
- `Auth:WebBaseUrl` (the SPA's absolute base URL)
- `Codelab:Identity:ActiveDirectory:*` and `Codelab:Identity:Sso:*` (see the Codelab.Identity MODULE.md; `MatchBy` chooses e-mail, login name or UPN)
- `Codelab:Security:Tickets:*` (required for `sso`)
- `Codelab:Identity:Sso:BackChannel` (1 Direct, default; 2 GatewayRelay) and `Codelab:Identity:Sso:NoEgress`: with 2, `LoginModesRegistration` registers the integration gateway client (`Codelab:Integration:Gateway:*`, shared with the Notifications SMS relay, registered once) and `Infrastructure/LoginModes/GatewaySsoBackChannel.cs`, so the token exchange and the JWKS fetch go to the gateway's `/relay/sso/token` and `/relay/sso/jwks` with the API's ES256 service assertion; the authorize and end_session redirects are unchanged. A relayed provider refusal (`GATEWAY_UPSTREAM_REJECTED`) is a rejected sign-in; anything else (unreachable, a disabled profile, the gateway refusing the API) is `IDENTITY_SSO_UNAVAILABLE`.
- With `sso` listed, the module also offers the kernel `IIdentityProviderKeySet` (`Infrastructure/LoginModes/SsoIdentityProviderKeySet.cs`): the provider's JWKS text over the registered back channel (direct or the gateway relay), null on any failure. The Ops certificate list reads the IdP signing certificates through it, so their expiry alert works in a backend zone without egress.

Login-mode gotchas:
- **The ticket travels in the URL fragment.** A browser never sends the fragment to a server, so the ticket reaches no proxy, server log or Referer header. The SPA reads it, removes it from the address bar and posts it to `sso/exchange` within 2 minutes. The ticket is a sealed, one-time Codelab.Security ticket that carries the new session's refresh token; the exchange spends it through `RefreshSessionUseCase`. The session starts at the callback, which is when `LoginSucceeded` is recorded.
- **end_session is a top-level GET with `id_token_hint`.** Open `endSessionUrl` with `window.location`, never with fetch or XHR. The hint is the id_token stored on the session; one longer than 8000 characters is dropped, and logout then answers 204.
- **No egress from the backend zone.** Every OIDC endpoint is explicit configuration with no runtime discovery. Use `SigningKeyMode=Pinned` where the API cannot reach the JWKS.
- **The sign-in cookie** is named after a hash of the state, so two tabs never overwrite each other. It is sealed, HttpOnly, SameSite=Lax, `Path=/api/auth/sso`, Secure on https and lives 10 minutes. Because it is SameSite=Lax, the identity provider must answer with the query response mode, not form_post.
- **Required browser binding (login-CSRF defence).** The SPA generates a random secret per sign-in, keeps it in `sessionStorage`, sends `base64url(SHA-256(secret))` as `binding` to `sso/login` and the secret as `bindingProof` to the exchange, and refuses to exchange a callback ticket when its tab holds no pending secret. `sso/login` refuses a request without a binding, and the exchange refuses a ticket without one, so a callback link forwarded to another browser cannot sign that browser in.
- **Basic login while basic is off is break-glass only** (see Login modes). `Identity:BreakGlass:Password` stays required in every deployment because it is the emergency path; keep it in the vault, not in the SPA.
- `Auth:Modes` owns the providers' `Enabled` flags: a configured `Codelab:Identity:*:Enabled` is overridden.
- The callback is a browser navigation, so every refusal is a redirect with `#error=`, never problem+json.
- With `sso` off, `sso/logout` answers 404; the client then calls `/api/auth/logout`.
- Tests: `backend/tests/Starter.Api.Tests/LoginModes` (the `Auth:Modes` boot matrix) and `backend/tests/Starter.Integration.Tests/LoginModes` (the in-process LDAP double, and Starter.DevIdp hosted in process).

The surface they build on (owned here):

- `SignInExternalAccount.ExecuteAsync(AccountMatchField field, string value, SignInMode mode, string? idTokenHint, CancellationToken)` returns `Outcome<LoginResult>`: matches the account on e-mail, login name or UPN (normalized), refuses an unknown, deactivated or wrongly bound account with `IDENTITY_INVALID_CREDENTIALS`, records the auth event and starts the session. Mode is `ActiveDirectory` or `Sso` (basic throws).
- `IUserAccountRepository.FindByMatchAsync(field, value)`; `SessionIssuer.StartAsync(account, new SessionStart(mode, idTokenHint))` (basic login passes `SessionStart.Basic`).
- `CurrentSessionQuery.FindAsync()` returns the caller's `CurrentSession` (sid, user id, `SignInMode`, `IdTokenHint`, ended flag) for the SSO logout; then `LogoutUseCase` ends the session as `/api/auth/logout` does.
- `SessionResponse.From(LoginResult)` (`Api/AuthController.cs`) is the response body every sign-in route shares.

## Extend
- **A new sign-in mode:** authenticate, then call `SignInExternalAccount` (an external authority) or `SessionIssuer.StartAsync(account, new SessionStart(mode, hint))`. Add the mode to `SignInMode` with a new explicit value and to `AccountBindings.Accepts`. Never sign a token or write a session anywhere else.
- **Ending every session of a user:** save the account change through `AccountWrites.SaveAsync(account, SessionImpact.EndSessions)` (or call `ISessionVersions.BumpAsync(subjectId)`): the version is bumped before the save, so open sessions fail their next request (401) and their next refresh.
- **A new account field:** change `Domain/UserAccount.cs` (and `AccountProfile` when it is editable), `Infrastructure/Persistence/IdentityDbContext.cs`, the kernel records in `Starter.Application/Users/IAccountAdministration.cs` if other modules see it, then add a migration (see Gotchas).
- **A new auth event:** add a member with the next explicit value to `AuthEventType` (or `AuthFailureReason`) and record it with `IAuthEventSink.RecordAsync`. Never renumber a member: the values are stored.
- **A new error:** add a constant to `Application/IdentityErrorCodes.cs` and `errors.<CODE>` to both the `ar` and `en` bundles (a field message code goes to `IdentityValidationMessages` and `validation.<CODE>`).

## Rules
- Identity depends on the kernel and the Codelab packages only (Security, Observability, Errors); it never references Access or Users (`module.rules.yaml`). The keys a Development user receives are mirrored in `DevUserPermissionKeys`; `IUserPermissionGrants` refuses a key the catalog lacks, so drift fails the Development start.
- Layers by namespace: `Domain` (entities, enums, the lockout, activation and binding rules), `Application` (use cases, validators, ports, the session issuer, account administration, activation, the auth event port), `Infrastructure` (EF Core, JWT, hasher, security-store and ticket adapters, the auth event sink, options, the startup task), `Api` (thin controllers and DTOs).
- **Session-version bumps** (user-wide, every open session ends): deactivation, an administrator's activation link (password reset), an activation redemption, a binding change, a changed e-mail address, login name or UPN (the identifiers AD and SSO sign-ins match on, so a re-keyed account starts over), a rotated configured password. **Grant and role changes do NOT bump**: permissions are resolved live per request by Access, so a grant or revoke applies to open sessions without relogin (a revoked route answers 403). Logout revokes only its own `sid`.
- The break-glass administrator is configured, never administered: update, deactivation and activation are refused (`IDENTITY_BREAK_GLASS_PROTECTED`); unlock works. It is locked out like every account (a lockout is the brute-force defence; an operator unlocks it or restarts, and every start restores it active, unlocked and with the configured password).
- No password, signing key or secret has a default in code or in a committed settings file. Options, seed records, commands, tickets and tokens print no secret in `ToString()`. An activation ticket is never logged, stored or put in an outbox payload; only its ticket id is.
- The auth event sink never throws (except cancellation) and stores no personal data: the typed identifier only as its keyed hash.
- Time comes from `TimeProvider`; tests use `FakeTimeProvider`.
- Generated files (`CLAUDE.md`, `.editorconfig`, `BannedSymbols.generated.txt`, `CodeMetricsConfig.txt`) are never hand-edited. A migration that has shipped is never changed; add a new one.

## Gotchas
- **A backend without egress uses the relay.** Set `Codelab:Identity:Sso:BackChannel=2` and `NoEgress=true`; with `NoEgress` true and the direct back channel the start fails outside Development. The relay setting is read at registration: changing it needs a restart.
- **`Codelab:Security:Tickets` never leaves the API.** That ring seals the SSO pending and exchange tickets and the activation tickets; it must never be deployed to another host. The attachment service gets only the separate `Codelab:Security:UploadTickets` ring.
- **A reused refresh token ends the session.** Presenting a refresh token that was already rotated is treated as theft: the session is revoked, so the legitimate holder must sign in again too. Clients must never send the same refresh token twice (serialize refreshes).
- **Refresh failures are one code.** Malformed, unknown, expired, idle, rotated, revoked, deactivated and version-bumped all answer `IDENTITY_REFRESH_REJECTED`, by design; the auth event says which.
- **A lockout is a denial-of-service lever too.** Anyone who knows a user name can lock it for `LockoutMinutes` (also the break-glass). That is the trade every lockout makes; keep the window short and watch the `AccountLockedOut` events.
- **Activation is not in the outbox here.** `IssueActivationAsync` returns the ticket; the caller (Users) builds the link and either shows it once to the administrator (an account without an e-mail address) or enqueues the `account-activation` outbox message with the ticket id only. At drain time the Users handler calls `IssueActivationAsync` **again** and e-mails that fresh ticket's link, so each delivery supersedes the request-time ticket (whose sealed string was never shown); the newest e-mailed link is the valid one and redemption stays one-time. The sealed ticket is never persisted (only its `ActivationTicketId`) or logged. The outbox is not transactional with this module's save.
- **The auth trail is written in its own DbContext**, so it never commits the deciding code's pending changes; a failed write is logged (the event line comes first) and the sign-in goes on.
- **Unicode identifiers are refused** for e-mail, login name and UPN (ASCII only): they are matched against external identities, and case folding could map a look-alike onto another person's account.
- **The bearer scheme is the default authentication scheme** and keeps claim names as issued (`MapInboundClaims = false`), so `sub` and `sid` stay `sub` and `sid` for `HttpCurrentUserContext` and the Codelab.Security middleware. Its token lifetime check reads the system clock, not `TimeProvider`.
- **The break-glass account is restored on every start**: re-activated, re-flagged, unlocked and given the configured password. Rotating the password means changing the configuration and restarting. A rotated password bumps the account's session version before the new hash is saved (a start that fails in between keeps the old hash, so the next start bumps again).
- **Migrations are scaffolded, then reshaped.** The clean-code rules apply to migrations too (no auto-generated marker, no `*.Designer.cs` name, named lengths, short methods). Scaffold with dotnet-ef 10 from a startup project that references `Microsoft.EntityFrameworkCore.Design` (`dotnet ef migrations add <Name> --project backend/src/Modules/Identity/Starter.Modules.Identity.csproj --startup-project backend/src/Starter.Api/Starter.Api.csproj --context IdentityDbContext --output-dir Infrastructure/Persistence/Migrations`), then reshape the output like `InitialCreate.cs`: ids in `IdentityMigrationIds`, store types in `IdentitySqlTypes`, the target model in `IdentityMigrationModel` (also the model snapshot). Freeze a copy of the model for the older migration first (as `InitialCreateModel` is). Unit tests fail when the model and the snapshot drift (`HasPendingModelChanges`) and when a migration lacks an operation of EF's own model diff. New non-null enum columns need an explicit default of a defined member (existing rows), never 0.
- **User PublicIds use the domain label `users`** (`Api/AuthController.cs`, `Users`). The label is part of the wire format: renaming it invalidates every outstanding user id.
