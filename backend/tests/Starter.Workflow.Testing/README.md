# Starter.Workflow.Testing

The workflow test kit (module 8). A plain class library with no test framework dependency: a failed expectation throws `WorkflowExpectationException`, which every runner reports as a failure. Namespace `Starter.Workflow.Testing`.

## What it holds

| Type | Use |
| --- | --- |
| `TransitionFixture` | Fluent, immutable assertions over a `TransitionCatalog` through the real `RecordWorkflowEngine`: `For(catalog)`, `From(status)`, `WithPermissions(keys)`, `AsOwner()` / `AsNonOwner()`, `OnSide(side)`, `ExpectAllowedActions(keys)`, `Do(action, reason?)`. |
| `TransitionOutcome` | The decision of one `Do`: `ExpectStatus(status)` (continues from the new status), `ExpectRefused(WorkflowRefusal)` (continues from the unchanged status), `ExpectTerminal()`. |
| `TestRecordWorkflow`, `TestRecordStatuses`, `TestRecordActions`, `TestRecordPermissionKeys`, `TestRecordWorkflowDefinition` | The generic `test-record` type (prefix `TR`) used by kernel tests: Draft, Submitted, InReview, Returned and three terminal statuses, with a return-and-resubmit loop. No module ships it. |
| `BrokenCatalogs` | One defect each: `Unreachable`, `WithoutPermission`, `ReasonWithoutField`, and `Combined` (all three: the workflow-conformance exit criterion). Validate against `KnownPermissions`. |

## Use

Reference the project from a module test project, then walk the module's own catalog:

```csharp
TransitionFixture.For(MyWorkflowDefinition.Catalog)
    .WithPermissions(MyPermissionKeys.Create).AsOwner()
    .Do(MyActions.Submit).ExpectStatus(MyStatuses.Submitted);

TransitionFixture.For(MyWorkflowDefinition.Catalog).From(MyStatuses.UnderReview)
    .WithPermissions(MyPermissionKeys.Decide)
    .Do(MyActions.Return).ExpectRefused(WorkflowRefusal.ReasonRequired)
    .Do(MyActions.Return, "page 2 is missing").ExpectStatus(MyStatuses.Returned);

TransitionFixture.For(MyWorkflowDefinition.Catalog).From(MyStatuses.Approved)
    .WithPermissions(MyPermissionKeys.Create, MyPermissionKeys.Decide).AsOwner()
    .ExpectAllowedActions();                     // a terminal status offers nothing
```

A catalog's structure (reachability, permissions, wording, notification decisions) is checked by `TransitionCatalogValidator`; the kit checks its behaviour. Test both: `Assert.Empty(TransitionCatalogValidator.Validate(catalog, knownKeys))` plus fixture walks of every path.

## Rules

- The kit names no real record type; module-specific catalogs are tested in that module's test project.
- `ExpectAllowedActions` compares as a set; without `OnSide` it asks every `TransitionSide`.
- `Do` acts from `OnSide` when set, otherwise from the requester side as the owner and the staff side as a non-owner. The engine takes only rules of that side, and never a staff rule for the record's owner (segregation of duties), so walk requester steps `AsOwner()` and staff steps `AsNonOwner()`.
- Fixtures are immutable: branch from a shared starting fixture freely.

Tests of the kit itself: `backend/tests/Starter.Application.Tests/Workflow/TransitionFixtureTests.cs`.
