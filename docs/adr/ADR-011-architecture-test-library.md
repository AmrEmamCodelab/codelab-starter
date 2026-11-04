# ADR-011: Architecture-test library (NetArchTest, fallback ArchUnitNET)

- **Status:** Accepted. The primary choice is conditional on the phase 0 net10.0 spike.
- **Date:** 2026-11-04
- **Deciders:** Amr Emam (platform owner)

## Context

ADR-004 enforces layering inside each module by namespace, so architecture tests are mandatory. The starter targets .NET 10. NetArchTest.Rules is small and readable, but its last release predates .NET 10. TngTech.ArchUnitNET is more capable and actively maintained, but heavier. Architecture-test logic must live in one place (`Codelab.Testing`, phase 1) and read `rules/rules.compiled.json`.

## Decision

1. **Primary: NetArchTest.Rules 1.3.2**, pinned in `Directory.Packages.props`. Phase 0 verifies it on `net10.0` with a spike test; this is a phase 0 exit criterion.
2. **Fallback: TngTech.ArchUnitNET.xUnit 0.13.5**, also pinned. It is used if the spike fails or a later .NET release breaks NetArchTest.
3. **xUnit v2 (2.9.3)** with `xunit.runner.visualstudio` 3.1.5, because ArchUnitNET.xUnit depends on `xunit.assert` 2.x. xUnit v3 can be adopted later if ArchUnitNET supports it.
4. Tests call helpers in `Codelab.Testing`. No test project calls NetArchTest or ArchUnitNET directly, so switching libraries touches one package.

## Consequences

- The library choice is isolated behind `Codelab.Testing`.
- The spike result is recorded in the phase 0 report. If the spike fails, this ADR is amended to make ArchUnitNET the primary library.
