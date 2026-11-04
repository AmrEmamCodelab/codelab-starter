# ADR-012: Clean-code size and magic-number enforcement phasing

- **Status:** Accepted (2026-11-04)
- **Date:** 2026-11-04 (proposed and accepted)
- **Deciders:** Amr Emam (platform owner)

## Context

The build plan says the clean-code rules are "enforced from P0": method and file length as warnings-as-errors, and an analyzer for magic numbers. In phase 0, `rules/base.rules.yaml` declares `thresholds.methodLines` (40), `fileLines` (400) and `parameters` (5), but no emitter reads them. Only `cyclomaticComplexity` reaches the build (CA1502 through the generated `CodeMetricsConfig.txt`). `base.no-magic-values` is declared with `availableFrom: P1`. Declaring a limit that nothing checks gives a false sense of enforcement.

The .NET SDK has no built-in method-length, file-length or magic-number diagnostic. Enforcing them needs our own Roslyn analyzers (or a third-party analyzer package that must be vetted for the offline feed). Phase 0 has no application code, so there is nothing to measure yet.

## Decision

1. **Phase 0 enforces:** cyclomatic complexity (CA1502, threshold from `thresholds.cyclomaticComplexity`), banned APIs (RS0030), and `base.no-analyzer-opt-out`, which stops anyone from switching those analyzers off (`CodelabEnableBannedApis=false`, `NoWarn`, `#pragma warning disable`, `.editorconfig` severity none/silent/suggestion).
2. **Deferred to phase 1, before the first module lands:**
   - `methodLines`, `fileLines` and `parameters` for C#: a Codelab analyzer (shipped in a `Codelab.*` analyzer package through the feed) reads the thresholds from the generated per-module `.editorconfig`, and reports them as build errors under `-warnaserror`.
   - `base.no-magic-values` for C#: the same analyzer package, honouring `settings.allowedNumbers` and `excludeTests`.
3. **Deferred to phase 2 (frontend):** ESLint `max-lines`, `max-lines-per-function`, `max-params` and `no-magic-numbers`, configured from the same thresholds by the rules engine's ESLint emitter.
4. Until an emitter reads a threshold, `rules/base.rules.yaml` marks it as "declared, not yet enforced (ADR-012)", so nobody mistakes it for an active gate. (Done in phase 1 for the C# thresholds; see the acceptance note.)

## Consequences

- Phase 0 code can exceed the size limits without failing CI. Phase 0 has no application code, so the exposure is limited to tooling, which is reviewed by the platform owner.
- Phase 1 must deliver the analyzer before the first module merges. The phase 1 exit criteria gain: "a method over `methodLines`, a file over `fileLines`, a method with more than `parameters` parameters and a magic number each fail the build in a fixture project".
- When this ADR is accepted, the plan's "enforced from P0" line for these rules is superseded by this phasing.

## Acceptance note (phase 1, 2026-11-04)

Accepted by the platform owner as a standing decision for phase 1. What landed, before any package code:

- `packages/dotnet/Codelab.Build.Analyzers` (netstandard2.0, Roslyn 5.0.0): CLB1001 method lines, CLB1002 file lines, CLB1003 parameters, CLB1004 magic numbers, all errors by default. Lines are counted as lines that hold code (blank, comment-only and preprocessor lines do not count). CLB1004 allows the configured numbers (default -1, 0, 1) and literals the code already names (const and readonly field initializers, property initializers, enum members, attribute arguments, parameter defaults), and skips test projects when `excludeTests` is true.
- Codelab.Build.Sdk wires it into every project: from source in the starter (a ProjectReference used as an Analyzer), from `analyzers/dotnet/cs` inside the Codelab.Build.Sdk package in feed mode. There is no separate analyzer package. The same change moved the `Thread.Sleep` ban (both overloads) into the SDK's global `BannedSymbols.txt`.
- The rules engine emits the thresholds as `codelab_*` keys: into each module's generated `.editorconfig` (module overrides) and into `rules/codelab.globalconfig` (base values for every project, including `tools/` and `tests/`). `base.no-analyzer-opt-out` now also covers the CLB ids and `CodelabEnableBuildAnalyzers`, and `base.no-threshold-override` refuses hand-written `codelab_*` keys.
- Exit criterion met: the `SdkBuildTests` in `Codelab.Build.Analyzers.Tests` build a fixture project with the real SDK, and each of the four violations fails `dotnet build -warnaserror` with its own id.
- Item 3 (ESLint size and magic-number rules) is still phase 2.

## Review follow-up (phase 1, 2026-11-04)

A phase 1 review found opt-outs that the rule did not catch, and a text rule that the normal coding style gets past:

- `base.no-analyzer-opt-out` now also refuses a bare `#pragma warning disable` (no ids), category severities (`dotnet_analyzer_diagnostic.category-*.severity = none`), `generated_code = true`, `<TreatWarningsAsErrors>false`, `<WarningsNotAsErrors>` with a gate id, and `<RunAnalyzers>false`. The CLB analyzers skip generated code, so new sibling rules refuse a hand-written `<auto-generated>` header (`base.no-auto-generated-marker`), generated-looking file names such as `*.g.cs` (`base.no-generated-file-name`), and `<IsTestProject>true` outside a `*.Tests` project (`base.no-test-project-opt-in`). Analyzing generated code was rejected: real generator output (Regex, logging, OpenAPI) would fail the build.
- Phase 1 review, round 2: the gate switches were refused only for the literal `false`, while the SDK enables a gate only for exactly `true`. `base.no-analyzer-opt-out` now refuses any other value (case-insensitive), `EnableNETAnalyzers` off and an `AnalysisLevel`/`AnalysisMode` of `none`, and the SDK itself fails the build (`CODELAB002`, `CODELAB003`). CLB1004 decides "test project" by the MSBuild project name (`*.Tests`), not by `IsTestProject` (Microsoft.NET.Test.Sdk sets it), and `base.no-test-project-opt-in` also refuses a test-framework reference or a `*.Tests` assembly name in production code. `codelab_*` threshold keys are allowed only in the engine's outputs chosen by path (`rules/codelab.globalconfig`, each module's `.editorconfig`; `exemptEmittedFiles`), never by a GENERATED marker in the content, and any other `.globalconfig` is refused (`base.no-hand-written-globalconfig`).
- CLB1005 (enum member without an explicit value) is the build gate for `base.enums-as-int`. The text rule `base.enum-explicit-values` stays as an early hook warning, because a regex cannot see past doc comments and attributes.
- The coverage thresholds (`backendLineCoverage`, `frontendLineCoverage`) are declared, not yet enforced: no step measures coverage before phase 2, so `base.unit-tests-mandatory` moved to `availableFrom: P2`.
