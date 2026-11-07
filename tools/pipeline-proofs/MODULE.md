# tools/pipeline-proofs

## Purpose

The Phase 9 pipeline-proof harness. One proof takes one **plain business scenario** (no slash command) through
Claude Code headless in a throwaway copy of the working tree and records the evidence the exit criteria ask for:
routing to `build-feature` and the skill sequence it drives, the feature spec with its check result and hand-edit
count (at most 10), the hook log (a refused edit, then the corrected edit on the same file), a refusal probe (a
hand-made module file is refused, naming `new-module`), the gates in the workspace, the `visual-verifier` result (at
most 5 % per viewport after masking, ar and en, desktop and mobile) and the review findings with the iteration count.
The workspace is discarded afterwards.

| Proof | Input type | Record type | Inputs |
| --- | --- | --- | --- |
| `complaint` | BRD + DGA screenshot | Complaint | `inputs/complaint/`: `brd-complaint.md`, `target.<ar\|en>.<1440\|390>.png` (the starter's multi-step wizard look), `masks.json` |
| `permit` | HLS use-case workbook (2 use cases) | Permit | `inputs/permit/permit-use-cases.xlsx`, generated from `permit-use-cases.json` |
| `feedback` | Figma frame | Feedback | `inputs/feedback/README.md`: placeholder until the owner provides the frame URL |

| File | What |
| --- | --- |
| `Invoke-PipelineProof.ps1` | entry script: parameters, exit code |
| `lib/PipelineProof.psm1` | definitions, prompt, transcript steps, hook-log analysis, refusal probe, spec and review log, visual summary, criteria, verdict, report, orchestration |
| `lib/OoxmlWorkbook.psm1` | zero-dependency .xlsx writer (System.IO.Compression, shared strings, byte-stable) |
| `build-permit-workbook.ps1` | regenerates `inputs/permit/permit-use-cases.xlsx` from its JSON source |
| `inputs/<proof>/proof.json` | record type, input files, expected steps, limits, workbook check, `requiresFigmaUrl` |
| `inputs/<proof>/scenario.txt` | the plain scenario prompt |
| `tests/` | Pester 3.4 suites; HeadlessClaude and KitEval are mocked, no claude call |

## Use

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
# Validate the inputs and run the refusal probe (read-only); no workspace, no claude call
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pipeline-proofs\Invoke-PipelineProof.ps1 -Proof permit -DryRun -EvidenceDir $env:TEMP\p9-proof-dry
# A real proof (one headless run; counts against the phase's headless budget)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pipeline-proofs\Invoke-PipelineProof.ps1 -Proof permit -PreApproveSpec -EvidenceDir docs\evidence\P9\<folder>\permit
# The feedback proof, once the owner has the frame
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pipeline-proofs\Invoke-PipelineProof.ps1 -Proof feedback -FigmaUrl "<frame url>" -PreApproveSpec -EvidenceDir <dir>
# Tests and the workbook
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester tools/pipeline-proofs/tests -EnableExit"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pipeline-proofs\build-permit-workbook.ps1
```

Parameters: `-Proof complaint|permit|feedback`, `-EvidenceDir` (required), `-WorkRoot` (default
`%TEMP%\p9-proofs`: workspaces `<runId>\`, transcripts `transcripts\`, gate logs `logs\<runId>\`), `-DryRun`,
`-PreApproveSpec` (the prompt tells the run to record `approvedBy: pipeline-proof (pre-approved)` and the date, the
headless run and the spec check get `CODELAB_PIPELINE_PROOF_APPROVAL=1` so the checker accepts that value, and the
spec check then uses `--require-approved`; without it the run stops at the approval gate, and outside a proof run
the checker refuses that value), `-FigmaUrl`,
`-KeepWorkspace`, `-MaxTurns`, `-TimeoutMinutes` (defaults from `proof.json`), `-PermissionMode`, `-Model`, `-RepoRoot`.

Flow of a run: input checks, then `New-ThrowawayWorkspace`, the refusal probe in the workspace,
`Invoke-HeadlessClaude` with `CODELAB_HOOK_LOG` = `<EvidenceDir>\hook-log.jsonl`, then the step sequence from the
transcript, the new spec under `docs/features/` (`node tools/feature-spec/check-spec.mjs <spec> --json`), the hook
log, the gates (`Invoke-KitEvalChecks` build, rulesCheck, contentRules, docsGate: `dotnet build -warnaserror` with
`--artifacts-path` inside the workspace), the `visual-report.json` files the run wrote, the report, and
`Remove-ThrowawayWorkspace` (marker-verified) in a `finally`.

Evidence in `-EvidenceDir`: `report.json`, `report.md`, `hook-log.jsonl`, `refusal-probe\` (hook output and its own
decision log), `transcript-summary.json`, `spec.md` (a copy of the spec whose local links are rewritten to text naming their workspace path, `ConvertTo-ProofEvidenceSpec`, so the copy passes the docs gate's dead-link check DOCS003), `visual\<n>-visual-report.json`.

Exit codes: `0` pass (or a dry run whose checks passed), `1` a criterion failed or the run broke, `2` invalid inputs,
blocked (feedback without `-FigmaUrl`) or a refused call (unknown proof, an evidence folder that already holds a run).

## Extend

- **A new proof:** a folder `inputs/<name>/` with `proof.json` and `scenario.txt`, the name in `$script:ProofNames`
  and in the script's `ValidateSet`, and a definition test in `tests/PipelineProof.Tests.ps1` (the loop over the
  committed proofs picks it up once the name is added there).
- **A new criterion:** a pure function next to `Get-HookProof` / `Get-VisualSummary`, one `New-Criterion` line in
  `Get-RunCriteria`, its tests, and a section in `Format-PipelineProofReport` when it has detail worth reading.
- **A new expected step:** add it to `$script:KnownSteps` (pipeline order) and to the `expectedSteps` of the proofs.
- **A workbook change:** edit `inputs/permit/permit-use-cases.json`, rerun `build-permit-workbook.ps1` and commit both;
  the test compares the committed xlsx with a fresh build byte for byte.

## Rules

- No headless run in tests or dry runs: tests mock `New-ThrowawayWorkspace`, `Invoke-HeadlessClaude`,
  `Invoke-KitEvalChecks` and the process helpers inside the module (`Mock -ModuleName PipelineProof`).
- Workspaces live under `%TEMP%` only and are removed only through `Remove-ThrowawayWorkspace` (marker check).
- Scenarios stay plain: `Test-PlainScenario` refuses a slash command or a named pipeline step, and the prompt itself
  passes the same check.
- Every literal lives in the constants block of the module; the thresholds (10 hand edits, 0.05 ratio, ar and en,
  1440 and 390) are those of the Phase 9 exit criteria and change only with them.
- Inputs are fictional and use starter names only; they must pass the scrub sweep and the secret scan (both read
  xlsx entries).

## Gotchas

- **The deadline recipe is a document, not a skill:** the step `deadline-recipe` is recorded when a file tool
  (`Read`) opens `docs/recipes/add-deadline.md`; a recipe read through `cat` in a shell is not seen.
- **Steps are first-use order, distinct:** a skill used again later does not move. Other skills (for example
  `coding-standards`) may sit in between; routing passes when `build-feature` is the first pipeline step.
- **Corrected means the same file:** a deny is corrected only by a later `allow` from `pre-tool-use` on one of the
  same repo-relative paths. An entry's files are its `paths[]` (a shell command may name several), else its `path`;
  a deny that names no file at all is never paired.
- **Only changed files are searched** for the spec and the visual reports (`git diff <base>` plus untracked files):
  a visual report written to a gitignored folder or outside the workspace is not found.
- **The dry-run probe runs against the source tree.** It is read-only (the hook only evaluates its input), but its
  decision log in `<EvidenceDir>\refusal-probe\` appends across dry runs into the same folder; the last deny counts.
- **Windows PowerShell 5.1:** `ConvertFrom-Json` emits a JSON array as one object (assign it before iterating), and
  `$PSScriptRoot` is empty in a script's `param` defaults (the scripts resolve defaults in the body). Test files that
  hold Arabic text are saved as UTF-8 with BOM.
- **The complaint targets come from the starter showcase,** so their header is the showcase chrome; `masks.json`
  masks the brand areas, and `dga-design-capture` may crop or de-brand the target before the visual loop.
- **The feedback proof needs Figma access in the headless run.** `Invoke-HeadlessClaude` passes `--strict-mcp-config`,
  so the run sees no Figma connector unless that module gains an MCP-config option.
