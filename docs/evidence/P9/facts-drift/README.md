# P9 evidence: facts-drift gate

Build plan phase 9: "A facts-drift gate: every agent, skill and doc referencing a path, command or type must resolve.
No stale Angular 19, role auth or source-product term text." Exit: 0 findings.

The gate is `node tools/facts-drift/check.mjs --root .` ([tools/facts-drift/MODULE.md](../../../../tools/facts-drift/MODULE.md)).
Runs below are from 2026-11-07, Node 24.22.2 (private toolchain), on a working tree that other phase 9 tasks were
editing at the same time: the agent, skill and feature-spec counts are the state seen at that moment.

## Tests

`node --test "tools/facts-drift/tests/**/*.test.mjs"`: 45 tests, 13 suites, 45 pass, 0 fail. Parsing (Markdown
fences, intros, code spans, tables; shell commands; PowerShell `param()` blocks), classification and resolution
(paths, globs, placeholders, types, symbols, skill and agent references, configuration and allowlist files) and
end-to-end runs over fixture trees in `%TEMP%` (every rule, the scope, block `cd`, placeholders, the allowlist,
source-product terms, CLI exit codes 0, 1 and 2).

## Before: first run (heuristics untuned, no allowlist)

77 documents in scope, 129 findings: 69 `path-missing`, 20 `type-unresolved`, 14 `command-file-missing`,
12 `npm-script-missing`, 10 `npm-package-missing`, 3 `glob-empty`, 1 `skill-missing`.

Most were false positives that tuned the heuristics rather than the docs:

| Seen | Change in the gate |
| --- | --- |
| `.ts`, `.scss`, `.xlsx`, `.Tests`, `.shell__brand` named on their own | a lone `.name` is an extension or CSS class, never a path |
| `npm run catalog:check -w @codelab/ng-dga` | `-w` accepts a package name, mapped through every `package.json` `name` |
| `[Unreleased]` (a changelog heading) | an attribute span needs a name of two or more words |
| `PinnedKeys`, `BlockedExtensions`, `ActiveKeyNotInRing` | the symbol index also holds C# members and C#/PowerShell enum members (and `deploy/` scripts) |
| `Directory.Build.props/.targets` | `x/.ext` reads as a sibling-extension shorthand |
| a code block introduced by "Then, from `frontend/starter-web`" | the paragraph before a block can set its working folder; a folder span on the same line is a hint |

## Fixed in the documents this task owns

| File | Finding | Fix |
| --- | --- | --- |
| `docs/standards/EXAMPLES.md` row 1 | `IRoleAdminService` (no such port; role-era name) | named-argument example from the docs gate (`RepositoryPaths.ToFullPath`) |
| `docs/standards/EXAMPLES.md` row 6 | `IServiceRequestRepository` (does not exist) | `IJobRuns.AbandonOtherRunningAsync`, the real one-UPDATE status change |
| `docs/standards/EXAMPLES.md` row 12, `docs/standards/SKILL.md` standard 12 | `ServiceRequestStatusIds` (does not exist) | `ContactTopicIds` (`backend/src/Starter.Domain/Lookups/ContactTopicIds.cs`) |
| `docs/standards/EXAMPLES.md` row 13 | `JobRunState` (the enum is `JobRunStatus`) | `JobRunStatus` |
| `docs/standards/EXAMPLES.md` row 14 | `SlaOptions`, `ConfigKeys.Sla.DefaultDays` (do not exist) | `ServiceRequestsPermissionKeys.Decide`, `LookupLimits.CodeMaxLength`, `DeadlinesJobKeys` |
| `docs/standards/EXAMPLES.md` row 16 | `ServiceRequestsEndpoints` | `ServiceRequestsController` |
| `docs/standards/EXAMPLES.md` row 18 | `SubmitServiceRequestTests` | `DraftUseCaseTests` (where `SubmitServiceRequest` is tested) |
| `docs/design/README.md` step 5 | `node e2e/visual.mjs` from the root | `npm run visual --workspace frontend/starter-web` |
| `docs/recipes/add-page.md` | `npm run api:gen`, `npm run lint` from the root | `-w frontend/starter-web` |

The BAD/GOOD code blocks in `docs/standards/EXAMPLES.md` and `ERROR-HANDLING.md` keep their illustrative names: they
show a shape, and the gate does not check types inside code blocks.

Allowlisted with reasons (`tools/facts-drift/allowlist.txt`, 33 findings): gitignored local secrets
(`appsettings.*.local.json`), the server-side `appsettings.Production.json`, deploy-kit output (`publish/`,
`manifest.json`, `kits\Install-*.ps1`), run outputs of the usecase-testing and design-loop skills, the worked-example
files the recipes create (add-lookup, add-page, add-workflow-type), and `CspDirective` (a Seq event property).

## After

`node tools/facts-drift/check.mjs --root .`: 77 documents, **0 findings in the paths this task owns**
(`docs/recipes`, `docs/standards`, `docs/design`, `docs/runbooks`); no stale-term or source-product findings
anywhere. 12 findings remain, all in files owned by other phase 9 tasks, routed to the integrator:

| File:line | Finding |
| --- | --- |
| `.claude/skills/build-feature/SKILL.md:17` | `docs/features/site-visit-permit.md` does not exist (the example is `docs/features/examples/site-visit-permit.md`) |
| `.claude/skills/build-feature/SKILL.md:52` | `npm run api:gen` from the root: add `--workspace frontend/starter-web` |
| `.claude/skills/new-endpoint/SKILL.md:29` | `npm run api:gen` from the root: add `--workspace frontend/starter-web` |
| `.claude/skills/new-permission/SKILL.md:29`, `:44` | `npm run api:gen` from the root: add `--workspace frontend/starter-web` |
| `.claude/skills/new-screen/SKILL.md:15`, `:38`, `:74` | `npm run api:gen` from the root: add `--workspace frontend/starter-web` |
| `.claude/skills/new-screen/SKILL.md:26` | `tools/scaffold/new-screen.mjs` does not exist (yet) |
| `.claude/skills/new-screen/SKILL.md:113` | `npm run lint` from the root: add `--workspace frontend/starter-web` |
| `.claude/skills/new-screen/SKILL.md:139`, `:141` | code block runs `npm run lint` and `node e2e/visual.mjs` without naming `frontend/starter-web` (add `Set-Location frontend/starter-web` or say "from `frontend/starter-web`" before the block) |

Line numbers are those of the run; the gate reprints them on every run.
