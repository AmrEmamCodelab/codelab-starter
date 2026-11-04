# ADR-012: Clean-code size and magic-number enforcement phasing

- **Status:** Proposed (needs the platform owner's acceptance)
- **Date:** 2026-11-04
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
4. Until an emitter reads a threshold, `rules/base.rules.yaml` marks it as "declared, not yet enforced (ADR-012)", so nobody mistakes it for an active gate.

## Consequences

- Phase 0 code can exceed the size limits without failing CI. Phase 0 has no application code, so the exposure is limited to tooling, which is reviewed by the platform owner.
- Phase 1 must deliver the analyzer before the first module merges. The phase 1 exit criteria gain: "a method over `methodLines`, a file over `fileLines`, a method with more than `parameters` parameters and a magic number each fail the build in a fixture project".
- When this ADR is accepted, the plan's "enforced from P0" line for these rules is superseded by this phasing.
