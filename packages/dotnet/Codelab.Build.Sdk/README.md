# Codelab.Build.Sdk

## Purpose

One MSBuild SDK that gives every Codelab .NET project the same build rules:

- `$(CodelabTargetFramework)` = `net10.0`, `Nullable`, `ImplicitUsings`, latest C#.
- `TreatWarningsAsErrors`, .NET analyzers at `latest-recommended`, code style enforced in build.
- Banned APIs (RS0030 = error): a global `Sdk/BannedSymbols.txt` (`DateTime.Now`, `DateTime.UtcNow`,
  `DateTime.Today`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow` -> inject `TimeProvider`; both `Thread.Sleep`
  overloads -> `await Task.Delay(delay, timeProvider, cancellationToken)`;
  `HttpClientHandler.DangerousAcceptAnyServerCertificateValidator` -> Codelab.Integration `ServerCertificatePolicy`), plus a per-project
  `BannedSymbols.generated.txt` (emitted by `tools/rules-engine` from the module's rules) when that file exists.
- Codelab clean-code analyzers (ADR-012, `Codelab.Build.Analyzers`, errors by default): CLB1001 method too long,
  CLB1002 file too long, CLB1003 too many parameters, CLB1004 magic number, CLB1005 enum member without an explicit
  value. Thresholds are the `codelab_*` keys the
  rules engine emits: a module's generated `.editorconfig` first, then `rules/codelab.globalconfig` (base values,
  passed as `GlobalAnalyzerConfigFiles` through `CodelabRulesGlobalConfig`), then the analyzer's built-in defaults.
  `MSBuildProjectName` is made visible to the analyzer so CLB1004 can skip `*.Tests` projects. The analyzer dll ships in this
  package at `analyzers/dotnet/cs`; in the starter monorepo the analyzer project is referenced as an Analyzer.
- For Codelab packages (`<CodelabPackage>true</CodelabPackage>`): Microsoft.CodeAnalysis.PublicApiAnalyzers with
  `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`, and NuGet package validation against
  `<CodelabPackageBaselineVersion>`.
- Release stamping: `CODELAB_RELEASE` (semver, default `0.0.0-local`) + short commit sha, so every assembly,
  the host included, reports `InformationalVersion = <semver>+<sha7>` and carries
  `AssemblyMetadata("CodelabRelease", ...)`.

## Use

In a fork (feed mode), pin the version and add the SDK to each project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Codelab.Build.Sdk" Version="0.2.0" />
  <PropertyGroup>
    <TargetFramework>$(CodelabTargetFramework)</TargetFramework>
  </PropertyGroup>
</Project>
```

Or once for the whole repo, in `Directory.Build.props`/`Directory.Build.targets`, with
`-p:UseCodelabPackageSource=false -p:CodelabBuildSdkVersion=<version>` (see the starter's root files).

Inside the starter monorepo nothing is needed: the root `Directory.Build.props`/`.targets` import
`Sdk/Sdk.props` and `Sdk/Sdk.targets` from source (ADR-002, `UseCodelabPackageSource=true`).

Analyzer versions: under Central Package Management (the starter default) `Directory.Packages.props` must contain
`Microsoft.CodeAnalysis.BannedApiAnalyzers` (and `Microsoft.CodeAnalysis.PublicApiAnalyzers` if any project sets
`CodelabPackage`). Without CPM the SDK supplies its own pinned versions.

Release builds:

```powershell
$env:CODELAB_RELEASE = '1.4.0'        # or -p:CodelabRelease=1.4.0
$env:CODELAB_COMMIT  = (git rev-parse HEAD)   # optional; otherwise read from git by the .NET SDK
dotnet publish ...
```

Pack: `tools/build.ps1 -Pack` (writes `Codelab.Build.Sdk.<base>-ci.<n>.nupkg` to `feed/nuget`, with the analyzer dll
inside; the base version is `CodelabBuildSdkBaseVersion` in the csproj, 0.2.0 since the analyzers landed).

## Extend

- New global ban: add a line `<doc id>;<fix message (rule id)>` to `Sdk/BannedSymbols.txt`. Module-specific bans go
  in the module's `module.rules.yaml`, never here.
- New clean-code diagnostic: add it to `packages/dotnet/Codelab.Build.Analyzers` (descriptor, analyzer, a row in
  `AnalyzerReleases.Unshipped.md`, unit tests and, for a gate, a case in the `SdkBuildTests` fixture builds). A new
  threshold key goes into `Options/OptionKeys.cs`, the rules engine's `emitters/analyzer-settings.mjs` and the
  schema together; the rules-engine and analyzer tests fail when they drift apart.
- New default property: put it in `Sdk/Sdk.props` with a `Condition="'$(X)' == ''"` guard when a project may
  override it; put anything that must see project settings (items keyed on project properties) in `Sdk/Sdk.targets`.
- Bump the package version (`CodelabBuildSdkBaseVersion` in the csproj) for every behaviour change; forks only get
  SDK fixes by a version bump.

## Rules

- Platform-owner review is required for every change here (CODEOWNERS covers `packages/`).
- Props never read project-body properties (they are not set yet); targets never set defaults the project should
  be able to override.
- No inline `PackageReference` versions when CPM is on: versions belong in `Directory.Packages.props`.
- The packaging project compiles nothing and ships no dependencies (`SuppressDependenciesWhenPacking`).

## Gotchas

- Switching the gates off (`CodelabEnableBannedApis`/`CodelabEnableBuildAnalyzers` false, `NoWarn`, `#pragma` or
  severity none for RS0030/CA1502/CLB*) and hand-written `codelab_*` keys are refused by the content rules
  (`base.no-analyzer-opt-out`, `base.no-threshold-override`). Change thresholds in the rules files instead.
- The analyzer references Roslyn 5.0.0 (the .NET 10 GA compiler) so every .NET 10 SDK can load it; never raise it
  above the compiler of the SDK pinned in `global.json`, or the analyzer fails to load (CS8032).
- In source mode a project in the analyzer's own folder never references itself; projects outside the repo that
  import the repo's `Directory.Build.props` (the test fixtures) still get the analyzer by project reference.

- `TargetFramework` is not defaulted (a default in props would break projects that set `TargetFrameworks`); use
  `$(CodelabTargetFramework)`.
- `GenerateDocumentationFile` is on so IDE0005 runs in the build; CS1591 (missing XML docs) is suppressed.
- `CODELAB_RELEASE` must be semver (`1.4.0`, `1.4.0-rc.2`) or the build fails with `CODELAB001`.
- The quality gates cannot be switched off from a project: `CodelabEnableBannedApis` and `CodelabEnableBuildAnalyzers`
  other than `true` fail with `CODELAB002`; `EnableNETAnalyzers` other than `true`, or an `AnalysisLevel` /
  `AnalysisMode` of `none`, fail with `CODELAB003`. Only the SDK's own fixture builds pass
  `-p:CodelabFixtureGateOptOut=true` (`base.no-fixture-gate-opt-out` refuses that name anywhere else).
- The feed-mode `Import ... Sdk="Codelab.Build.Sdk"` needs `CodelabBuildSdkVersion` and the Codelab feed in
  `nuget.config` (package source mapping sends `Codelab.*` only to that feed).
