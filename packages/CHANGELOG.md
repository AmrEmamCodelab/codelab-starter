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
