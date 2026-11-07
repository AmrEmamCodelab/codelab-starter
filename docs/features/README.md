# Feature specs

A **feature spec** is the one document a feature is built from. The `build-feature` skill
([.claude/skills/build-feature/SKILL.md](../../.claude/skills/build-feature/SKILL.md)) writes it from the input (a
plain-language scenario, a BRD, an HLS-format use-case workbook, a DGA screenshot or a Figma frame), a human approves
it, and only then do the builders touch code. It lists exactly which modules, statuses and transitions, deadlines,
permissions, endpoints, screens, rule files, tests and docs the feature creates, and the ordered skill plan that
creates them.

| File | What |
| --- | --- |
| [TEMPLATE.md](TEMPLATE.md) | The format: copy it to `docs/features/<feature>.md` |
| [examples/site-visit-permit.md](examples/site-visit-permit.md) | A worked, valid draft for "clients request a site-visit permit; the inspector approves; undecided after 5 days it escalates to the manager" |
| `<feature>.md` | One per feature, named by its `feature` slug |

## The format

Front matter (all keys required):

| Key | Value |
| --- | --- |
| `feature` | kebab-case slug, the same as the file name |
| `status` | `draft` or `approved` |
| `approvedBy` | empty at draft; who approved (a headless proof run records `pipeline-proof (pre-approved)`) |
| `approvedOn` | empty at draft; the approval date, `YYYY-MM-DD` |
| `inputs` | a list: the repo-relative input files, or the plain-language scenario |

Then these H2 sections, all required, in this order (write `None.` in a section the feature does not need; put extra
detail in H3 sections inside them): Scenario, Inputs, Modules, Statuses and transitions, Deadlines, Permissions,
Endpoints, Screens, Rule files, Tests, Docs, Skill plan, Hand-edit log, Review log.

| Section | Checked shape |
| --- | --- |
| Statuses and transitions | table `From \| Action \| To \| Permission \| Reason required`; Reason required is `yes` or `no` |
| Endpoints | table `Method \| Route \| Permission \| Request \| Response`; method `GET`, `POST`, `PUT`, `PATCH` or `DELETE`; route starts with `/` |
| Screens | table `Route \| Page pattern \| Permission \| Target image`; one of the 8 page patterns ([docs/design/page-patterns.md](../design/page-patterns.md)) |
| Skill plan | ordered list; each item starts with `new-module`, `new-transition`, `deadline-recipe` ([add-deadline recipe](../recipes/add-deadline.md)), `new-permission`, `new-lookup`, `new-endpoint` or `new-screen`; `new-module` items first |
| Hand-edit log | table `# \| File \| Reason`; more than 10 rows is a warning (the pipeline target is at most 10) |
| Review log | a line `Iterations: <n>` (0 before the first review) |

No table cell is empty. A section that declares work (a transition, an endpoint, a screen, a deadline) without the
matching skill-plan item is a warning.

## Check a spec

With the private Node on `PATH`:

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
node tools/feature-spec/check-spec.mjs docs/features/<feature>.md                       # format (draft or approved)
node tools/feature-spec/check-spec.mjs docs/features/<feature>.md --require-approved    # the build gate
node tools/feature-spec/check-spec.mjs docs/features/<feature>.md --json                # findings, status, handEditCount
```

Exit codes: `0` valid (warnings may be printed), `1` error findings, `2` usage or unreadable file. Tools import
`checkSpec(text)` from `tools/feature-spec/check-spec.mjs`; see [tools/feature-spec/MODULE.md](../../tools/feature-spec/MODULE.md).

## The approval gate

1. `spec-writer` and `workflow-designer` write the draft (`status: draft`, `approvedBy` and `approvedOn` empty) and
   run the check until it exits 0.
2. The pipeline **stops**. A human reads the spec (the transition table and the Mermaid diagram, the open
   decisions, the skill plan) and either asks for changes or approves it by setting `status: approved`,
   `approvedBy: <name>` and `approvedOn: <date>`.
3. Every builder runs the check with `--require-approved` first and refuses a draft.

A headless run (the pipeline proofs) may pre-approve **only** when its run prompt says the spec is pre-approved; the
spec then records `approvedBy: pipeline-proof (pre-approved)` and the date. Nothing else approves a spec.

## Lifecycle

| Stage | Who | What changes in the spec |
| --- | --- | --- |
| Draft | `spec-writer`, `workflow-designer` | everything; `status: draft` |
| Approved | a human | `status`, `approvedBy`, `approvedOn` |
| Built | `api-designer`, `backend-builder`, `screen-builder`, `test-writer` | the Hand-edit log: one row per edit after approval that no skill or agent step produced, with the file and the reason |
| Reviewed | `visual-verifier`, the reviewers | the Review log: findings per iteration and how each was resolved; `Iterations: <n>` |
| Committed | `commit-gate` | nothing; it re-runs the check with `--require-approved` |

A change of scope after approval goes back through the gate: record it in the Review log, set `status: draft`,
clear the approval, change the spec and get it approved again. The spec stays in the repository after the feature ships; it is the record of why
the feature looks the way it does.
