# P9 evidence: pipeline-proof harness

Task `proof-harness` (Phase 9) built the harness and the inputs of the three pipeline proofs. **No headless claude
session ran in this task.** The real proofs belong to the `proof-run` task, which uses this harness.

## What was built

| Path | What |
| --- | --- |
| `tools/pipeline-proofs/Invoke-PipelineProof.ps1` | `-Proof complaint\|permit\|feedback -EvidenceDir <dir> [-WorkRoot <temp>] [-DryRun] [-PreApproveSpec] [-FigmaUrl] [-KeepWorkspace]` |
| `tools/pipeline-proofs/lib/PipelineProof.psm1` | the harness logic; imports `tools/ai-evals/lib/HeadlessClaude.psm1` (workspace, headless run) and `KitEval.psm1` (gates, spec-path filter) |
| `tools/pipeline-proofs/lib/OoxmlWorkbook.psm1`, `build-permit-workbook.ps1` | zero-dependency, byte-stable .xlsx writer and the permit workbook generator |
| `tools/pipeline-proofs/inputs/complaint/` | generic citizen-complaint BRD, 4 DGA-look targets (the starter's own multi-step wizard baselines from `docs/evidence/P4/baselines/showcase-wizard.*`, ar and en, 1440 and 390), `masks.json`, plain `scenario.txt` |
| `tools/pipeline-proofs/inputs/permit/` | `permit-use-cases.xlsx` (HLS format: Summary, Functional, Data, BusinessRules, StateTransitions, Gaps_Messages; exactly 2 use cases UC-PRM-01 apply, UC-PRM-02 inspector approve or reject, escalation to the inspection manager after 5 days undecided), its JSON source, plain `scenario.txt` |
| `tools/pipeline-proofs/inputs/feedback/` | placeholder README: the owner provides a frame in a Codelab Figma file; no Figma call was made |
| `tools/pipeline-proofs/MODULE.md` | Purpose, Use, Extend, Rules, Gotchas |

Each report (`report.json` + `report.md`) records: the prompt, the skill sequence (expected `build-feature >
new-module > new-transition > deadline-recipe > new-permission > new-endpoint > new-screen`, observed from the
transcript), routing, the spec path with its check-spec result, the hand-edit count against the limit of 10, the hook
proof (a deny followed by an allow on the same file), the refusal probe, the gates (build `-warnaserror`, rules check,
content rules, docs gate), the visual summary (final iteration per route, language and width, at most 0.05), the
Review log findings and iteration count, and the verdict.

## Verification (2026-11-07, Windows PowerShell 5.1, Pester 3.4.0, node 24 private toolchain)

| Check | Result |
| --- | --- |
| `powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester tools/pipeline-proofs/tests -EnableExit"` | **66 passed, 0 failed** (workbook writer and committed workbook, definitions and prompt, step sequence, skill-sequence check, hook-log analysis, refusal probe pure and through the real hook, check-spec output and the real checker on the worked example, hand-edit threshold, Review log, visual summary, criteria, verdict, report, dry run with no claude call, mocked full run, infrastructure failure, `-KeepWorkspace`) |
| `Invoke-PipelineProof.ps1 -Proof permit -DryRun -EvidenceDir %TEMP%\p9-proof-dry` | exit 0, verdict `dry-run`: inputs valid (workbook read by `Read-Xlsx.ps1`: UC-PRM-01, UC-PRM-02), refusal probe passed |
| `-Proof complaint -DryRun` | exit 0, verdict `dry-run` |
| `-Proof feedback -DryRun` (no `-FigmaUrl`) | exit 2, verdict `blocked` (waiting for the owner's Figma frame) |
| Refusal probe through `.claude/hooks/pre-tool-use.mjs` | `deny`, reason `[hooks.module-without-rules] backend/src/Modules/ProofProbe has no module.rules.yaml ... Fix: Use the `new-module` skill ...`; decision log line `{"hook":"pre-tool-use","decision":"deny","ruleIds":["hooks.module-without-rules"],"skill":"new-module"}` |
| `build-permit-workbook.ps1` twice | identical SHA-256 (byte-stable); the test compares the committed file with a fresh build |
| `node tools/extract/scrub-sweep.mjs --root .` | clean (untracked files and xlsx entries included) |
| `node tools/extract/secret-scan.mjs --root .` | clean |

## Open

- The three real proofs (`proof-run` task; one headless run each against the phase budget) and their evidence.
- Feedback proof: needs the Codelab Figma frame URL and Figma access inside the headless run (`Invoke-HeadlessClaude`
  passes `--strict-mcp-config`).
- The harness is not yet a CI step: the integrator adds `tools/pipeline-proofs/tests` to the Pester step of
  `tools/build.ps1`.
