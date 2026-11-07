# P9 kit evals: smoke evidence

Two real headless runs (`claude -p`, Claude Code 2.1.319) of the kit-eval harness in [tools/ai-evals](../../../../tools/ai-evals/MODULE.md), on 2026-11-07,
against the working tree of that day (other phase 9 tasks were editing it in parallel):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\ai-evals\Invoke-KitEval.ps1 -Fixture new-lookup,new-permission -Runs 1 -EvidenceDir docs\evidence\P9\evals\smoke
```

| Fixture | Result | Skill(s) invoked | Turns | Duration of the claude run | Permission denials | Grade (build / rules / content / contract / docs gate) |
| --- | --- | --- | --- | --- | --- | --- |
| new-lookup | pass | new-lookup, coding-standards | 29 | 436 s | 8 | all passed (23.8 / 0.6 / 1.3 / 32.9 / 3.4 s) |
| new-permission | pass | new-permission | 50 | 243 s | 12 | all passed (11.8 / 0.6 / 1.2 / 28.1 / 3.1 s) |

Pass rate 2/2; the exit criterion (>= 90% over >= 10 runs) needs the full eval (10 fixtures x 1 run), which was not
run in this task (headless budget: 2 runs).

**Exit criterion not met.** 2 conclusive runs of the 10 required. Never run: `new-module`, `new-endpoint`,
`new-screen`, `new-transition` and the four input-type fixtures (`input-brd`, `input-dga-screenshot`,
`input-figma-frame`, `input-usecase-workbook`); the decision log's default of 3 runs per sample is not met either. To
close it, run `Invoke-KitEval.ps1 -Fixture all` with enough runs for at least 10 conclusive ones and commit the summary
next to this file (`input-figma-frame` needs the owner's Figma frame, open item 15).

Per run, `smoke/runs/<run id>/` holds `result.json` (every check with its status), `transcript-summary.json`
(skills, tool counts, turns, cost, permission denials, the final message cut to 2000 characters) and
`hook-log.jsonl` (the repository hooks' decisions, `CODELAB_HOOK_LOG`). `smoke/summary.json` and `smoke/summary.md`
are the aggregate. Full transcripts are not in the repository.

## What the runs showed

- **Routing works from plain language.** Neither prompt named a skill or a command; both runs called the expected
  skill through the `Skill` tool first.
- **The hooks ran inside the workspace.** 52 hook events: 47 pre-tool-use allows, 1 warning
  (`hooks.unresolved-shell-write`), and the stop gate refused to stop once per run (docs gate), after which each run
  fixed the docs and stopped cleanly.
- **Scaffold gap (for the scaffold owner).** `tools/scaffold/new-lookup.mjs` writes the `<Name>Ids` constants into
  `Starter.Domain` but does not touch `backend/src/Starter.Domain/MODULE.md`, so every new lookup trips the docs gate
  (DOCS005) until someone updates that file by hand. The run did that by hand.
- **Permission denials, fixed in the harness after these runs.** The runs lost calls to commands outside the
  allowlist: setting `PATH` for node, calling node or dotnet by absolute path, and three attempts to start the full
  CI script (`tools\build.ps1 -Target CI`, correctly refused: no full CI inside a phase). The harness now puts node24
  and the .NET 10 SDK first on the child `PATH`, says so in the prompt, allows the absolute tool paths, and always
  refuses `tools\build.ps1` and `git push` through `--disallowedTools`.
- **Harness bug found and fixed.** Writing the summary at the end of the live run failed in Windows PowerShell 5.1
  (`@()` over a `List[object]`: "Argument types do not match"); the runner now uses `ToArray()` and a regression test
  covers it. `smoke/summary.*` was then written with `-AggregateOnly` from the two `result.json` files.

The throwaway workspaces were removed by `Remove-ThrowawayWorkspace` at the end of each run.
