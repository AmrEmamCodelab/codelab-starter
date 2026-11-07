# tools/ai-evals

## Purpose

The kit evals: proof that a developer who describes work in plain language gets the right skill and an output that
passes the gates. Each fixture is a plain-language prompt (no slash command) plus the skill it must route to and the
checks its output must pass. A run copies the working tree into a throwaway workspace under `%TEMP%`, runs Claude
Code headless (`claude -p`) there, grades the result, and deletes the workspace. The phase 9 exit criterion is a
**pass rate of at least 90% over at least 10 conclusive runs**.

The headless module (`lib/HeadlessClaude.psm1`) is also the contract the pipeline proofs (`tools/pipeline-proofs`)
import.

| File | What it is |
| --- | --- |
| `lib/HeadlessClaude.psm1` | `New-ThrowawayWorkspace`, `Invoke-HeadlessClaude`, `Remove-ThrowawayWorkspace` (+ transcript parsing, the process runner). |
| `lib/KitEval.psm1` | Fixture loading and validation, the grader, run scoring, the pass-rate aggregation and the Markdown report. |
| `Invoke-KitEval.ps1` | The runner: `-Fixture <id[,id]|all> -Runs <n> -EvidenceDir <dir> [-DryRun] [-AggregateOnly] [-RequireCriteria]`. |
| `Register-KitEvalSchedule.ps1` | Prints (default) or, with `-Register`, creates the weekly scheduled task for the full eval. |
| `fixtures/<id>.json` | One fixture per scaffold skill and one per input type; `fixtures/inputs/` holds the sample inputs. |
| `tests/` | Pester 3.4 tests (claude, workspaces and the grader mocked where they would be slow or spend usage). |

## Use

Run from the repo root in Windows PowerShell 5.1. The scripts put `E:\codelab-tools\node24` first on `PATH` and pass
`CODELAB_DOTNET=E:\codelab-tools\dotnet10\dotnet.exe` to every child; `claude` must be on `PATH` (or set
`CODELAB_CLAUDE`) and signed in.

```powershell
# Validate every fixture and print the plan; no workspace, no claude call
powershell -NoProfile -ExecutionPolicy Bypass -File tools\ai-evals\Invoke-KitEval.ps1 -Fixture all -DryRun

# Real runs (each one is a headless Claude Code session: it spends usage)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\ai-evals\Invoke-KitEval.ps1 -Fixture new-lookup,new-permission -Runs 1 -EvidenceDir <dir>

# The full eval: 10 fixtures x 1 run = 10 runs, exit 1 unless the criterion is met
powershell -NoProfile -ExecutionPolicy Bypass -File tools\ai-evals\Invoke-KitEval.ps1 -Fixture all -Runs 1 -RequireCriteria -EvidenceDir <dir>

# Recompute summary.json / summary.md from every runs\*\result.json already in a folder (runs from several invocations)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\ai-evals\Invoke-KitEval.ps1 -AggregateOnly -EvidenceDir <dir>

# The scheduled full eval: prints the task; -Register creates it (owner decision, not done by the kit)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\ai-evals\Register-KitEvalSchedule.ps1 [-DayOfWeek Saturday -At 02:00 -Runs 1] [-Register]

# Tests (CI runs them in step 19 of tools\build.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester tools/ai-evals/tests -EnableExit"
```

Runner options: `-MaxTurns` (80), `-TimeoutMinutes` (45 per headless run), `-PermissionMode`
(`acceptEdits` with a command allowlist, `dontAsk` or `bypassPermissions`), `-Model`, `-NodeModules Offline|None`,
`-SkipBaseline`, `-KeepWorkspace`, `-WorkspaceRoot` (default `%TEMP%\p9-evals`), `-RepoRoot`.

Exit codes of `Invoke-KitEval.ps1`: `0` every conclusive run passed (or the dry run found every fixture valid); `1` a
run failed, or `-RequireCriteria` and the criterion was not met; `2` usage error or an invalid fixture.

**One run.** `New-ThrowawayWorkspace` copies the working tree (uncommitted and untracked changes included) to
`<WorkspaceRoot>\<fixture>-r<n>-<id>`, makes one snapshot commit tagged `eval-base` and runs `npm ci --offline`. A
**baseline** grade of the untouched copy follows (`-SkipBaseline` skips it). Then `Invoke-HeadlessClaude` sends the
fixture prompt plus a fixed "you are unattended" note on stdin, and the grader runs the fixture's checks in the
workspace. Last, `Remove-ThrowawayWorkspace` deletes the copy.

**Evidence.** `<EvidenceDir>\runs\<run id>\result.json` (checks with status, exit code, duration and the log tail of
a failure), `transcript-summary.json` (skills, tool counts, turns, cost, permission denials, the final text cut to
2000 characters) and `hook-log.jsonl` (written only when the hooks support `CODELAB_HOOK_LOG`). Full transcripts and
check logs stay in `<WorkspaceRoot>\transcripts` and `<WorkspaceRoot>\logs`, outside the repository.
`<EvidenceDir>\summary.json` and `summary.md` hold the aggregate.

**Fixture contract** (`fixtures/<id>.json`, the file name is the id):

| Key | Rule |
| --- | --- |
| `id` | kebab-case, equal to the file name |
| `kind` | `scaffold` or `input` |
| `skill` | the expected skill: a scaffold skill for `scaffold` (`new-module`, `new-endpoint`, `new-screen`, `new-permission`, `new-transition`, `new-lookup`), `build-feature` for `input` |
| `prompt` | plain language; no slash command, no skill name |
| `inputs` | repo-relative files that exist (at least one for `input`) |
| `checks` | from `build`, `rulesCheck`, `contentRules`, `contractTests`, `docsGate`, `specValid`, `skillInvoked`; `skillInvoked` always, `specValid` for `input` |
| `description`, `maxTurns`, `timeoutMinutes` | optional |

