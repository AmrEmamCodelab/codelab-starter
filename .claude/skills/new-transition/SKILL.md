---
name: new-transition
description: "Use when the work needs a new status, a new step in an approval flow, a new decision (approve, reject, return, escalate, put on hold, reopen) or any changed status flow of a record type, and whenever a hook or CI refusal says 'Use the `new-transition` skill' (a transition or status outside the workflow catalog). Adds the status and/or transition to the record type's catalog (module 8) in one run: the status constant with the next explicit int and its status key, the TransitionRule entry with permission, side, reason, label and an explicit notification decision, the ar and en wording and label keys, TransitionFixture unit tests, the MODULE.md note, the workflow-in-the-catalog rule and recipe in module.rules.yaml, then the regenerated workflow sheet (docs/generated/workflows/<record-type>.md, Mermaid) and statuses.md."
---

# New transition

Load the `coding-standards` skill first ([docs/standards/SKILL.md](../../../docs/standards/SKILL.md)). Engine
contracts: the Workflow sections of [Starter.Domain](../../../backend/src/Starter.Domain/MODULE.md) and
[Starter.Application](../../../backend/src/Starter.Application/MODULE.md); test kit
[backend/tests/Starter.Workflow.Testing/README.md](../../../backend/tests/Starter.Workflow.Testing/README.md). A whole
new record type is not this skill: follow [docs/recipes/add-workflow-type.md](../../../docs/recipes/add-workflow-type.md).

## When this applies

- A record type that already has a workflow gains a status, an action between statuses, or a changed rule (who, from
  where, with which reason, who is told). A time limit on a status is the deadline recipe
  ([docs/recipes/add-deadline.md](../../../docs/recipes/add-deadline.md)) after this skill.
- **Refusal counterpart:** the flow lives only in the module's catalog class (one `TransitionCatalog`). The PreToolUse
  hook and the CI content check (`node tools/rules-engine/check-content.mjs`) refuse a transition or status written
  outside it with the message "Use the `new-transition` skill"; the host's `WorkflowCatalogStartupCheck` and the
  contract tests refuse an incomplete rule. The module's rule `<lower-case module>.workflow-in-the-catalog` (added by
  the first run when missing) states it.

## What one run emits

| Part | Emitted |
| --- | --- |
| Code | yes: `<Entity>Statuses` (+ `<Entity>StatusKeys`), `<Entity>Actions`, the `Statuses` and `Rules` lists of the workflow class (the catalog), the notification template constant, ar and en wording and label keys |
| Rules | yes: the required rule `<lower-case module>.workflow-in-the-catalog` (unless the module already has a rule ending in `.workflow-in-the-catalog`) and `.claude/skills/new-transition/SKILL.md` in `recipes`; only when the run added something |
| MODULE.md | yes: one Use note per new status and per new transition (under `<!-- scaffold:workflow -->` when present) |
| Unit tests | yes: `Workflow/<Status>StatusTests.cs` and `Workflow/<Action>From<Status>TransitionTests.cs` on `TransitionFixture`, plus the test-kit reference |
| Generated docs | rerun automatically (rules compile, `Starter.DocsGen`: `docs/generated/workflows/<record-type>.md` and `statuses.md`); `--no-gen` prints the DocsGen command, `--skip-generate` and `--dry-run` print the refresh as `next:` lines |

## Quick start

From the repo root, with the private Node on `PATH`. A new status and the transition into it, in one run (a status
nothing reaches stops the host at startup):

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
node tools/scaffold/new-transition.mjs --module ServiceRequests `
  --status escalated --status-en "Escalated;Escalated to a manager;Escalated" --status-ar "مُصعَّد" `
  --action escalate --from under-review --to escalated --permission service-requests.decide --side staff `
  --reason required --label-en "Escalate" --label-ar "تصعيد" `
  --notify requester,assignee --template service-requests.escalated --dry-run
