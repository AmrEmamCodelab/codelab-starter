# Codelab.Testing

## Purpose

The test helpers every starter test project uses. This package is the single owner of the architecture-test logic
(ADR-011). It is test-framework neutral: every check returns a `CheckResult` (title, examined count, violations), and
`ThrowIfFailed()` throws `ContractViolationException` with every violation on its own line. A check that examined
nothing fails as vacuous, so a narrowed scope never passes silently.

- **Architecture rules from `rules/rules.compiled.json`** (NetArchTest.Rules, the only file calling it is
  `Architecture/DependencyQuery.cs`):
  - `ModuleBoundaryRules`: `mayDependOn` and `mustNotDependOn`.
  - `LayerRules`: Domain is innermost, Application depends on ports only, and there is no DbContext in Api
    (`.Api.Composition` is exempt).
  - `AssemblyPurityRules`: no hosting, configuration or persistence references or signatures in pure code.
- **Source sweeps** (`SourceSweep`): banned literals, secret shapes, brand identifiers, and the source-product term list
  with its exception list. These are the same files and the same glob and identifier-normalization rules as the
  repository's scrub sweep, read from paths the caller passes.
- **PII-leak canary** (`CanaryLogSink`): a Serilog sink that records events. Its check fails when a planted canary
  reaches a rendered message, a template, a property (nested ones too) or an exception.
- **Golden-vector runner** (`GoldenVectorRunner`): reads `packages/vectors/<area>.vectors.json` and runs a canonicalizer
  or signer supplied as a delegate, so it needs no reference to the security package. The protocol prefix is a
  parameter. With the pinned prefix every expected value is compared. With another prefix, canonical values are
  rebased (`VectorExpectationRebaser`) and signatures are skipped; the cross-language check compares those.
- **Endpoint contracts** (over an `EndpointDataSource` or a `WebApplicationFactory` client):
  - `EndpointContracts.AllEndpointsHavePermission`: the permission attribute type and a closed anonymous allowlist are
    parameters.
  - `EndpointContracts.AllEndpointsHaveValidator`
  - `PublicIdContract.AllIdsArePublicId`: the wire-id type is a predicate.
  - `WireJsonSweep`: finds numeric ids in real response bodies.
  - `ProblemJsonContract.AllErrorsAreProblemJsonAsync`

## Use

```csharp
// Architecture (Starter.Architecture.Tests, phase 2)
ModuleBoundaryRules.CheckRepository([typeof(RecordsModule).Assembly, typeof(WorkflowModule).Assembly]).ThrowIfFailed();
LayerRules.Check([typeof(RecordsModule).Assembly], "Starter.Modules.Records").ThrowIfFailed();

// Contracts (Starter.Contract.Tests, phase 2)
var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
EndpointContracts.AllEndpointsHavePermission(endpoints, typeof(HasPermissionAttribute), AnonymousEndpoints.All).ThrowIfFailed();
PublicIdContract.AllIdsArePublicId(endpoints, type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PublicId<>), LookupIds.Members).ThrowIfFailed();
EndpointContracts.AllEndpointsHaveValidator(endpoints, EndpointContracts.ValidatorsIn(typeof(IValidator<>), typeof(RecordsModule).Assembly)).ThrowIfFailed();
(await ProblemJsonContract.AllErrorsAreProblemJsonAsync(factory.CreateClient(), endpoints,
    new ProblemJsonOptions { RequiredMembers = ["type", "title", "status", "code", "correlationId", "referenceCode"] })).ThrowIfFailed();

// PII canary
var sink = new CanaryLogSink([nationalIdCanary, emailCanary]);
// ...log through new LoggerConfiguration().WriteTo.Sink(sink), drive the code under test...
sink.Check().ThrowIfFailed();

// Golden vectors (prefix CODELABTEST, or the fork's prefix)
new GoldenVectorRunner(prefix).Run(GoldenVectorFile.Load(path), (vectorCase, context) => Canonicalize(vectorCase, context.Prefix)).ThrowIfFailed();
```

For a source sweep, pass the term list and exception list that sit under `tools/extract/` to
`SweepConfigurationFiles.ReadTermRules` and `ReadExceptions`. Then pass the rules, plus `SweepRules.Secrets()`, to
`SourceSweep.Check`.

**Migrating the phase 0 ArchitectureSpike tests.** `tests/Starter.ArchitectureSpike.*` call NetArchTest directly. They
stay in place in phase 1. In phase 2, `Starter.Architecture.Tests` replaces them with `ModuleBoundaryRules` and
`LayerRules` calls, and the spike projects are deleted. `ModuleBoundaryRulesTests` and `LayerRulesTests` in this
package already prove both outcomes the spike proved: a real dependency is detected, and an allowed direction passes.

## Extend

- **A new architecture rule:** add a static method that returns `CheckResult`. Do the dependency work through
  `DependencyQuery`, so the ADR-011 fallback (ArchUnitNET) still touches one file. Add a clean and a dirty fixture
  namespace under `Codelab.Testing.Tests/Fixtures/` and test both.
- **A new contract:** read endpoint facts through `EndpointDescriptions`, which covers both minimal APIs and MVC. Take
  host types (attributes, validators, wire ids) as parameters, never as references.
- **A new sweep rule set:** add a factory to `SweepRules`. Build secret-shaped test inputs at run time, so the
  repository's own scanners stay clean.
- **A vector field that a foreign prefix can derive:** subclass `VectorExpectationRebaser`, or name the field with
  `Canonical` in it.

## Rules

- The package references no other Codelab package (`module.rules.yaml`). Security, error and identity types come in as
  `Type` parameters or predicates.
- Never make a check pass on empty input. `CheckResult` treats zero examined items as a failure.
- NetArchTest.Rules is called only from `Architecture/DependencyQuery.cs` (ADR-011). Namespaces match exactly or as a
  parent: `A.B` covers `A.B.C`, never `A.BC`.
- Secret findings are redacted in excerpts. A canary leak report never repeats the canary value.
- The public API is tracked in `PublicAPI.Unshipped.txt`. It moves to `PublicAPI.Shipped.txt` at the 0.5.0 release.

## Gotchas

- `ModuleNamespaceMap.FromProjects` reads each module's project file (`RootNamespace`, else the project file name). A
  module folder without a project file is skipped, which is normal while the module is only scaffolded.
- Module pairs whose namespaces contain each other (a kernel namespace and a module below it) are not checked against
  each other, because every type would depend on its own parent namespace. Keep module namespaces side by side.
- The repository content rule `base.public-id-routes` refuses route templates that pair an id parameter with a raw
  numeric constraint, anywhere in C#. The fixtures that must contain one build the template from two constants
  (`FixtureRoutes`).
- `LayerRules.DbContextTypes` finds DbContexts by base-type name only among the assemblies you pass. Include the
  module's Infrastructure assembly, or a DbContext used by the Api layer goes unseen.
- `ProblemJsonContract` probes every endpoint with placeholder route values and an empty JSON body. An endpoint that
  answers 2xx to that is counted but not checked, so give the probe client no credentials to reach the 401 and 404
  paths.
