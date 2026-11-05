# Codelab packages changelog

All notable changes to the Codelab packages are recorded here, one section per package. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the versions follow [Semantic Versioning](https://semver.org/).

How versions work (see `tools/README.md`, "Versioning"):

- `packages/versions.json` holds the next release version of every package. It is the only version source.
- `tools\build.ps1 -Pack` publishes `<version>-ci.<n>` prereleases to `feed\` (the same `<n>` for all packages of a run).
- `tools\build.ps1 -Pack -Release` publishes the exact `versions.json` versions once. A package whose version is
  already in the feed and that has no `Unreleased` section here is released and unchanged: the release skips it. One
  that is in the feed but has an `Unreleased` section is refused (bump its version). On release, the package's
  `Unreleased` section below becomes `[<version>] - <date>`; every released package moves its
  `PublicAPI.Unshipped.txt` entries into `PublicAPI.Shipped.txt`.
- A version marked **Known insecure** must not be used; upgrade to the version named there.

Breaking changes are allowed before 1.0.0 but must be listed under **Changed** or **Removed** with the migration step.

## Codelab.Build.Sdk

### [0.2.1] - 2026-11-04

#### Added

- CLB1005 (error): an enum member without an explicit value. It matches the syntax, so doc comments and attributes
  on a member no longer hide it from the enum rule.
- `HttpClientHandler.DangerousAcceptAnyServerCertificateValidator` is in the global `BannedSymbols.txt`.
- CODELAB002 (error): `CodelabEnableBannedApis` and `CodelabEnableBuildAnalyzers` must be `true`. Any other value
  (`False`, `off`, empty) used to switch the gate off silently.
- CODELAB003 (error): `EnableNETAnalyzers` must be `true` and `AnalysisLevel` / `AnalysisMode` must not be `none`, so
  CA1502 (complexity) cannot be switched off from a project.

#### Changed

- CLB1004 skips a project only when its MSBuild project name ends in `.Tests` or `.Test`
  (`build_property.MSBuildProjectName`). `IsTestProject` is no longer trusted: referencing Microsoft.NET.Test.Sdk set
  it and switched the magic-number rule off for production code. **Migration:** a test project whose name does not
  end in `.Tests` or `.Test` is renamed, or its magic numbers are named.

### [0.2.0] - 2026-11-04

#### Added

- Codelab clean-code analyzers, shipped inside the SDK package under `analyzers/dotnet/cs` (ADR-012):
  CLB1001 method length, CLB1002 file length, CLB1003 parameter count and CLB1004 magic numbers. All four are errors.
  Thresholds come from the rules engine's generated `.editorconfig` per module, then `rules/codelab.globalconfig`.
- `Thread.Sleep` (both overloads) is in the global `BannedSymbols.txt`.
- Source mode references the analyzer project directly. Feed mode loads the packaged dll.

### [0.1.0] - prerelease only (Phase 0)

#### Added

- net10 defaults, nullable, warnings as errors, .NET analyzers, banned APIs (global and per-project generated
  list), public API tracking and package validation for Codelab packages, and release stamping
  (`CODELAB_RELEASE` and the short commit sha).

## Codelab.Security

### [Unreleased] - 0.5.2

#### Fixed

- Response signatures now cover error bodies written by the host's own error handling. `ResponseSignatureMiddleware`
  runs first in the pipeline (a startup filter registered by `AddCodelabSecurity`) instead of inside
  `UseCodelabSecurity`, so the problem+json body that `UseStatusCodePages` writes for an empty 401/403 and the body
  `UseExceptionHandler` writes for a 500 are hashed. Before, they were signed with the empty-body hash and every
  verifying client refused them (`response_signature_mismatch`). `HttpResponse.Clear()` now drops a failing endpoint's
  partial output from the signed body. **Migration:** none; `UseCodelabSecurity` no longer adds the response signer,
  and `AddCodelabSecurity` adds it.

### [0.5.1] - 2026-11-04

#### Fixed

- The service-assertion server refuses a published `TrustedServices[].PublicKeyPem` at start (the starter fixture
  public key had booted in Production, and its private half is committed).
- Published asymmetric keys are matched by key, not by text: the denylist also matches the SHA-256 of the DER
  `SubjectPublicKeyInfo`, so a one-line, re-wrapped, SEC1 or public-half spelling of a published key is refused.
- Request signing and service assertions hash an HTTP/2 or HTTP/3 body sent without `Content-Length` (it was
  hashed as empty, so an unsigned body passed as signed input).
- Request signing covers `Request.Path` without `PathBase`, the service-relative path @codelab/ng-security signs.
  Under an IIS child application every signed call was refused. **Migration:** none for hosts without a PathBase.
- Request signing and `sid`/`sver` revocation check the principal authorization will use. An endpoint whose policy
  names its authentication scheme (`[Authorize(AuthenticationSchemes = ...)]`) on a host with no default scheme, or
  another default, was authenticated only after `UseCodelabSecurity`, so unsigned calls and revoked sessions passed.
  `UseCodelabSecurity` now authenticates the policy's schemes first (call it after `UseRouting`).
- A principal is a service only when every authenticated identity was issued by the service-assertion scheme. A user
  token that carried a `codelab_service` claim skipped request signing and revocation.

#### Changed

- Outside Development, the memory session store and every user of the memory replay store (service-assertion
  server, sealed tickets, not only signing in Enforce) need `Codelab:Security:ReplayProtection:AcknowledgeSingleInstance`.
  **Migration:** a single-instance deployment sets it to `true`; a multi-instance one sets the stores to `SqlServer`.

### [0.5.0] - 2026-11-04 - Known insecure: do not use; upgrade to 0.5.1

In Production it boots with the committed starter fixture ES256 public key in `TrustedServices` (its private half is
in `packages/vectors/fixture-keys/`), it signs HTTP/2 bodies without `Content-Length` as empty, and the session and
signature checks can be bypassed (see 0.5.1, Fixed).

#### Added

- Configurable protocol prefix (`Codelab:Security:Protocol:Prefix`, required). It names the canonical form
  `{P}-CANON-v1`, the signatures `{P}-SIG-v1` (`/REQ`, `/RESP`, `/KDF`), `{P}-PUBLICID-v1`, the ticket, receipt and
  service-to-service profiles, and the `X-{P}-*` headers.
- Request and response signing in Off, Report and Enforce modes, plus the session signing-key endpoint
  `MapCodelabSigningKeyEndpoint()`.
- Replay guards: a bounded in-memory guard and a SQL Server guard. The SQL script is idempotent and EF-less; it is
  embedded and also shipped under `sql/`. Expired rows are removed through a purge job hook.
- PublicId opaque identifiers (AES-GCM): minimal-API binding, the MVC model binder, the route constraint, the
  built-in OpenAPI schema transformer and the JSON converter.
- Security headers, a CORS validator, the `RefuseOversizedBody` request body ceiling and a rate-limit partitioner
  that trusts the forwarded-for header only from listed proxies.
- Key-ring fail-fast guards with the published-material denylist on by default (source product key digests and the
  starter's own fixture keys).
- Sealed one-time tickets, one ES256 service-to-service assertion scheme (`jti` replay, method, path and body
  binding), upload safety primitives (magic bytes, caps, path containment, SHA-256 and HMAC receipts), and session
  revocation (a user-wide session-version store and a per-`sid` revocation store, in memory and SQL versions).
- One registration call: `AddCodelabSecurity` and `UseCodelabSecurity`.

## Codelab.Observability

### [0.5.1] - 2026-11-04

#### Fixed

- `UserIdHash` refuses a published key (a harvest-source key or a starter test fixture key) outside Development. The
  denylist data is Codelab.Security's `PublishedMaterial/*.json`, linked at build time (no package dependency).
- `AddCodelabObservability(serviceName)` takes the host assembly from `IHostEnvironment.ApplicationName`, not from
  `Assembly.GetEntryAssembly()`. Under a test host the entry assembly is the test runner, so every line's `Release`
  and the OTel `service.version` carried the test SDK's version.

### [0.5.0] - 2026-11-04 - Known insecure: do not use; upgrade to 0.5.1

Outside Development it accepts a starter test fixture key as the user-hash key (see 0.5.1, Fixed).

#### Added

- One Serilog host bootstrap (`AddCodelabObservability`, `UseCodelabObservability`, `CodelabHost.Run`). The root
  logger reads only the configured minimum levels; every configured sink sits behind the PII filter and the
  exception-text redaction.
- Opt-in OpenTelemetry (OTLP/HTTP) and an opt-in Seq sink (https only outside Development).
- Correlation id middleware, reference codes, `[Pii]` masking destructuring, the keyed user hash (no committed key),
  the Hangfire job failure log filter and the health endpoints `/health/live` and `/health/ready`.
- Outbound propagation denied by default. `AsInternalService()` forwards the trace and correlation headers and
  keeps certificate validation on.

## Codelab.Errors

### [0.5.1] - 2026-11-04

#### Changed

- Depends on Codelab.Observability 0.5.1 (published user-hash keys are refused outside Development). No API change;
  released in step with Observability because a pack gives a package and its project references one version.

### [0.5.0] - 2026-11-04

#### Added

- `GlobalExceptionHandler` with an ordered failure classifier table. Built-in classifiers cover request body,
  upstream, concurrency and transient SQL or timeout failures. Hosts add their own with `Map<T>`.
- One problem+json envelope (`code`, `correlationId`, `referenceCode`) for every error path, including framework
  status codes, model binding and `Results.Problem` bodies, cleaned by the client error scrubber.
- `CodedRefusal` for expected refusals returned as values.
- Pipeline order: `UseCodelabObservability()` first, then `UseCodelabErrors()`.

## Codelab.Identity

### [Unreleased] - 0.5.0

#### Added

- Active Directory over LDAPS (`IActiveDirectoryAuthenticator`): binds as the user, then searches the user's own entry;
  LDAP result codes and AD sub-codes map to `DirectoryAuthStatus`; a timeout; the `ILdapConnectionFactory` test seam;
  `LdapsCertificateReader`; `IActiveDirectoryConnectionProbe`; `ActiveDirectoryDeploymentGuard`.
- Generic OIDC code flow with PKCE S256 (`IOidcIdentityProvider`): JWKS or pinned signing keys, an asymmetric-only
  algorithm allowlist (none and HS* refused), issuer/audience/azp/nonce/lifetime checks on `TimeProvider`, and the
  end_session URL with `id_token_hint`; `SsoDeploymentGuard`.
- `LoginMatchField` / `LoginMatch` (email, login name or UPN) and `AddCodelabActiveDirectory` / `AddCodelabOidc`.

#### Security

- Active Directory sign-in refuses a `DOMAIN\user` whose prefix is not `NetBiosName` and a `user@suffix` whose suffix
  is not `Domain` or in `UpnSuffixes`, before the bind (a trusted-domain account could resolve to a local namesake).
  New options `NetBiosName` and `UpnSuffixes`; log event 3106. Breaking: deployments whose users type `DOMAIN\user`
  must set `NetBiosName`.
- Active Directory sign-in checks that the account the password was bound as is the entry the search found, using
  RFC 4532 "Who am I?" (new `ILdapConnection.WhoAmI()`, default `null`; `SystemLdapConnection` implements it). A
  mismatch is `InvalidCredentials` with log event 3107. A connection without `WhoAmI` gets a weaker check: a found
  entry whose `userPrincipalName` is present and differs from the bind name is refused. Possibly breaking: if the
  directory server refuses the "Who am I?" request, every AD sign-in becomes `Unavailable`.

## Codelab.Integration

### [0.1.0] - 2026-11-04

#### Added

- Typed outbound clients (`AddCodelabOutboundClient<TClient>`) with options validated at start-up.
- Retry and timeout policy on Microsoft.Extensions.Http.Resilience. Only idempotent requests are retried, only after
  a transient failure. Retry-After is honoured and capped.
- Outbound failure classification (`OutboundFailureKind`).
- Server certificate validation always on: system trust, a pinned internal CA bundle or pinned leaf thumbprints.
  There is no accept-everything path.
- A correlation handler slot (`AddCodelabOutboundHandler<THandler>`).

## Codelab.Testing

### [0.5.0] - 2026-11-04

#### Added

- Architecture checks driven by `rules/rules.compiled.json`: module boundaries, layer rules and assembly purity.
- A source-sweep runner, a PII-leak canary Serilog sink and a golden-vector runner that takes the protocol prefix
  as a parameter.
- Endpoint contract checks: every endpoint has a permission and a validator, every id is a PublicId, and every error
  is problem+json.

## @codelab/ng-security

### [0.5.1] - 2026-11-04

#### Changed

- The package documentation moved to `MODULE.md` (shipped in the package); `README.md` points to it. The package is a
  rules-engine module (`module.rules.yaml`), linted in CI (magic numbers, module boundaries). No API or behaviour
  change.

### [0.5.0] - 2026-11-04

#### Added

- `createSecurityProtocol(prefix)`, byte-compatible with the C# framing for any prefix.
- Canonical framing, the request signing interceptor (one re-sign on `signature_stale`, fail closed) and the
  response verification interceptor.
- The session signing-key store (non-extractable keys, injected clock), `WireId<D>` branded ids, the problem+json
  error parser and `provideCodelabSecurity()`.
- A second entry point, `@codelab/ng-security/core`, with no Angular imports, so Node tools can use it.

## @codelab/ng-tools

### [Unreleased] - 0.5.0

#### Added

- `codelab-ng-tools rtl-lint <folder> [--icons <map>] [--direction-file <glob>]...`: physical-side style bindings, inline
  styles, text-align/float/clear left/right, hard-coded dir and physically named or unmirrored icons in templates and
  TypeScript (base.fe-tokens-logical-css).
- `codelab-ng-tools icons-subset`: the icons an app uses (unknown literal names fail), and the subset code point map and
  WOFF2 through the ng-dga generator contract.
- `codelab-ng-tools i18n-check`: translation parity, Arabic text in ar and every key used in code present (moved from
  the app's i18n-parity script).
- One dispatcher for every codelab-ng-tools subcommand; root exports `checkRtl`, `collectIconUsage`, `subsetOutputs`,
  `checkI18n`, `runCodelabNgTools`, `ExitCodes`.

#### Changed

- The boundary preset no longer passes the deprecated `mode: 'folder'` to eslint-plugin-boundaries (behaviour
  unchanged).

#### Added (0.2.0)

- `@codelab/ng-tools/routes`: `assertRoutesGated` / `findUngatedRoutes` / `formatRouteFindings`, the route-table
  permission gate for a Vitest spec. Every route rendering a page carries a permission key in route data on itself or
  an ancestor, or its full path is public. Redirects are exempt. Lazy child arrays are passed in `lazyChildren` (a
  missing one fails). Optional `knownPermissions`. TypeScript declarations included.
- `@codelab/ng-tools/permission-gates` and the `codelab-ng-tools permission-gates --dir <folder> [--fix <text>]` bin:
  every `<dga-button>`, and every `<button>` with `(click)`, in templates and inline templates carries
  `*hasPermission` (itself, an ancestor, or `[hasPermission]` on `<ng-template>`) or `data-public-action`.
  String-literal keys are refused. Exit 0/1/2.
- The route gate also requires a permission guard (`missing-guard`), tracked per key: the route that declares a page's
  innermost key runs it in `canActivate` or `canMatch`, or an ancestor strictly above that route runs it in
  `canActivateChild` (a route's own `canActivateChild` does not count for itself), with a guard tagged with
  `Symbol.for('codelab.ng-tools.permission-guard')` (`markPermissionGuard()`, `PERMISSION_GUARD_MARKER`), or one the
  `isPermissionGuard` option accepts. Breaking for consumers whose guards are not tagged.
- The template gate counts more action elements: a `<button>` with any event binding (including `on-x` and
  `(keyup.enter)`), a submit button or a typeless one inside a submitting form, a `role=button` element with an event
  binding, and any element that binds `(click)` or `(confirm)`. `template: NAME` inline templates are scanned when the
  file declares `NAME` as a string.

### [0.1.1] - 2026-11-04

#### Changed

- The package documentation moved to `MODULE.md` (shipped in the package); `README.md` points to it. The package is a
  rules-engine module (`module.rules.yaml`), linted in CI. No API or behaviour change.

### [0.1.0] - 2026-11-04

#### Added

- ESLint boundary preset (`core`, `layouts`, `patterns`, `shared`, `features/*`, `showcase`) driven by
  `rules/eslint-boundaries.compiled.json`, with third-party UI libraries banned.
- The `codelab/wire-id-cast` rule.
- Stylelint preset: design tokens only, logical CSS properties.
- The `codelab-vectors-sync` command, which copies `packages/vectors` into a consumer.

### [0.0.1] - prerelease only (Phase 0)

#### Added

- Placeholder package to prove the npm feed.

## @codelab/ng-dga

### [Unreleased] - 1.0.0

The 1.0 release candidate (Phase 4). `-Pack` publishes it as `1.0.0-ci.<n>` prereleases; the catalog's `since`
values name this line. 1.0.0 itself is released in Phase 10.

#### Added

- Components: `dga-accordion` and `dga-accordion-item` (single or multiple open sections), `dga-alert` (info,
  success, warning and error tones, optional close), `dga-header` (brand, one-level submenu navigation, start and
  actions slots, collapses below the md breakpoint), `dga-hero` and `dga-hero-slider` (slides with previous/next,
  picker dots and an optional autoplay with pause), `dga-stamp-strip` (the official-government-website statement with
  its verification disclosure), `dga-select` and `dga-textarea` (live character counter, opt-in auto-resize).
- Generic widgets moved into the library from the app: `dga-confirm-dialog`, `dga-error-dialog` (reference code with
  copy), `dga-page-feedback`, `dga-portal-page-header`, `dga-file-upload` (client-side type, size, empty and count
  checks; it sends nothing), `dga-date-input` (ISO Gregorian), `dga-hijri-date-input` (Umm al-Qura, with
  `hijriIsoToGregorianIso` / `gregorianIsoToHijriIso`), `dga-unit-input` (Arabic-Indic digits), `dga-multi-select`
  and `dga-geo-select` (region, city and district from a `DgaGeoDataSource`; no HTTP).
- The branding layer: `DgaBranding`, `parseDgaBranding`, `applyDgaBranding`, `provideDgaBranding` and `DGA_BRANDING`
  set the `--dga-brand-*` custom properties, which `styles/_branding.scss` maps onto the semantic primary tokens.
  Without branding every fallback is the package's own DGA green.
- Tokens: `--dga-shadow-lg`, `--dga-duration-slow`, `--dga-color-hero-overlay`, the header, logo, menu, hero and
  stamp-strip sizes, and the header and dropdown z-indexes.
- 22 icons in the owned icon font (bell, calendar, clock, download, email, error, external-link, file, filter,
  location, minus, more-vertical, pause, phone, play, plus, question, shield-check, success, trash, upload, warning).
- The component catalog: a `<selector>.catalog.json` beside every component (schema `catalog.schema.json`), turned
  into `docs/design/component-catalog.md` by `catalog:generate` and checked by `catalog:check`. The token reference
  `docs/design/tokens.md` is generated from the token sources by `tokens:generate` / `tokens:check`. Both checks run
  in the package `test` script.

#### Changed

- `dga-stepper` merges the former library and app steppers: `orientation` (horizontal or vertical), `navigable`
  steps that emit `stepSelect` with a roving tabindex, `linear`, `compact`, and per-step `completed`, `invalid` and
  `disabled` with an `invalidLabel`. `DgaStepState` is now a constant object plus its type, with an `invalid` state.
  **Migration:** none for a progress-only stepper; string values `'completed' | 'current' | 'upcoming'` still match.

### [0.1.0] - prerelease only (Phase 2)

#### Added

- First release of the owned DGA component library (decision D5), in Angular package format (ng-packagr, partial
  compilation) for Angular 22.
- Styles entry `@use '@codelab/ng-dga/styles'`: `--dga-*` design tokens on `:root`, reset, base, typography, grid and
  spacing helpers, the `media-up`, `visually-hidden` and `focus-ring` Sass helpers, and the precompiled
  `dist/ng-dga.css` for apps without Sass.
- Self-hosted IBM Plex Sans Arabic and IBM Plex Sans (OFL-1.1), and the owned `Codelab Icons` font generated
  deterministically from Bootstrap Icons (MIT) SVGs with fixed Private Use Area code points. No remote URL in the
  built CSS or JavaScript.
- Components (standalone, OnPush, signal inputs, accessible, RTL-correct, no built-in text): `dga-button`,
  `dga-card`, `dga-tag`, `dga-status-tag`, `dga-tabs`, `dga-stepper`, `dga-breadcrumb`, `dga-text-input`,
  `dga-checkbox`, `dga-radio-group`, `dga-info-field` and `dga-icon`. The form controls are signal-model controls
  (`[(value)]` / `[(checked)]`, `[(touched)]`) that also serve as signal-forms field controls.
- `assets/LICENCES.json` with every asset's source, version, licence and sha256, checked against ADR-009 by the
  package tests; `assets:import` / `assets:check` and `icons:generate` / `icons:check` scripts.
