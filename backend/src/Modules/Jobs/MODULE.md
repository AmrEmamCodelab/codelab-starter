# Jobs module

## Purpose

Background jobs (module 6b): every piece of work that runs outside an HTTP request on a schedule or on an
administrator's "run now". It replaces jobs that acted as a seeded user holding a business role with jobs that run as the
**system actor** (`SYSTEM`), and an outbox that nobody drained with a drain every 5 minutes. The module owns:

- **The job catalog**: every `IJob` (kernel port in `Starter.Application.Jobs`) that any module registers with
  `services.AddStarterJob<TJob>()` (`Starter.Infrastructure.Jobs`). Keys follow `JobKeyFormat` (lowercase kebab, 3 to
  64 characters); two jobs with one key, a malformed key or a missing name stop the catalog from being built.
- **`jobs.JobRuns`**: the run history. One row per run: `JobKey`, `Status` (`JobRunStatus`, int: `Queued = 1`,
  `Running = 2`, `Succeeded = 3`, `Failed = 4`), `Trigger` (`JobTrigger`, int: `Schedule = 1`, `Manual = 2`), `Actor`
  (always `SYSTEM`), `TriggeredBySubjectId` (the administrator's token subject for a manual run), `QueuedAt`,
  `StartedAt`, `FinishedAt`, `CorrelationId`, and for a failure only `ErrorCode` (a classified code) and
  `ReferenceCode`. The table is `[NotAudited]`: it is a trail of its own.
- **The scheduler**: Hangfire with SQL Server storage in schema `hangfire` of the application database. Recurring
  schedules come from each job's `Cron`, in the `Jobs:TimeZoneId` time zone. The Hangfire dashboard is never mapped.
- **The built-in jobs**: `outbox-drain` (`*/5 * * * *`, one pass of the kernel `IOutboxDrainRunner`) and
  `security-replay-purge` (`0 2 * * *`, Codelab.Security's `SecurityStorePurgeJob` over the SQL replay and session
  revocation stores).
- **The admin endpoints**:

| Endpoint | Permission | Result |
| --- | --- | --- |
| `GET api/admin/jobs` | `jobs.view` | `[{key, nameAr, nameEn, cron, lastRun}]`, ordered by key |
| `POST api/admin/jobs/{key}/runs` | `jobs.run` | 202 `{runId}`; 404 `JOBS_JOB_NOT_FOUND`; 409 `JOBS_RUN_IN_PROGRESS` |
| `GET api/admin/jobs/{key}/runs?take=20` | `jobs.view` | `[JobRunDto]`, newest first; 400 `take` outside 1 to 100; 404 unknown key |

`JobRunDto` is `{id (PublicId), status, trigger, actor: "SYSTEM", triggeredBy (display name or null), startedAt,
finishedAt, referenceCode}`; times are ISO 8601 UTC, enums their integer values.

## Use

**How a run executes.** A recurring tick (`HangfireJobEntry.RunScheduledAsync`) or a queued manual run
(`RunManualAsync`) is resolved by Hangfire in a fresh DI scope and handed to `JobRunner`, which:

1. takes the job's run lock (a Hangfire distributed lock, `starter:jobs:<key>`, across every node). A held lock means
   the job is executing: a tick is skipped without a row; a manual run ends `Failed` with `JOBS_RUN_OVERLAPPED`;
2. opens a system scope (`ISystemActorScope`): `ICurrentUserContext` reads as anonymous and `IAuditContextAccessor`
   answers the system actor, so every audited write of the job is recorded as `SYSTEM`;
3. opens a fresh correlation scope (32 hex), marks the run `Running`, and closes the job's other `Running` rows as
   `Failed` with `JOBS_RUN_ABANDONED` (their node stopped mid-run);
4. calls `IJob.RunAsync`, then records `Succeeded`, or `Failed` with the code from `IFailureCodeResolver` (the same
   classifier table as the HTTP error handler) and the reference code of the one log line. The exception message is
   never stored.

**Run a job now from code.** Do not call a job class directly from a request; trigger a run through the admin
endpoint, or (from another module) enqueue your own outbox message and let the drain deliver it.

**Configuration** (`Jobs`, validated on start):

| Key | Default | Meaning |
| --- | --- | --- |
| `Jobs:Enabled` | `true` | Run the job server on this node (executes runs, registers the schedules on start). A node with `false` still queues manual runs for the others. |
| `Jobs:WorkerCount` | `5` | Worker threads of this node's job server (1 to 20). |
| `Jobs:TimeZoneId` | `Arab Standard Time` | Time zone of the cron schedules. |
| `Jobs:PrepareSchemaIfNecessary` | `true` | Let Hangfire create or upgrade its tables in schema `hangfire` (needs DDL rights); `false` when the DBA installs them. |

## Extend

**Add a job** (the add-job recipe):

1. In the owning module (never in this one), add the key and the schedule as constants, for example
   `public const string ReminderSweep = "reminder-sweep";` and `public const string EveryFifteenMinutes = "*/15 * * * *";`
   (five-field cron in the jobs time zone; `null` for a manual-only job).
2. Implement `IJob` in the module's Application layer (or Infrastructure when it wraps an adapter):

   ```csharp
   public sealed class ReminderSweepJob(IReminderSweep sweep) : IJob
   {
       public const string ArabicName = "إرسال التذكيرات";
       public const string EnglishName = "Send reminders";

       public string Key => MyModuleJobKeys.ReminderSweep;
       public string NameAr => ArabicName;
       public string NameEn => EnglishName;
       public string? Cron => MyModuleSchedules.EveryFifteenMinutes;

       public Task RunAsync(JobRunContext context, CancellationToken cancellationToken) =>
           sweep.RunAsync(cancellationToken);
   }
   ```

   The job is resolved per run in its own DI scope, so it may inject scoped ports and DbContexts. Make `RunAsync`
   safe to run again from the start (a run that died is not resumed), honour the cancellation token (it fires when
   the node stops), and throw to fail the run. Log identifiers only (`context.RunId`, counts), never personal data.
3. Register it in `Add<Name>Module`: `services.AddStarterJob<ReminderSweepJob>();`.
4. Unit-test the job with fakes (its work, and that a failure throws). Nothing else changes: the catalog, the admin
   screen, the schedule (registered on the next start of a job-server node) and the run history pick it up.

**Change a schedule:** change the job's cron constant; every job-server node updates the recurring job when it starts.
Removing a job (or setting its cron to null) removes its schedule on the next start.

**Another built-in job of this module:** add it under `Application/BuiltIn` (pure ports) or `Infrastructure/BuiltIn`
(wrapping a package or adapter), with its key in `JobsJobKeys` and its cron in `JobsSchedules`, and register it in
`JobsModuleRegistration`.

## Rules

- Every run executes inside `ISystemActorScope` and is recorded with `SystemActor.Name` (`SYSTEM`). A job never acts as
  a user account or a role; the administrator who pressed "run now" is recorded only as `TriggeredBySubjectId`.
- A failed run stores only the classified code and the reference code, never `ex.Message` or a stack trace.
- Hangfire types stay in `Infrastructure/Scheduling`; Application sees `IJobRunDispatcher`, `IJobRunLock` and `IJobRuns`.
- The Hangfire dashboard is never mapped; storage comes from the DI `JobStorage`, never Hangfire's static storage.
- Every endpoint is a thin controller action with `[HasPermission]` (`jobs.view`, `jobs.run`) and one use case.
- The module never references another module; jobs reach it through `AddStarterJob`.

## Gotchas

- **At least one node needs `Jobs:Enabled` = true**, or nothing runs: the outbox is not drained and manual runs stay
  `Queued`. Test hosts without a database (contract tests, DocsGen) must set it to `false`, because a job-server node
  connects to SQL (and installs the `hangfire` schema) on start.
- **The `hangfire` schema is not EF's.** `dotnet ef` never sees it; when `Jobs:PrepareSchemaIfNecessary` is true
  (DDL rights) a job-server node installs or upgrades it at start through the hosted service `SchedulerSchema`,
  registered after the kernel's migration step, so a first start on a database the migrations just created works (its
  storage, resolved while the host is built, has its own schema preparation off). A node without the job server lets
  the storage prepare the schema on first use. Otherwise the DBA runs Hangfire's install script for schema `hangfire`. The `jobs` schema is an ordinary EF migration (`InitialCreate`).
