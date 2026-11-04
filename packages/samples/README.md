# packages/samples

## Purpose

The **package integration test** (CI step 13). It proves the Codelab packages the way a fork consumes them: from the
feed only, never from source.

| Folder | What it is |
| --- | --- |
| `Package.Sample.Api/` | A small host that wires `Codelab.Observability`, `Codelab.Errors`, `Codelab.Security` and `Codelab.Integration` in a few lines of `Program.cs`. It has records with PublicIds, bearer sessions, an ES256 service-to-service endpoint, logout with `sid` revocation and an outbound archive client. |
| `Package.Sample.Tests/` | HTTP tests over `WebApplicationFactory`, with no browser. They cover the Phase 1 exit criteria: security headers, PublicId round trip and tamper, signing in Enforce (replay, body tamper, signed responses), S2S assertion replay, `sid` revocation, an outbound call to an untrusted certificate, a fail-fast boot with a published key, and PII-free logs carrying the correlation id and reference code (`Codelab.Testing` canary sink). There are also unit tests of the sample's own logic and the feed-only boundary checks. |
| `ng-security-consumer/` | A minimal npm project that installs `@codelab/ng-security` from the packed tarball in `feed/npm`. Its Vitest smoke test checks the published build against `packages/vectors`. |

The sample's own build boundary: `nuget.config` (`<clear/>`, `Codelab.*` only from `feed/nuget`), `Directory.Build.props`
and `.targets` (Codelab.Build.Sdk from the feed, `UseCodelabPackageSource=false`; the repo-root files are never
imported), `Directory.Packages.props` (its own central versions) and `Codelab.Versions.props` (the Codelab versions under
test).

## Use

```powershell
# What CI step 13 runs (also part of tools\build.ps1 -Target CI)
$env:NUGET_PACKAGES = "$env:TEMP\codelab-sample-$(New-Guid)"     # empty: the restore must come from the feed
E:\codelab-tools\dotnet10\dotnet.exe build packages\samples\Package.Sample.Tests\Package.Sample.Tests.csproj -warnaserror
E:\codelab-tools\dotnet10\dotnet.exe test  packages\samples\Package.Sample.Tests\Package.Sample.Tests.csproj --no-build
$env:PATH = 'E:\codelab-tools\node24;' + $env:PATH
npm ci --prefix packages\samples\ng-security-consumer; npm test --prefix packages\samples\ng-security-consumer

# Test against a prerelease without editing the pins (the property name is the package id without dots + Version)
dotnet test packages\samples\Package.Sample.Tests -p:CodelabSecurityVersion=0.5.0-ci.12 -p:CodelabBuildSdkVersion=0.2.0-ci.12 ...
```

Running the API by hand needs the key material a deployment takes from the environment. These keys are not in
`appsettings.json`: `Codelab:Security:PublicIds` and `:RequestSigning` rings, `Observability:UserIdHash:Key`,
`Sample:Authentication:SigningKeyBase64` and `Codelab:Security:ServiceAssertions:Server:TrustedServices`. See
`Package.Sample.Tests/Support/SampleSettings.cs` for the full list.

## Extend

- **A new package capability to prove:** add an endpoint to `Package.Sample.Api/Records/RecordEndpoints.cs` (route in
  `Hosting/SampleNames.cs`) and an HTTP test class next to the others. Drive it only through the package's public API.
- **A new package:** reference it by `PackageReference`, pin it in `Directory.Packages.props` with a
  `$(<IdWithoutDots>Version)` property, and add the property to `Codelab.Versions.props`. Step 13 refuses a NuGet
  package of `packages/versions.json` that has no pin there.
- **Moving to a new release:** after `tools\build.ps1 -Pack -Release`, set the versions in `Codelab.Versions.props`,
  point `ng-security-consumer/package.json` at the release tarball, then run `npm install` in the consumer to refresh
  its lock file.

## Rules

- **Feed only.** Never add a `ProjectReference` to a Codelab project, never add a sample to `codelab-starter.slnx`,
  and never import the repo-root `Directory.Build.props`. `FeedOnlyBoundaryTests` and step 13 (`Test-SampleBoundary`)
  fail otherwise.
- **No key material in the sample.** Tests generate keys at run time. The published-key test reads the TEST-ONLY
  fixture keys from `packages/vectors/fixture-keys` and expects the Production boot to refuse them.
- Starter names only (records and widgets). The sample follows the clean-code rules: constants for routes, sections
  and limits; source-generated logging; personal data masked before it reaches a template.

## Gotchas

- **Settings reach the host with `UseSetting`.** The packages read configuration while `Program` registers them, so a
  configuration source added in `ConfigureAppConfiguration` arrives too late.
- **The tests run one at a time.** The PublicId codec is process-wide (`UseCodelabSecurity` installs it).
- **Bearer claims keep their issued names** (`MapInboundClaims = false`), because Codelab.Security reads `sub`, `sid`,
  `skid` and `sver`. The observability user hash reads `NameIdentifier`, so the sample adds that claim from `sub`
  when the token is validated.
- **Minimal-API binding failures** (for example a tampered PublicId in the route) answer 400 with the error envelope
  only because the sample sets `RouteHandlerOptions.ThrowOnBadRequest = true`. Without it, ASP.NET Core writes an
  empty 400.
- **HSTS is not sent to `localhost`** (`Codelab:Security:Headers:Hsts:ExcludedHosts`), so the tests use
  `https://sample.test`.
- **A fresh clone has an empty feed.** Step 13 then packs a ci prerelease and tests against it: the NuGet versions
  through `-p:` overrides, and the npm consumer in a `%TEMP%` copy that installs the new tarball.
