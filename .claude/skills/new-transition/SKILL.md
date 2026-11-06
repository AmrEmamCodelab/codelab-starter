---
name: new-transition
description: "Add a status and/or a transition to a record type's workflow (module 8): the status constant with the next explicit int and its status key, the TransitionRule entry with permission, side, reason requirement and field, label and an explicit notification decision, the ar and en wording and label keys, TransitionFixture test stubs, the MODULE.md note, then the regenerated workflow sheet (docs/generated/workflows/<record-type>.md, Mermaid) and statuses.md. Use when the work needs a new status, a new step in an approval flow, a new decision (approve, reject, return, escalate, put on hold, reopen) or a changed status flow."
---

# New transition

Load the `coding-standards` skill first ([docs/standards/SKILL.md](../../../docs/standards/SKILL.md)). Engine
contracts: the Workflow sections of [Starter.Domain](../../../backend/src/Starter.Domain/MODULE.md) and
[Starter.Application](../../../backend/src/Starter.Application/MODULE.md); test kit
[backend/tests/Starter.Workflow.Testing/README.md](../../../backend/tests/Starter.Workflow.Testing/README.md). A whole
new record type is not this skill: follow [docs/recipes/add-workflow-type.md](../../../docs/recipes/add-workflow-type.md).

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
   kit reference, and a note in the module's `MODULE.md`.
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
6. **Docs:** the run adds the MODULE.md note; add any new rule or gotcha by hand.

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
