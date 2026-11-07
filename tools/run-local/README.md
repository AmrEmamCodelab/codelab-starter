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
powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1 -Slot 1         # a second stack next to a running one
powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1 -Stop -Slot 1   # stop only slot 1
```

### Slots: a second stack next to a running one

A running stack (for example the owner's demo) makes a plain start refuse: its PIDs are recorded and its ports
answer. `-Slot <n>` (1-9) runs the current tree next to it without touching it:

| | Slot 0 (default) | Slot n (slot 1 shown) |
| --- | --- | --- |
| Ports | API 5180, Attachment service 5185, web 4280 | each + n * 100: 5280, 5285, 4380 |
| State and logs | `%TEMP%\codelab-starter` | `%TEMP%\codelab-starter\slot-<n>` |
| .NET build output | the project's `bin\Debug` | `slot-<n>\build\<api or attachments>` (`dotnet build -o`), so the DLLs a running stack holds open are never overwritten |
| Database | as configured | `ConnectionStrings__Starter` (or the development settings) with the database renamed `<name>_Slot<n>`; migrations apply there, never to the other stack's database |
| Attachment storage | as configured (run-local never creates or changes it) | `slot-<n>\attachments-storage`, created (idempotent) before the slot's Attachment service starts, because the service's readiness check fails on a missing storage root |
| Web app | `ng serve` (hot reload) | a frozen development build in `slot-<n>\web`, its `config.json` pointed at the slot's API and Attachment service, served by `static-server.mjs` (unknown routes fall back to `index.html`) |

The slot's API and Attachment service get their web origin (CORS), SSO return URLs, web links and service URL
through environment overrides (`plan.mjs` `slotOverrides`); no settings file changes. Rebuild the slot web app after
a frontend change with `-Stop -Slot n` and a new start.

A start:

1. Runs `tools/devseed/New-LocalSecrets.ps1` when a local secrets file is missing or the API file lacks the
   attachment keys (an API file from before the Attachment service or before the dedicated `Codelab:Security:UploadTickets` ring). The script only adds missing keys, so existing
   passwords, sessions and PublicIds keep working; see [tools/devseed/README.md](../devseed/README.md).
   With the Attachment service, the start then **fails** when the service file's `Codelab:Security:Tickets` ring is
   not exactly the API's `Codelab:Security:UploadTickets` ring (compared by key id and key, never printed): a service
   file from before the dedicated ring holds the API's own SSO/activation ring and every upload would be refused
   with 401. Seeding cannot repair it (it never changes a value); delete the service file and start again, or run
   `New-LocalSecrets.ps1 -Force` (regenerates both files).
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
- A new .NET host: add it to `Services`, `LOG_FILES`, `Urls`, `Ports`, `Timeouts` and `Hosts`, place it in
  `START_ORDER`, add its decision (and flag) to `plan.mjs` and its slot overrides to `slotOverrides`; `run.mjs` builds,
  starts, records and health-checks every planned host.
- A setting that names a local URL (a new web link, origin or service URL): add its environment key to
  `SlotSettings` and set it in `slotOverrides`, or a slot stack points back at the default stack.

## Rules

- It never installs or deletes anything: no `npm install`, no database drop, no file removal except its own state
  file. A missing prerequisite is a warning or a refusal with the command to run. The only folders it creates are its
  state folders and a slot's attachment storage folder (`system.ensureDir`, idempotent).
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
- Slots share the browser's `localhost` cookies (cookies ignore the port): signing in to slot 1 in the same browser
  profile as slot 0 replaces that session. Use a separate profile or a Playwright context per stack.
- The local DevIdp registers only the slot-0 redirect URIs, so SSO sign-in works in slot 0 only; basic sign-in works
  in every slot.
- The first start of a slot builds everything into its own folder (a few minutes); its database is created and
  migrated on the first API start, empty except for the seeded data and the break-glass user.
- With `-NoAttachments` the API still runs, but attachment upload tickets, downloads and removals fail until the
  service runs.
