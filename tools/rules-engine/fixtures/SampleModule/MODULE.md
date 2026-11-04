# Sample module (rules-engine fixture)

## Purpose
A minimal .NET module used by `tools/rules-engine/tests` to prove that `module.rules.yaml` becomes a
`BannedSymbols.generated.txt` and that a banned call then fails the build.

## Use
The tests copy this folder into a temporary workspace, run the rules engine there and build the project with the
.NET 10 SDK. Nothing builds it in place.

## Extend
Add a rule to `module.rules.yaml` and a matching assertion in `tools/rules-engine/tests/fixture-build.test.mjs`.

## Rules
See `module.rules.yaml`. Records advance only through `IWorkflowEngine`; constructing `LegacyWorkflowRunner` is banned (RS0030).

## Gotchas
The folder is named `fixtures`, which the engine skips, so it never appears in `rules/rules.compiled.json`.
