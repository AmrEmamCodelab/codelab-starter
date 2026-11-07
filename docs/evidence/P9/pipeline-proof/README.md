# P9 pipeline proofs: status

**Exit criterion not met.** The phase asks for three proofs run end to end (Complaint, Permit, Feedback), each with
the expected skill sequence, hook proof, reviews with an iteration count, the commit gate, and a visual diff of at most
5% per viewport in `ar` and `en`. On 2026-11-07 the evidence holds two runs that did not finish the pipeline and no
visual measurement.

| Proof | Input | Status | What is missing |
| --- | --- | --- | --- |
| Permit | use-case workbook (`tools/pipeline-proofs/inputs/permit/`) | **incomplete**: the one run was terminated from outside at step 7 (Review), see `report.json` | test-writer, visual-verifier and the five reviewers never reported; commit gate not reached; skill sequence, hook proof, visual, review and commit-gate criteria all `passed: false`. The input has no target images, so the visual criterion needs a target (or an owner decision that it does not apply to this proof). |
| Complaint | BRD + 4 target images + masks (`tools/pipeline-proofs/inputs/complaint/`) | **FAIL**, run `complaint-20261107-155353-159cee` (29 min, 12 turns, USD 21.95), evidence in [pipeline-proof-complaint/](../pipeline-proof-complaint/report.md) | The headless session stopped after `new-endpoint`, before `new-screen`: its last message was "The Complaints module builder is still running; waiting for it." The main agent ended its turn while a background builder subagent was still working, and `claude -p` exits when the main agent's turn ends. Passed: routing, spec valid, 5 hand edits, refusal probe, gates (build, rules, content rules, docs gate). Failed: skill sequence (no `new-screen`), hook proof (3 denies, none corrected), visual (no `visual-report.json`, no screen was built), review log (0 iterations). |
| Feedback | Figma frame (`tools/pipeline-proofs/inputs/feedback/`) | **blocked on the owner** | the Figma frame (open item 15); until it exists the harness exits `2` (blocked) and calls nothing. |

Visual criterion: no `visual-report.json` exists for any proof, so the 5% per-viewport limit has not been measured.

## Why the Complaint run stopped (fix outside this area)

`claude -p` returns as soon as the main agent ends a turn. `build-feature` started the module builder as a background
subagent and then ended its turn to wait for it, so the session ended with the pipeline at step 5. Rerunning the
proof before that is fixed would stop at the same point. The fix belongs to the `build-feature` skill (wait for builder
subagents in the foreground, never end the turn while one is running) or to the proof harness (resume the session
until the pipeline reports the commit gate). The Permit rerun waits for the same fix: the earlier Permit run was cut
at step 7 as well.

## Files

- [pipeline-proof-complaint/](../pipeline-proof-complaint/report.md): the Complaint run as the harness wrote it
  (`report.json`, `report.md`, `spec.md`, `hook-log.jsonl`, `refusal-probe/`, `transcript-summary.json`).
- `report.json`: the Permit run, assembled afterwards from the kept workspace with the harness's own functions
  (the harness's report writer never ran).
- `spec.md`: the feature spec the run wrote. Its local links pointed from `docs/features/` inside the throwaway
  workspace; they are rewritten to text naming the workspace path (the harness now does this on copy,
  `ConvertTo-ProofEvidenceSpec` in [tools/pipeline-proofs](../../../../tools/pipeline-proofs/MODULE.md)).
- `hook-log.jsonl`, `refusal-probe/`, `gates.json`, `transcript-summary.json`: the run's hook decisions, the refusal
  probe, the gate checks and the transcript summary.

## To close the criterion

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pipeline-proofs\Invoke-PipelineProof.ps1 -Proof complaint -PreApproveSpec -EvidenceDir docs\evidence\P9\pipeline-proof-complaint-2
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pipeline-proofs\Invoke-PipelineProof.ps1 -Proof permit -PreApproveSpec -EvidenceDir docs\evidence\P9\pipeline-proof-permit
```

Run these after the early-stop fix above. Each run needs an empty evidence folder and an uninterrupted time budget (the Permit run spent 73 minutes in the
headless session before reaching Review). Feedback runs once the owner supplies the Figma frame.
