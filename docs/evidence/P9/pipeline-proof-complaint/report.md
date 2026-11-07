# Pipeline proof: complaint (Complaint)

- Verdict: **FAIL** (run)
- Input type: BRD + DGA screenshot
- Run: complaint-20261107-155353-159cee; started 2026-11-07T15:53:53.8775077+02:00, finished 2026-11-07T16:23:14.4727397+02:00

## Criteria

| Criterion | Result | Detail |
| --- | --- | --- |
| The headless run finished (exit 0, no timeout) | pass | exit 0, timed out: False |
| A plain scenario routed to build-feature first | pass | first pipeline step: build-feature |
| build-feature invoked the expected skills in order | FAIL | observed: build-feature > coding-standards > deadline-recipe > new-module > new-permission > new-transition > new-lookup > new-endpoint; missing: new-screen; in order: False |
| The feature spec validates (check-spec) | pass | docs/features/complaint.md: status approved, 0 error finding(s) |
| At most 10 hand edits after approval, each logged | pass | 5 of at most 10 hand edits |
| The hook log shows a refused edit that was then corrected | FAIL | 3 deny, 0 corrected, 3 not corrected |
| A hand-made module file is refused, naming new-module | pass | [hooks.module-without-rules] backend/src/Modules/ProofProbe has no module.rules.yaml. Every module needs its rules before its code. Fix: Use the `new-module` skill (/new-module) to create the module; it writes module.rules.yaml, MODULE.md and the generated CLAUDE.md. |
| The gates pass in the workspace (build -warnaserror, rules, content rules, docs gate) | pass | ran: build, rulesCheck, contentRules, docsGate; failed:  |
| visual-verifier at most 0.05 per viewport after masking, ar and en | FAIL | worst ; languages ; widths ; no visual-verifier target report found |
| Review findings and the iteration count are recorded | FAIL | iterations: 0; findings: 0 |

## Scenario prompt

```text
Citizens file a complaint about one of our services and can follow it; a complaints officer resolves it with a response or rejects it with a reason, and can ask the citizen for more information; a complaint still open 10 days after it was filed escalates to the complaints manager.

The material for it is in this repository:
- tools/pipeline-proofs/inputs/complaint/brd-complaint.md
- tools/pipeline-proofs/inputs/complaint/target.ar.1440.png
- tools/pipeline-proofs/inputs/complaint/target.en.1440.png
- tools/pipeline-proofs/inputs/complaint/target.ar.390.png
- tools/pipeline-proofs/inputs/complaint/target.en.390.png
- tools/pipeline-proofs/inputs/complaint/masks.json

For this run the feature spec counts as pre-approved by the platform owner: once it is complete and valid, set status: approved, approvedBy: pipeline-proof (pre-approved) and approvedOn: 2026-11-07 in it, then carry on through the whole pipeline to the commit gate.
You are working unattended in a disposable copy of the repository: nobody will answer questions, so make reasonable assumptions, finish the work, verify it, and leave the result in the working tree. Do not push.
```

## Skill sequence

- Expected: build-feature > new-module > new-transition > deadline-recipe > new-permission > new-endpoint > new-screen
- Observed: build-feature > coding-standards > deadline-recipe > new-module > new-permission > new-transition > new-lookup > new-endpoint

## Hook log (refused, then corrected)

- not corrected: backend/src/Modules/Complaints/Infrastructure/CreateComplaintDraftStore.cs [hooks.no-shell-source-writes]
- not corrected: backend/tests/Starter.Contract.Tests/EndpointAllowlist.cs [hooks.platform-owner]
- not corrected: query [hooks.no-shell-source-writes]

## Review log (iterations: 0)