- **Overlap is refused, not queued.** A manual run that meets a running run of the same job ends `Failed`
  (`JOBS_RUN_OVERLAPPED`); a trigger while a run younger than one hour (`JobsLimits.ActiveRunWindow`) is `Queued` or
  `Running` answers 409. A row older than that window no longer blocks a trigger, so a lost run never locks a job out.
- **Hangfire never retries a run** (`AutomaticRetry` 0 on both entry methods): the run row records the failure and
  the next tick, or the next manual trigger, is the retry. A failure that reaches Hangfire itself (storage down) is
  logged by Codelab.Observability's `JobFailureLogFilter` and the Hangfire job is deleted.
- **`HangfireJobEntry` method names are stored** in Hangfire's tables (queued and recurring jobs). Renaming the class,
  its namespace or a method breaks the jobs already stored; add a new method instead and remove the old one later.
- **The run history grows** by one row per tick (about 290 rows a day for the outbox drain); a retention job is not
  part of this module yet.
- **Hangfire keeps some static state** (its serializer settings and log provider); the module configures them once per
  host through `AddHangfire`. Tests that build several containers run their Hangfire tests one at a time.
- **`triggeredBy` is a display name, resolved when the history is read.** The row stores the token subject
  (`TriggeredBySubjectId`); `JobRunViews` resolves each distinct subject once through the kernel `IUserDirectory`
  (Identity's `DisplayName`), so a renamed administrator shows the new name, and a subject without an account shows
  null. The internal subject id never goes on the wire.
