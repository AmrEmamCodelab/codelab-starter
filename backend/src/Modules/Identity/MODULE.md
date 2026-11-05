# Identity

## Purpose
Module 1 (phase 2 minimum): who the caller is and how a session lives.

- **Basic login:** user name and password, hashed with the ASP.NET Core Identity `PasswordHasher` (PBKDF2, from the shared framework). Every credential failure (unknown user, wrong password, deactivated account) answers the same `IDENTITY_INVALID_CREDENTIALS`, and an unknown user costs the same hashing work (a decoy hash).
- **One session issuer** (`Application/SessionIssuer.cs`): a JWT access token (HMAC-SHA256) carrying `sub` (the account's `SubjectId`), `sid`, `sver` (the session version), `jti` and, while request signing is on, `skid`; never a permission or a role. A refresh token `<sid>.<secret>` (only the SHA-256 of the secret is stored) that every refresh rotates. The idle timeout travels in the login response.
- **Logout** ends the session and revokes its `sid` in the Codelab.Security revocation store until its last access token expires; the bearer scheme refuses a revoked `sid` on every request (401).
- **Accounts from configuration:** the break-glass administrator (holds every permission) and, in Development only, low-privilege Development users that hold `dashboard.view`.
- **`IUserDirectory`** for the other modules: internal id, active flag, break-glass flag, looked up by token subject.

Tables (schema `identity`, one `InitialCreate` migration): `UserAccounts`, `UserSessions`.

## Use
HTTP (all under `/api/auth`, JSON, errors as Codelab.Errors problem+json):

| Endpoint | Access | Body | Answer |
| --- | --- | --- | --- |
| `POST /api/auth/login` | anonymous | `{userName, password}` | 200 `{accessToken, refreshToken, accessTokenExpiresAt, idleTimeoutMinutes, user:{id (PublicId), displayName}}`; 400 `VALIDATION_FAILED` or `IDENTITY_INVALID_CREDENTIALS` |
| `POST /api/auth/refresh` | anonymous | `{refreshToken}` | 200, same shape, new refresh token; 400 `IDENTITY_REFRESH_REJECTED` |
| `POST /api/auth/logout` | `[AuthenticatedUser]` | none | 204; later requests with that access token get 401 |

Every `/api/auth` action is under the Codelab.Security per-client rate limit (`[EnableRateLimiting(ClientRateLimitOptions.PolicyName)]`, tuned by `Codelab:Security:RateLimiting`); past the limit the answer is 429 `rate_limited`. A contract test (`AllAnonymousWritesAreRateLimited`) fails any anonymous write without it.

Configuration (every key through an options class with `SectionName`, validated on start):

| Key | Notes |
| --- | --- |
| `Identity:Session:AccessTokenMinutes` / `RefreshTokenDays` / `IdleTimeoutMinutes` | Defaults 15 / 7 / 30. The access token may not outlive the idle timeout. |
| `Identity:Jwt:SigningKey` | Base64 of at least 32 random bytes. Local secrets file or environment only. |
| `Identity:Jwt:Issuer` / `Audience` | Optional; default to the host's service name (`starter-api`). |
| `Identity:BreakGlass:UserName` / `DisplayName` / `Password` | User name defaults to `admin`. The password has no default: the host does not start without it, nor with one shorter than 16 characters or with fewer than 8 distinct characters (`IdentityLimits.BreakGlassPasswordMinLength`, `BreakGlassPasswordMinDistinctCharacters`; every environment). |
| `Identity:DevUsers:<n>:UserName` / `Password` / `DisplayName` | Development only; ignored in every other environment. |

In code: `IUserDirectory.FindBySubjectAsync(subjectId)` (inject it; never reference this project from another module).

## Extend
- **A new sign-in mode** (directory, SSO): add a use case that authenticates and then calls `SessionIssuer.StartAsync(user)`. Never sign a token or write a session anywhere else.
- **Ending every session of a user** (password change, deactivation, permission change): call `ISessionVersions.BumpAsync(subjectId)` (over the Codelab.Security session version store). Open sessions then fail their next refresh, and their access tokens fail at the session middleware.
- **A new account field:** change `Domain/UserAccount.cs` and `Infrastructure/Persistence/IdentityDbContext.cs`, then add a migration with dotnet-ef (see Gotchas). The kernel applies it on start in Development; other environments use the DBA scripts.
- **A new error:** add a constant to `Application/IdentityErrorCodes.cs` and `errors.<CODE>` to both the `ar` and `en` bundles.

## Rules
- Identity depends on the kernel and the Codelab packages only; it never references Access (`module.rules.yaml`). The keys a Development user receives are mirrored in `DevUserPermissionKeys`; `IUserPermissionGrants` refuses a key the catalog lacks, so drift fails the Development start.
- Layers by namespace: `Domain` (entities, token shape), `Application` (use cases, validators, ports, the session issuer), `Infrastructure` (EF Core, JWT, hasher, security-store adapters, options, the startup task, `IdentityModule`), `Api` (thin `AuthController` and DTOs).
- No password, signing key or secret has a default in code or in a committed settings file. Options, seed records and tokens print no secret in `ToString()`.
- Time comes from `TimeProvider`; tests use `FakeTimeProvider`.
- Generated files (`CLAUDE.md`, `.editorconfig`, `BannedSymbols.generated.txt`, `CodeMetricsConfig.txt`) are never hand-edited. A migration that has shipped is never changed; add a new one.

## Gotchas
- **A reused refresh token ends the session.** Presenting a refresh token that was already rotated is treated as theft: the session is revoked, so the legitimate holder must sign in again too. Clients must never send the same refresh token twice (serialize refreshes).
- **Refresh failures are one code.** Malformed, unknown, expired, idle, rotated, revoked, deactivated and version-bumped all answer `IDENTITY_REFRESH_REJECTED`, by design.
- **The bearer scheme is the default authentication scheme** and keeps claim names as issued (`MapInboundClaims = false`), so `sub` and `sid` stay `sub` and `sid` for `HttpCurrentUserContext` and the Codelab.Security middleware.
- **The break-glass account is restored on every start**: re-activated, re-flagged and given the configured password. Rotating the password means changing the configuration and restarting. A rotated password bumps the account's session version before the new hash is saved (a start that fails in between keeps the old hash, so the next start bumps again), so every session opened with the old password fails its next refresh and its access tokens are refused.
- **Migrations are scaffolded, then reshaped.** The clean-code rules apply to migrations too (no auto-generated marker, no `*.Designer.cs` name, named lengths, short methods). Scaffold with dotnet-ef 10 from a startup project that references `Microsoft.EntityFrameworkCore.Design` (`dotnet ef migrations add <Name> --project backend/src/Modules/Identity/Starter.Modules.Identity.csproj --startup-project backend/src/Starter.Api/Starter.Api.csproj --context IdentityDbContext --output-dir Infrastructure/Persistence/Migrations`), then reshape the output like `InitialCreate.cs`: ids in `IdentityMigrationIds`, store types in `IdentitySqlTypes`, the target model in `IdentityMigrationModel` (also the model snapshot). Freeze a copy of the model for the older migration before changing `IdentityMigrationModel`. A unit test fails when the model and the migrations drift (`HasPendingModelChanges`).
- **User PublicIds use the domain label `users`** (`Api/AuthController.cs`, `Users`). The label is part of the wire format: renaming it invalidates every outstanding user id.
