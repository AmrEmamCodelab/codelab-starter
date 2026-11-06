# Deadlines module

## Purpose

Deadlines and SLA timers (module 10), generic over every record type. A record type's own module declares "status X
has N days" (an `ISlaPolicySource`); this module does the rest:

- **`deadlines.RecordDeadlines`**: one row per record and status with an SLA: `RecordId`, `RecordType`, `Status` (the
  record type's int value), `StartedAt`, `DueAt`, `DueSoonAt` (both frozen when the row opens), `BreachedAt` (the scan
  marked the breach), `EscalatedAt` (the scan claimed the one escalation), `ClosedAt` (the record left the status). At
  most one open row per record (filtered unique index `UX_RecordDeadlines_RecordId_Open`); the scan reads the open rows
  by `DueAt` (`IX_RecordDeadlines_DueAt_Open`). `[NotAudited]`: timer state; the record history holds the transitions.
- **The timers** (`DeadlineTransitionObserver`, an `IRecordTransitionObserver`): on every committed record transition
  the open deadline closes when the record leaves its status, and a new one opens when the record enters a non-terminal
  status with an SLA. A replayed transition writes nothing.
- **The scan job** `deadlines-sla-scan` (`DeadlinesJobKeys.SlaScan`, every 5 minutes, actor `SYSTEM` through the Jobs
  module): marks every open deadline whose `DueAt` has passed (exactly at `DueAt` counts), then raises ONE escalation
  notification per breach (`SlaDefinition.EscalationTemplateKey`, bell and e-mail, placeholders `recordNumber`,
  `statusKey`, `dueAt` from `DeadlinesNotifications`).
- **The SLA badges** (`RecordSlaReader`, the kernel `IRecordSlaReader`): `OnTrack` (1), `DueSoon` (2, from `DueSoonAt`),
  `Breached` (3, from `DueAt`) and the `DueAt`, at the current `TimeProvider` time, for the queue and detail views.
  Records without an open deadline are absent.

There is no endpoint and no permission: runs, schedules and the "run now" button are the Jobs module's (admin Jobs
screen, `jobs.view` / `jobs.run`).

## Use

Other modules never call this module; they plug into kernel ports:

| You want | Do |
| --- | --- |
| An SLA on a status | Register an `ISlaPolicySource` (singleton) with `SlaDefinition(RecordType, Status, Duration, DueSoonBefore, EscalationTemplateKey)` and seed the template: [the add-deadline recipe](../../../../docs/recipes/add-deadline.md). |
| A badge in a list or a detail | Resolve `IEnumerable<IRecordSlaReader>` and call `BadgesAsync(recordIds)` once per page (absent: no badge). |
| Timers that follow your record | Nothing: the Records module calls every `IRecordTransitionObserver` after it commits a transition. |

Who is notified on a breach (`EscalationRecipients`): the current assignee (`IRecordAssignmentReader`, the Assignments
module), else every holder of the record type's queue permission (`RecordTypeRegistration.QueuePermissionKey`) who
reaches the record's organization (`IPermissionHolderDirectory`). Both are optional; with neither, the breach is
claimed, counted as unaddressed and logged.

## Extend

- **A new SLA**: [docs/recipes/add-deadline.md](../../../../docs/recipes/add-deadline.md). Nothing changes here.
- **Another reaction to a breach** (for example an automatic transition): add it in the scan's escalation step
  (`SlaEscalations.EscalateOneAsync`) behind a port the owning module implements; keep the claim-before-act rule.
- **Working-day calendars**: replace the wall-clock `startedAt + Duration` in `RecordDeadline.Open` with a calendar
  port; the frozen `DueAt`/`DueSoonAt` keep every reader unchanged.

## Rules

- `module.rules.yaml` holds this module's rules; run `node tools/rules-engine/compile.mjs` after changing it.
- Kernel ports only: no reference to another module, no record type, status or template of its own. The vocabulary
  sweep (`Starter.Architecture.Tests/Engine`) covers this folder.
- A breach is escalated at most once per recipient: `EscalatedAt` is saved before the first notification is sent, and
  released only when the escalation failed before reaching anyone.
- Every instant comes from `TimeProvider` (the scan, the reader) or the `RecordTransitioned.At` of the event (the
  observer). Enums are ints with explicit values; limits are `DeadlinesLimits`.
- Every rule has unit tests in `backend/tests/Modules/Starter.Modules.Deadlines.Tests` (FakeTimeProvider); the real-SQL
  proof (exit E2E #9) is `backend/tests/Starter.Integration.Tests/Deadlines`.

## Gotchas

- **The job key is `deadlines-sla-scan`, not `deadlines.sla-scan`.** `JobKeyFormat` allows no dot (the key is a
  Hangfire recurring id and a URL segment), so the phase contract's dotted key could not be registered.
- **At most about 5 minutes late.** The escalation happens on the first tick after `DueAt`; the badge turns `Breached`
  exactly at `DueAt` (it is computed on read).
- **One escalation batch per run** (`DeadlinesLimits.ScanBatchSize`, 100). Marking repeats batches until none is left;
  a larger backlog of escalations is worked off by the next ticks, oldest deadline first.
- **A failed escalation fails the run after the others.** A missing template (`NOTIFICATIONS_TEMPLATE_NOT_FOUND`) is
  given back and retried on every tick until the template migration ships; the run row shows the code.
- **Stale deadlines heal in the scan.** If an observer call failed after a transition, the scan finds the record in
  another (or a terminal) status, or gone, and closes the deadline instead of escalating it.
- **Without the Records module** (no `IRecordDirectory`) the host still starts; breaches are marked but wait for their
  escalation (warning logged on every run).
- **`IPermissionHolderDirectory` is a module port waiting for a kernel home.** Nothing implements it yet, so an
  unassigned breach reaches no one until the Access module implements the proposed kernel port
  (`Starter.Application.Authorization`, "users who hold a permission at a scope that reaches an organization") and this
  module consumes it.
- **Changing an SLA does not move running deadlines**; removing one leaves running rows that the scan claims without a
  notification (warning `1002`).
- `CLAUDE.md`, `BannedSymbols.generated.txt`, `.editorconfig` and `CodeMetricsConfig.txt` in this folder are generated
  by the rules engine; never edit them by hand.