```

Drop `--dry-run` to write. A transition between existing statuses needs only the `--action` part; one action legal
from several statuses takes `--from a,b` (one rule each). A requester action owned by the record's owner adds
`--owner`.

| Flag | Meaning |
| --- | --- |
| `--status <kebab>` `[--terminal]` | the new status key; its constant gets the next free int of `<Entity>Statuses` (never renumber) |
| `--status-en`, `--status-ar` | one wording for every audience, or `requester;staff;admin`. Both required; never invent Arabic |
| `--action <kebab>` `--from` `--to` | the action key and the status keys it moves between (`--to` may be the new status) |
| `--permission <key>` | a key that is already a `<Module>PermissionKeys` constant (else `new-permission` first) |
| `--side requester\|staff\|system` | which screens offer it (`system`: a background job) |
| `--reason none\|optional\|required` `[--reason-field <field>]` | the reason dialog; the field defaults to `reason` |
| `--label-en`, `--label-ar` | the button label; needed unless `<prefix>.action.<action>` exists in both bundles |
| `--notify none` or `--notify requester[,assignee] --template <key>` | the notification decision; required: "nobody" is a decision, never an omission |
| `--i18n-prefix <camel>` | defaults to the camelCase module name (`serviceRequests`) |
| `--no-gen` / `--skip-generate` / `--dry-run` | print the DocsGen command instead / skip it silently / write nothing |

Exit 0 done or already present (a second identical run is `unchanged`), 1 refused and nothing written (unknown
module, status or permission, missing Arabic label, a constant name holding another value, no workflow found), 2
usage error (for example a transition out of the new terminal status, a missing `--notify`).

## Workflow

1. **Decide the change with the user.** Which statuses, who acts (side and permission), whether a reason is required
   and which field holds it, who is told (template or nobody), the Arabic and English wording per audience and the
   label. A new permission comes first through the `new-permission` skill; a new notification template is seeded by
   [docs/recipes/add-notification.md](../../../docs/recipes/add-notification.md) (ar and en, InApp and Email).
2. **Dry run**, read the file list, then run it for real. It edits only the record type's module and its test
   project: `<Entity>Statuses` (+ `<Entity>StatusKeys`), `<Entity>Actions`, the `Statuses` and `Rules` lists of the
   workflow class (above `// scaffold:workflow-statuses` / `// scaffold:workflow-rules` when present), the template
   constant in `<Entity>NotificationKeys` (and its `All` list), `ar.json` and `en.json`, one test class per new status
   (`Workflow/<Status>StatusTests.cs`) and per new rule (`Workflow/<Action>From<Status>TransitionTests.cs`), the test
   kit reference, a note in the module's `MODULE.md`, and (first time only) the workflow rule and the recipe in its
   `module.rules.yaml`.
3. **Read the generated entries and stubs.** Entries use named arguments; keep them. Extend the stubs with the cases
   the business adds (for example "only the assignee may escalate"). Update the module's own table tests that pin
   the whole flow (the sample's `ServiceRequestWorkflowTests`: allowed actions per status, terminal list, notified
   rules): they fail on purpose after a flow change.
4. **Wire what the engine does not.** A status with a time limit needs an SLA
   ([docs/recipes/add-deadline.md](../../../docs/recipes/add-deadline.md)); a status the screens style specially
   needs its tone in the frontend area. Screens never list actions: they render the server's `allowedActions`.
5. **Regenerate the docs** (the run does it unless `--no-gen`/`--skip-generate`):
   `& "E:\codelab-tools\dotnet10\dotnet.exe" run --project backend/tools/Starter.DocsGen -- --out docs/generated`.
   Review `docs/generated/workflows/<record-type>.md` (diagram and transition table) with the user; it is the client
   sign-off sheet. `statuses.md` lists the new status with its three wording keys.
6. **Docs:** the run adds the MODULE.md note and the rules entry (the rules compile then refreshes the nested
   `CLAUDE.md`); add any new business rule or gotcha by hand.

## Verify

```powershell
$dotnet = "E:\codelab-tools\dotnet10\dotnet.exe"
& $dotnet test backend/tests/Modules/Starter.Modules.<Module>.Tests --filter FullyQualifiedName~Workflow
& $dotnet test backend/tests/Starter.Contract.Tests --filter FullyQualifiedName~Workflow   # permission, wording, notification decision per rule
& $dotnet run --project backend/tools/Starter.DocsGen -- --out docs/generated --check       # the sheet and statuses.md are current
node --test tools/scaffold/tests/new-transition.test.mjs
```

The host also validates every catalog at startup (`WorkflowCatalogStartupCheck`): an unreachable status, a rule
without a known permission, a required reason without a field, a missing wording or notification decision, or a rule
out of a terminal status stops the boot and lists every violation.

## More

Generator details and exit codes: [tools/scaffold/README.md](../../../tools/scaffold/README.md). The sheets and the
drift check: [backend/tools/Starter.DocsGen/MODULE.md](../../../backend/tools/Starter.DocsGen/MODULE.md).