**Checks** (in this order):

| Check | Passes when |
| --- | --- |
| `build` | `dotnet build codelab-starter.slnx -warnaserror --artifacts-path <workspace>\artifacts` exits 0 |
| `rulesCheck` | `node tools/rules-engine/compile.mjs --check --root <workspace>` exits 0 |
| `contentRules` | `node tools/rules-engine/check-content.mjs --root <workspace>` exits 0 |
| `contractTests` | `dotnet test backend/tests/Starter.Contract.Tests` exits 0 |
| `docsGate` | `dotnet run --project tools/docs-gate -- check --root . --base eval-base` exits 0 |
| `specValid` | at least one new or changed `docs/features/<slug>.md` (not `README.md`, `TEMPLATE.md` or sub-folders), and `node tools/feature-spec/check-spec.mjs <spec>` exits 0 for each |
| `skillInvoked` | the transcript shows the expected skill called through the `Skill` tool |

**Scoring.** A run passes when every check passed, the headless run did not time out and no infrastructure step
failed. A run is **inconclusive** (reported, kept out of the rate) when an infrastructure step failed (copy, npm,
claude missing) or when every failed check had already failed in the baseline: the source tree was broken, not the
kit. The criterion is met when the conclusive runs number at least 10 and at least 90% of them passed.

## Extend

- **A new fixture:** add `fixtures/<id>.json` following the contract and put any sample input under
  `fixtures/inputs/`. `Invoke-KitEval.ps1 -Fixture <id> -DryRun` and the test "The shipped fixtures" validate it.
- **A new check:** add its name to `$script:KnownChecks` (and to `$script:BaselineChecks` when it reads only the
  repository), its command to `Get-KitEvalCheckCommand` (or a function like `Invoke-SpecCheck`), its timeout to
  `$script:CheckTimeoutMinutes`, a test in `tests/KitEval.Tests.ps1` and a row in the table above.
- **A new scaffold skill:** add it to `$script:ScaffoldSkills` and add its fixture.
- **Another caller of the headless module** (`tools/pipeline-proofs`): `Import-Module tools/ai-evals/lib/HeadlessClaude.psm1`;
  in tests, `Mock -ModuleName HeadlessClaude Invoke-LoggedProcess` (see `tests/HeadlessClaude.Tests.ps1`) or mock
  `Invoke-HeadlessClaude` itself.

## Rules

- Workspaces live under `%TEMP%` only; `New-ThrowawayWorkspace` refuses a destination outside it, inside the source
  repository or not empty. It never copies `.git`, `bin`, `obj`, `artifacts`, `publish`, `node_modules`,
  `.angular`, `dist`, `.claude/memory`, `.claude/handoffs`, key material (`*.pfx`, `*.p12`, `*.key`, `*.pem`), local
  secrets (`appsettings.*.local.json`, `.env*`) or `settings.local.json`. `feed/` is copied (local packages).
- `Remove-ThrowawayWorkspace` deletes only a directory whose marker (`.codelab-throwaway-workspace.json`, written
  first, git-excluded) was created by this module for that very path, below `%TEMP%`; it never follows a junction.
- `node_modules` come from `npm ci --offline` inside the workspace; never a junction to the real repository, so
  nothing in a run can install into it.
- No full transcripts in the repository: only the summaries. No secrets in fixtures or inputs (fictional data).
- Every run spends Claude usage: dry-run first, and keep the headless budget of the phase.
- Starter names only in fixtures and inputs (the scrub sweep covers this folder).

## Gotchas

- `claude -p` runs with `--permission-mode acceptEdits` and an allowlist (node, npm, npx, git, dotnet, the private
  toolchain under `E:\codelab-tools\` bare or through `&`, read-only shell commands and `mkdir`). A command outside
  it is denied, not asked; the denials are counted in `transcript-summary.json`. Every rule names its executable
  first: never add `PowerShell(& *)`, a rule that starts with a wildcard, a write cmdlet (`New-Item`, `Set-*`,
  `Remove-*`) or `$env:` (the Pester suite refuses them; inputs are untrusted content and could inject a command).
  Widen the list in `$script:DefaultAllowedTools` rather than switching to `bypassPermissions`. In every mode
  `--disallowedTools` refuses `tools\build.ps1` (no full CI inside a run), `git push`, `git clean`,
  `git reset --hard`, deletes (`Remove-Item`, `rm`, `rmdir`, `del`, `rd`) and shell hand-offs (`cmd`, `iex`,
  `Start-Process`). The allowlist is not a sandbox (`node` can still do anything): run the scheduled eval under a
  low-privilege local account with no access to other repositories. The child `PATH` starts
  with node24 and the .NET 10 SDK folder, and the prompt says so, because the first smoke run lost 8 calls to
  denied `PATH` juggling.
- The repository's own hooks run inside the workspace (its `.claude/settings.json` is copied), so a hook refusal
  shows in the transcript like in a real session.
- The workspace has no remote and one commit: the docs gate compares against the tag `eval-base`.
- A machine that sleeps stalls builds and tests for hours: `Invoke-KitEval.ps1` holds `SetThreadExecutionState`
  while runs are going on. A process timeout counts only awake time.
- The baseline doubles the build time of a run but makes a broken source tree visible as "inconclusive" instead of
  a kit failure; the second build is incremental.
- Windows PowerShell 5.1 turns stderr of a native command into an error under `$ErrorActionPreference = 'Stop'`;
  the helpers that call git set `Continue` locally and judge by the exit code.
