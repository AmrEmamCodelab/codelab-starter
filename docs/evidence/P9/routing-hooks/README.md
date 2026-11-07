# P9 evidence: routing-hooks

Task: automatic routing (the developer never types a command), bypass refusals that name the skill, the hook
decision log, and the memory secret-scan fix. Run on 2026-11-07 against the uncommitted P9 working tree (other P9 tasks
were editing in parallel), with Node 24.22.2 from `E:\codelab-tools\node24`. Nothing was committed or pushed.

## What changed

| Path | Change |
| --- | --- |
| `CLAUDE.md` | Routing table: every row `ready`; `build-feature` is the default entry point (permit, approval, escalation, complaint, BRD, workbook, screenshot, Figma); `new-transition` ready; deadline row points at `docs/recipes/add-deadline.md`; new rows `usecase-testing`, `prd-to-plan`, `prd-and-plan`, `reduce-comments`; a pipeline line linking `docs/ai-kit/README.md` and listing the five skill-naming refusals |
| `.claude/hooks/routing-table.json` | Same skills as `CLAUDE.md`; a `default` route (`build-feature`); scenario keywords; regex `patterns` (`\buc-?\d+\b` for `usecase-testing`) |
| `.claude/hooks/lib/routing.mjs`, `user-prompt-submit.mjs` | `loadRoutingTable` (routes + default), `pickRoutes`, regex patterns, "recipe not written yet" note; a prompt with no keyword gets "No keyword matched: default route build-feature" |
| `rules/base.rules.yaml` | New forbidden rules `base.endpoint-has-permission`, `base.minimal-api-has-permission`, `base.transition-in-catalog`, `base.lookup-seeded-by-migration`; raw-text fixes name `new-screen`; two P8 false positives fixed (`base.enum-explicit-values` skips `//` comment text, `base.fe-http-via-api-service` ignores a method named `fetch`); `base.endpoint-contract` / `base.lookups-via-migrations` list `hook` |
| `rules/rules.compiled.json` + every generated nested `CLAUDE.md` | Regenerated with `node tools/rules-engine/compile.mjs` (never hand-edited) |
| `.claude/hooks/lib/path-policy.mjs` | Module-folder refusal wording: "Use the \`new-module\` skill" (backend) / "Use the \`new-screen\` skill" (frontend feature area) |
| `.claude/hooks/lib/decision-log.mjs` (new), `lib/hook-io.mjs`, `pre-tool-use.mjs`, `on-stop-gate.mjs`, `lib/constants.mjs` | `CODELAB_HOOK_LOG` JSONL decision log (`HookNames`, `LoggedDecisions`, `SkillReferencePattern`, `RuleIdReferencePattern`, `DefaultRouteSkill` constants); `runHook` gained an `onOutput` callback |
| `.claude/hooks/tests/{skill-routing,decision-log,routing-table}.test.mjs` (new), `tests/helpers/fixture-project.mjs` | 46 new tests; the helper clears `CODELAB_HOOK_LOG` from the child environment |
| `.claude/hooks/README.md` | Automatic routing, decision log, new refusal rows, how to add a skill-routing refusal |
| `.gitignore` | `.claude/memory/` |
| `tools/extract/lib/files.mjs`, `lib/secret-scanner.mjs`, `tests/memory-scan.test.mjs` (new) | `listSecretScanFiles` = git list + `ALWAYS_SECRET_SCANNED_DIRECTORIES` (`.claude/memory`); the secret scan uses it, the scrub sweep and the content check keep honouring `.gitignore` |

`.claude/settings.json` is unchanged: the deny and ask rules stay as they were; nothing was added.

## Refusals that name the skill

Live run against this repository (the hook only decides, nothing is written), `CODELAB_HOOK_LOG` set:

| Hand-made change | Decision | Rule | Skill named |
| --- | --- | --- | --- |
| `backend/src/Modules/Permits/Domain/Permit.cs` (module folder without `module.rules.yaml`) | deny | `hooks.module-without-rules` | `new-module` |
| `ServiceRequests/Api/ExportController.cs` with an ungated `[HttpGet]` | deny | `base.endpoint-has-permission` | `new-endpoint` |
| same file with `[HasPermission(ServiceRequestsPermissionKeys.Review)]` on the class | none (normal flow) | | |
| `ServiceRequests/Application/Approve.cs` assigning `r.Status = ServiceRequestStatuses.Approved` | deny | `base.transition-in-catalog` | `new-transition` |
| `Lookups/Infrastructure/Seeder.cs` calling `LookupSeed.InsertType` | deny | `base.lookup-seeded-by-migration` | `new-lookup` |
| `features/service-requests/demo.component.html` with `<p>Submit your request</p>` | deny | `base.fe-raw-text-in-template` | `new-screen` |

