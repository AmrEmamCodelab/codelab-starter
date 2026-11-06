# Starter.Integration.Tests

The integration tier: the real host on a throwaway SQL Server database.

## What it does

Each fixture (`DevelopmentHost`, `ProductionHost`, `UsersHost`, `Phase5Host`, `JobServerHost`, ...):

1. starts LocalDB when the server is LocalDB (`sqllocaldb start <instance>`, a no-op when it runs);
2. creates `StarterIt_<guid>` (the solution token comes from the host assembly, so a renamed fork gets `<Token>It_<guid>`);
3. starts the host with accounts and keys generated at run time, which migrates every module DbContext, creates the
   Codelab.Security store tables and seeds accounts through the startup tasks (outside Development the fixture
   migrates first, because the kernel refuses to migrate there);
4. drops ONLY that database at the end, also when the start or a test failed, then deletes what else it created (the
   phase 5 hosts' temp folder `starter-it-drops-<guid>`). `TestSqlServer.RequireTestName` refuses to create or drop
   any name without the `It_` marker.

Covered (phases 2-3): break-glass login and `GET /api/dashboard` 200; every catalog permission for break-glass; the
Development user's `GET /api/access/admin` 403 problem+json; logout 204 then the replayed access token 401; refresh
rotation with the old token refused (and the session ended); a wrong password as a refusal problem+json; the dev user
absent in Production; `/api/features` flags only; every migrated schema with its own migration history, and the
security-store tables; users, organizations, grants and the login modes.

Covered (phase 5 exit criteria, real SQL):

| Folder | What it proves |
| --- | --- |
| `Settings/` | A setting changed through the admin API on node A is seen by node B (a second host on the same database, its own snapshot cache, a `FakeTimeProvider`): on B's next request with the default per-request stamp check, and exactly when B's 30 s snapshot expires without it. |
| `Jobs/` | A job server on a database the host's first migration creates (no pre-created database) starts and schedules its recurring jobs (regression: `Invalid object name 'hangfire.Hash'`). The scheduler dashboard routes (`/hangfire`...) answer 404, also to the administrator. A manual `outbox-drain` run on the real job server (Hangfire, schema `hangfire`) is recorded with actor `SYSTEM` (the administrator only as `triggeredBy`), delivers the activation e-mail, and every audit row written under a run's correlation id has actor `SYSTEM`. |
| `Messaging/` | A message is delivered once and never again; a drain that "crashed" after the handler (no Sent mark) is redelivered after the 5-minute lease with the same message id, and the notification e-mail is not sent twice; a failing handler is retried with the 1/5/15/60-minute back-off and ends `Failed` after 8 attempts with a code, never the exception text. |
| `Auditing/` | Deleting a record that has history is refused by SQL (error 547; `HasRecordHistory` maps the FK Restrict) on a probe context; the PII canary: a national id, e-mail, phone and name planted through contact us, the Excel import and the notifications never reach a retained log sink (Codelab.Testing `CanaryLogSink`) or `audit.AuditEntries` unmasked. |
| `Notifications/` | The test notification: the bell feed shows the seeded wording in Arabic and in English, mark-read lowers the unread count, the e-mail is dropped as `.eml` in the recipient's language; the file SMS sender records an Arabic and an English SMS. |
| `Portal/` | Contact us answers 201 `CU-<year>-NNN` (year in Riyadh time), stores the row and e-mails the inbox; with `portal.contact-enabled` set false through the admin API it answers 409 `PORTAL_CONTACT_DISABLED` and stores nothing; numbering restarts at 001 when the Riyadh year rolls over; one client over the limit gets 429 problem+json; an administrator's FAQ replace shows at once on the anonymous (cached) page in both languages. |
| `Activation/` | An Excel-imported user with an e-mail address: the drain e-mails the link, its fragment ticket sets a password, the user signs in, the reused link is refused (`IDENTITY_ACTIVATION_REJECTED`). |
| `Lookups/` | Active values for a signed-in user, the cache (a change behind the API stays invisible until a deactivation through the API drops the list), the admin type list, 404 for an unknown type, 401 anonymous. |
| `SupportTests/` | Unit tests (no database) of the support code below. |

## Use

```powershell
E:\codelab-tools\dotnet10\dotnet.exe test backend/tests/Starter.Integration.Tests
$env:CODELAB_TEST_SQL = 'myserver\instance'   # optional: another server (integrated security); a connection string such as 'Server=(localdb)\Other;Integrated Security=true' also works (its data source is used), so step 6 can share the value with the package SQL tests
```

## Extend

Add a test class with `[Collection(DevelopmentHostGroup.Name)]` (or another group) and drive the API through
`StarterClient` or `ApiClient`. A new environment-specific behaviour gets its own `IntegrationHost` subclass and
collection; override `ExtraSettings` for configuration and `ConfigureServices` for test services.

The phase 5 support (`Support/`):

- `Phase5Host`: a `ShiftableClock` as the host's `TimeProvider` (real time plus an offset; `TravelTo`/`Advance`, and
  always `Reset` in a `finally`); e-mail as `.eml` in `Mail` (`MailDrop`, `EmlReader`) and SMS (enabled, file mode) in
  `Sms` (`SmsDrop`); the contact inbox `InboxEmail`; the canary sink `LogSink` over `Canaries`; `Recipients` (the
  recipients directory override: accounts store no mobile or language yet); the probe outbox handlers (`Probes`);
  `DrainOutboxAsync()` (one drain pass, as the job does); no job server.
- `JobServerHost`: the same with `Jobs:Enabled` true (Hangfire runs against the test database).
- `FreshDatabaseJobServerHost`: the job server host without the pre-created database (`CreatesDatabaseBeforeStart`
  false); the host's migrations create it.
- `Settings/SecondNode`: a second host on a fixture's database, with its own `FakeTimeProvider`.
- `TestSqlServer.ScalarAsync` / `ReadStringsAsync` / `ExecuteAsync` take parameters; never interpolate a value into SQL.

## Gotchas

- Collections run one after another (`xunit.runner.json`), so at most one test database exists at a time (a second
  node shares its fixture's database).
- Tests in one collection share a database and an outbox: assert on your own rows (a unique suffix, a probe marker,
  ids after a starting id), never on totals. A drain delivers every due message, not only yours.
- The phase 5 host clock is shared: a test that shifts it resets it in `finally`. Call the authenticated API only while
  the offset is zero, so tokens and sessions keep real times.
- `Phase5Host.Recipients.Use(...)` applies to every user until disposed; dispose it before the test ends.
- The rate-limit test sends from its own client address (`TestServer.SendAsync` sets `RemoteIpAddress`), so it never
  starves the other contact tests.
- The job server host runs the recurring outbox drain too: a manual run that meets a scheduled tick fails as
  `JOBS_RUN_OVERLAPPED` by design and the test triggers again.
- Every host except `JobServerHost` sets `Jobs:Enabled` false (the shared host settings, plus `Phase5Host`); a job
  server connects to SQL and installs schema `hangfire` when it starts.
- Request and response signing are Off here; signing is covered by the Codelab.Security golden vectors and the
  browser check.
- Run it from a normal shell. A LocalDB instance that is first started from inside a restricted sandbox can be
  unreachable for every other process (named-pipe error 121) until it is stopped with `sqllocaldb stop <instance> -k`.
- A process killed mid-run leaves its `StarterIt_*` database (and a `%TEMP%\starter-it-drops-*` folder) behind; drop
  or delete them by hand.
