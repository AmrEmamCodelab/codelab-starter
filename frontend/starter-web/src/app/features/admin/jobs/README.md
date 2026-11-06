# Background jobs screens (`features/admin/jobs`)

Part of the `features/admin` module (WebAdmin; its [MODULE.md](../MODULE.md) is the module's doc). This folder holds
the job catalog and a job's run history, built on the page patterns ([patterns/MODULE.md](../../../patterns/MODULE.md))
over the backend's job endpoints (`GET api/admin/jobs`, `POST api/admin/jobs/{key}/runs`,
`GET api/admin/jobs/{key}/runs?take=20`).

## Purpose

| Screen | Route (under `/admin/jobs`) | Key | Pattern |
| --- | --- | --- | --- |
| Job catalog: name in the current language and key, schedule (cron, or "On demand only"), last run as a status tag with its start time (or "Never run"), a link to the run history, "Run now" behind a confirmation | `''` | `jobs.view` (run `jobs.run`) | admin list + editor dialog |
| Run history: the latest 20 runs, newest first, with status tag, trigger (schedule / manual), actor (always the system account), who triggered a manual run, start and finish times and a failed run's reference code; Refresh, "Run now" | `:jobKey/runs` | `jobs.view` (run `jobs.run`) | admin list + editor dialog |

Every job runs as the system account (`SYSTEM`), also when an admin starts it; the admin is recorded as
"Triggered by". A run started from the catalog opens that job's history. While a run is queued or running the screen
reloads every 2 seconds, for at most a minute (`RunPoller`); Refresh starts again.

| Piece | What it does | File |
| --- | --- | --- |
| `AdminJobsClient`, `JobsApiPaths`, `JOB_RUNS_TAKE` | The three endpoints over `ApiService`; quiet calls (refusals shown inline) | `admin-jobs.client.ts` |
| `JobRunStatus`, `JobTrigger`, `JobRun`, `JobSummary`, `SYSTEM_ACTOR` | The wire shapes (enums as their integers, run ids as `WireId`) | `job.models.ts` |
| Status / trigger labels and tones, `isRunActive`, `jobName`, `actorLabelKey` | Display rules (an unknown status falls back to a neutral tag) | `job-labels.ts` |
| `JobRunTrigger` | The confirmed "Run now" shared by both screens: pending job, busy, refusal | `job-run-trigger.ts` |
| `RunPoller` | Reloads while a run is unfinished; stops on destroy | `run-poller.ts` |
| `JobsPageStore`, `JobRunsPageStore` | Load (latest answer wins, a failure clears the rows), paging, run, polling | `jobs-page.store.ts`, `job-runs-page.store.ts` |
| `JobsPageComponent`, `JobRunsPageComponent` | Render the stores; the history binds `jobKey` from the route | `*-page.component.*` |
| `JOBS_ROUTES`, `JobsPaths`, `JOBS_MOUNT_SEGMENT`, `JobsRouteParams` | The routes (both gated by `jobs.view`) and links | `jobs.routes.ts`, `jobs.paths.ts` |
| `JobsPermissionKeys` | `jobs.view` / `jobs.run` until `npm run api:gen` adds them to `PermissionKeys` | `jobs-permissions.ts` |
| `JobsTranslationKeys` | Every text (`admin.jobs.*`) | `jobs-translation-keys.ts` |

## Use

- Mount: `ADMIN_ROUTES` adds `{ path: AdminRouteSegments.Jobs, loadChildren: () => import('./jobs/jobs.routes').then(m => m.JOBS_ROUTES) }`
  where `AdminRouteSegments.Jobs` is `JOBS_MOUNT_SEGMENT`; the admin home's tile links `JobsPaths.list()`, a row
  links `JobsPaths.runs(job.key)`.

## Extend

- **A new job** needs no frontend change: the backend registers it (`AddStarterJob<TJob>()`) and the catalog lists it.
- **A new run status or trigger:** a member with the backend's value in `job.models.ts`, its label and tone in
  `job-labels.ts`, texts in both bundles, specs.

## Rules

- No job logic here: the server owns schedules, the system actor and run state; the screens only show them and ask
  for a run.
- Cron expressions, job keys and reference codes are code: they render left-to-right (`data-fixed-direction`).

## Gotchas

- `jobs-permissions.ts` casts the two keys to `PermissionKey`: after `npm run api:gen` the integrator replaces every
  `JobsPermissionKeys.*` with `PermissionKeys.JobsView` / `PermissionKeys.JobsRun` and deletes the file.
- The history title needs the catalog (for the job's name); until it answers, or when the catalog no longer lists the
  key, the page shows the key and "Run now" stays disabled.
- `jobs-translations.spec.ts` checks the bundles for every key of `JobsTranslationKeys` plus the home tile.