The hook log written by that run:

```json
{"ts":"2026-11-07T11:24:13.244Z","hook":"pre-tool-use","tool":"Write","path":"backend/src/Modules/Permits/Domain/Permit.cs","decision":"deny","ruleIds":["hooks.module-without-rules"],"skill":"new-module"}
{"ts":"2026-11-07T11:24:13.555Z","hook":"pre-tool-use","tool":"Write","path":"backend/src/Modules/ServiceRequests/Api/ExportController.cs","decision":"deny","ruleIds":["base.endpoint-has-permission"],"skill":"new-endpoint"}
{"ts":"2026-11-07T11:24:14.033Z","hook":"pre-tool-use","tool":"Write","path":"backend/src/Modules/ServiceRequests/Api/ExportController.cs","decision":"allow","ruleIds":[],"skill":null}
{"ts":"2026-11-07T11:24:14.334Z","hook":"pre-tool-use","tool":"Write","path":"backend/src/Modules/ServiceRequests/Application/Approve.cs","decision":"deny","ruleIds":["base.transition-in-catalog"],"skill":"new-transition"}
{"ts":"2026-11-07T11:24:14.626Z","hook":"pre-tool-use","tool":"Write","path":"backend/src/Modules/Lookups/Infrastructure/Seeder.cs","decision":"deny","ruleIds":["base.lookup-seeded-by-migration"],"skill":"new-lookup"}
{"ts":"2026-11-07T11:24:14.952Z","hook":"pre-tool-use","tool":"Write","path":"frontend/starter-web/src/app/features/service-requests/demo.component.html","decision":"deny","ruleIds":["base.fe-raw-text-in-template"],"skill":"new-screen"}
```

Lines 2 and 3 are the hook-proof shape (a refused edit, then the corrected edit allowed).

CI agreement: `tests/skill-routing.test.mjs` writes the same five bypass files into a throwaway git project with the
real `rules/rules.compiled.json` and runs `node tools/rules-engine/check-content.mjs --root <project>`: exit 1, each
file reported with its rule id and the "Use the \`<skill>\` skill" fix; the corrected files exit 0.

## Routing

| Prompt (no slash command) | Routing line added by `user-prompt-submit.mjs` |
| --- | --- |
| "Farmers register their farms and an officer checks the papers before the season starts" | No keyword matched: default route build-feature ... |
| "clients request a site-visit permit; the inspector approves; undecided after 5 days it escalates to the manager" | Likely match for this request: build-feature, docs/recipes/add-deadline.md. |

## Verification

| Check | Command | Result |
| --- | --- | --- |
| Hook tests | `node --test ".claude/hooks/tests/**/*.test.mjs"` | 161 pass, 0 fail (115 before this task) |
| Rules-engine tests | `node --test "tools/rules-engine/tests/**/*.test.mjs"` | 309 pass, 0 fail |
| Rules drift | `node tools/rules-engine/compile.mjs --check` | `rules check: OK (144 generated file(s) up to date)` |
| Content rules (CI step) | `node tools/rules-engine/check-content.mjs --root .` | `0 error(s), 0 warning(s)`; before the two false-positive fixes it reported 2 P8 errors (`OidcBackChannel.cs` enum doc comment, `reference-lookup.store.ts` `this.fetch`) |
| Extract tests | `node --test "tools/extract/tests/**/*.test.mjs"` | `memory-scan.test.mjs` 5/5 pass. Suite: 96/100; the 4 failures are outside this task: 3 are the new `provenance/origins/p9-*.json` files of the harvest tasks whose manifest batches the integrator adds (shared file), 1 is a scrub-sweep test on another task's file |
| Scrub sweep / secret scan | `node tools/extract/scrub-sweep.mjs --root .`, `node tools/extract/secret-scan.mjs --root .` | No finding in this task's paths (a first run flagged the lookup rule's `\bLookupItem\.Seeded` as a UNC path in the compiled JSON; dots are now written `[.]`). The repo-wide findings are in other tasks' files (`tools/ai-evals/**`) |

## Notes

- The endpoint rule is a text twin of the contract test (`backend/tests/Starter.Contract.Tests`, `EndpointAllowlist.cs`),
  which stays authoritative. It recognises a gate on the action's attribute block or on the nearest class's attribute
  block; a gate written on the same line as `[HttpX]` is not recognised (one attribute per line).
- The minimal-API rule wants the gate in the same statement; a gate on a `MapGroup` is not seen (none exist in
  `backend/src` today).
