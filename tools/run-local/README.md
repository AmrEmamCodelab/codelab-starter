# tools/run-local

## Purpose

The logic behind `run-local.ps1` at the repo root (v1): one command that starts the local stack in Development,
proves it is healthy, and stops only what it started.

## Use

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1                 # API + Attachment service + web (+ Seq)
powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1 -NoWeb          # API + Attachment service
powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1 -NoAttachments  # without the Attachment service
powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1 -Stop           # stop what a previous run started
```

A start:

1. Runs `tools/devseed/New-LocalSecrets.ps1` when a local secrets file is missing or the API file lacks the
   attachment keys (an API file from before the Attachment service or before the dedicated `Codelab:Security:UploadTickets` ring). The script only adds missing keys, so existing
   passwords, sessions and PublicIds keep working; see [tools/devseed/README.md](../devseed/README.md).
2. Starts LocalDB (`sqllocaldb start MSSQLLocalDB`), unless `ConnectionStrings__Starter` is set to another server
   (for example a throwaway database on `(localdb)\P2Proof`): then that instance is left alone.
3. Uses Seq when it answers at `CODELAB_SEQ_URL` or `http://localhost:5341`, starts the `Seq` Windows service when
   it is installed but stopped, and otherwise warns and continues (`-NoSeq` skips this).
4. In this order, with the private .NET SDK and Node:
   - builds the API (`dotnet build backend/src/Starter.Api/Starter.Api.csproj`; a build error stops here) and runs
     the built `Starter.Api.dll` detached from its project folder in Development on http://localhost:5180 (a detached
     `dotnet run` exits at once on Windows);
   - builds the Attachment service (`services/attachment-service/src/Codelab.Attachments.Api`) and runs the built
     `Codelab.Attachments.Api.dll` the same way on http://localhost:5185 (skipped with `-NoAttachments`, or with a
     warning when the project is not in the repository);
   - starts the web app (`npm start --workspace frontend/starter-web`, http://localhost:4280).
5. Waits, in the same order, for `GET /health/ready` on the API, `GET /health/ready` on the Attachment service
   (Codelab.Observability `MapCodelabHealth` maps `/health/live` and `/health/ready`) and HTTP 200 on the web root,
   then prints the URLs, the logs and the break-glass user name.

Exit codes: 0 ok, 1 refused or failed, 2 usage. PIDs and logs live in `%TEMP%\codelab-starter`
(`run-local.json`, `api.log`, `attachments.log`, `web.log`). Tests: `node --test "tools/run-local/**/*.test.mjs"`.

## Extend

- Ports, paths, timeouts and names: `lib/constants.mjs`. Decisions (pure, unit-tested): `lib/plan.mjs`. The
  orchestration: `lib/run.mjs`, which reaches the machine only through the `system` object of `lib/system.mjs`, so
  `tests/run.test.mjs` drives it with a fake.
- A new .NET host: add it to `Services`, `LOG_FILES`, `Urls`, `Timeouts` and `Hosts`, place it in `START_ORDER`, add
  its decision (and flag) to `plan.mjs`; `run.mjs` builds, starts, records and health-checks every planned host.

## Rules

- It never installs or deletes anything: no `npm install`, no database drop, no file removal except its own state
  file. A missing prerequisite is a warning or a refusal with the command to run.
- `-Stop` kills only recorded PIDs, and only while each still runs the recorded image (`tasklist` check), so a reused
  PID is never killed. It kills the process tree (`taskkill /T`), because `dotnet run` and `npm` start children.
- Private toolchain only: `CODELAB_DOTNET` or `E:\codelab-tools\dotnet10\dotnet.exe`; `CODELAB_NODE_DIR` or
  `E:\codelab-tools\node24` first on `PATH`.
- The secrets script is never run with `-Force` from here: regenerating values is the developer's decision.

## Gotchas

- The first start builds the solution and the Angular bundle; the health waits allow 4, 4 and 6 minutes.
- A port already in use (5180, 5185 when the service starts, 4280 when the web starts) is refused before anything
  starts; stop the other process first.
- Starting the `Seq` service may need an elevated shell; without it run-local continues without Seq.
- If a start fails, run-local stops what it started and prints the tail of the failing log.
- With `-NoAttachments` the API still runs, but attachment upload tickets, downloads and removals fail until the
  service runs.
