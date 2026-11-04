# Starter.ArchitectureSpike.Tests

## Purpose

This is the Phase 0 spike and the evidence for ADR-011, the choice of architecture-test library. It proves that
**NetArchTest.Rules** (the version pinned in `Directory.Packages.props`) loads and checks net10.0 assemblies. It
uses a two-project fixture: `Starter.ArchitectureSpike.Fixture.Domain` and `Starter.ArchitectureSpike.Fixture.Api`.

## Use

```powershell
E:\codelab-tools\dotnet10\dotnet.exe test tests/Starter.ArchitectureSpike.Tests
```

It runs in CI as part of `dotnet test codelab-starter.slnx` (build.ps1 step 6).

## Extend

Do not extend the spike. Real architecture rules belong to `Starter.Architecture.Tests` (P1), which reads
`rules/rules.compiled.json` through the `Codelab.Testing` helpers. Delete this project and both fixture projects
when that suite lands.

## Rules

- There are two tests. The first checks that the allowed direction passes: Domain does not depend on Api. The second
  checks that a real dependency is detected (Api depends on Domain), so a rule that can never fail does not count as
  green.

## Gotchas

- Result on 2026-11-04: NetArchTest.Rules 1.3.2 is green on net10.0 with SDK 10.0.403. The ArchUnitNET fallback
  (`TngTech.ArchUnitNET.xUnit`, also pinned) was not needed.
