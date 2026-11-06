# Recipe: add a deadline (SLA) to a record type

Gives one status of a record type a time limit: when a record enters the status a timer starts, the queue and detail
views show an SLA badge (on track, due soon, breached), and when the time runs out the record's assignee (or, while it
is unassigned, the staff of its queue) gets ONE escalation notification. Module 10, Deadlines, does the work; the
record type's own module only **declares** the SLA and seeds the escalation template. There is no generator. Module
docs: [backend/src/Modules/Deadlines/MODULE.md](../../backend/src/Modules/Deadlines/MODULE.md).

## 1. Decide the shape

| Question | Answer |
| --- | --- |
| Which status? | A non-terminal status of the record type's workflow (the startup check refuses a terminal or undefined one). One SLA per status; several statuses may each have one. |
| How long? | `Duration`, more than zero (for example 3 days). The timer runs in wall-clock time from the moment the record entered the status (no working-day calendar). |
| When is it "due soon"? | `DueSoonBefore`, zero or more and shorter than `Duration` (for example 1 day before the deadline). Zero: no due-soon state. |
| Which notification? | A template key of your module (for example `<module>.sla-escalation`), sent on the bell and e-mail with the placeholders `recordNumber`, `statusKey` and `dueAt` (`DeadlinesNotifications`). |
| Who receives it? | The current assignee (`IRecordAssignmentReader`), else every holder of the record type's queue permission (`RecordTypeRegistration.QueuePermissionKey`) who reaches the record's organization. Nothing to configure. |

## 2. Declare the SLA

In the owning module's Application layer, add the duration constants and an `ISlaPolicySource`:

```csharp
public static class LeaveRequestSla
{
    public static readonly TimeSpan UnderReview = TimeSpan.FromDays(3);
    public static readonly TimeSpan UnderReviewDueSoon = TimeSpan.FromDays(1);
}

public sealed class LeaveRequestSlaPolicy : ISlaPolicySource
{
    public IEnumerable<SlaDefinition> Definitions { get; } =
    [
        new(
            RecordType: LeaveRequestRecordType.Key,
            Status: LeaveRequestStatuses.UnderReview,
            Duration: LeaveRequestSla.UnderReview,
            DueSoonBefore: LeaveRequestSla.UnderReviewDueSoon,
            EscalationTemplateKey: LeaveRequestNotificationKeys.SlaEscalation),
    ];
}
```

Register it as a **singleton** in `Add<Name>Module` (the SLA catalog is a singleton that reads every source once):

```csharp
services.TryAddEnumerable(ServiceDescriptor.Singleton<ISlaPolicySource, LeaveRequestSlaPolicy>());
```

`SlaDefinition` refuses a malformed record type key, a blank template key and out-of-range durations when it is built,
so a bad declaration fails at startup. The Deadlines startup check (`SlaPolicyStartupCheck`) also refuses two SLAs on
one status and a status the workflow does not define or that is terminal.

## 3. Seed the escalation template

Follow [the add-notification recipe](add-notification.md): a Notifications migration that inserts the template key on
`InApp` (1) and `Email` (2), each in Arabic and English, using only `{{recordNumber}}`, `{{statusKey}}` and
`{{dueAt}}` (the deadline in UTC, `yyyy-MM-dd HH:mm UTC`). A missing template makes the escalation fail: the scan gives
the breach back, fails the job run with `NOTIFICATIONS_TEMPLATE_NOT_FOUND` and retries on the next tick.

## 4. Nothing else

- Deadlines open and close themselves: the Records module reports every committed transition to
  `IRecordTransitionObserver`, and the Deadlines observer opens a deadline on entering the status and closes it on
  leaving it (also on cancel and on any terminal status).
- The job `deadlines-sla-scan` runs every 5 minutes as `SYSTEM` (admin Jobs screen: "Watch service-level deadlines"); a
  breach is escalated at most about 5 minutes after its deadline.
- Badges come from `IRecordSlaReader` (queue items and the record detail carry `sla: { state, dueAt }`; the web app's
  `SlaBadgeComponent` renders it).

## 5. Test

- A unit test of the policy source: the record type, status, durations and template key of each definition.
- A workflow test that the status is non-terminal (the startup check covers it too).
- The template migration test of the add-notification recipe (both languages, both channels, exactly the three
  placeholders).
- End to end, the Deadlines integration test shows the pattern: `backend/tests/Starter.Integration.Tests/Deadlines`
  enters the status through the observer, advances the host `TimeProvider` past the deadline, runs the scan job and
  finds one escalation notification row (and still one after a second run).

```powershell
E:\codelab-tools\dotnet10\dotnet.exe test backend/tests/Modules/Starter.Modules.Deadlines.Tests
E:\codelab-tools\dotnet10\dotnet.exe test backend/tests/Starter.Integration.Tests --filter FullyQualifiedName~Deadlines
```

## Gotchas

- **The deadline is frozen when it opens.** Changing `Duration` affects records that enter the status afterwards;
  running deadlines keep their due time.
- **Leaving and re-entering the status starts a new deadline** (for example a returned record that is resubmitted and
  reviewed again): the earlier one is closed, never reopened.
- **Removing an SLA** leaves running deadlines in place; when one breaches, the scan claims it without a notification
  and logs a warning.
