# tools/run-local

## Purpose

The logic behind `run-local.ps1` at the repo root (v1): one command that starts the local stack in Development,
proves it is healthy, and stops only what it started.

## Use

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1          # API + web (+ Seq when available)
powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1 -NoWeb   # the API alone
powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1 -Stop    # stop what a previous run started
```

A start:

1. Writes `backend/src/Starter.Api/appsettings.Development.local.json` with `tools/devseed/New-LocalSecrets.ps1`
   when it is missing (never overwrites).
2. Starts LocalDB (`sqllocaldb start MSSQLLocalDB`).
3. Uses Seq when it answers at `CODELAB_SEQ_URL` or `http://localhost:5341`, starts the `Seq` Windows service when
   it is installed but stopped, and otherwise warns and continues (`-NoSeq` skips this).
4. Builds the API with the private .NET SDK (`dotnet build backend/src/Starter.Api/Starter.Api.csproj`; a build
   error stops here), runs the built `Starter.Api.dll` detached from its project folder in Development on
   http://localhost:5180 (a detached `dotnet run` exits at once on Windows), and starts the web app with the private Node
   (`npm start --workspace frontend/starter-web`, http://localhost:4280).
5. Waits for `GET /health/ready` on the API and HTTP 200 on the web root, then prints the URLs, the logs and the
   break-glass user name.

Exit codes: 0 ok, 1 refused or failed, 2 usage. PIDs and logs live in `%TEMP%\codelab-starter`
(`run-local.json`, `api.log`, `web.log`). Tests: `node --test "tools/run-local/**/*.test.mjs"`.

## Extend

- Ports, paths, timeouts and names: `lib/constants.mjs`. Decisions (pure, unit-tested): `lib/plan.mjs`. The
  orchestration: `lib/run.mjs`, which reaches the machine only through the `system` object of `lib/system.mjs`, so
  `tests/run.test.mjs` drives it with a fake.
- A new service: add it to `Services` and `LOG_FILES`, start it in `start`, record its PID, health-check it.

## Rules

- It never installs or deletes anything: no `npm install`, no database drop, no file removal except its own state
  file. A missing prerequisite is a warning or a refusal with the command to run.
- `-Stop` kills only recorded PIDs, and only while each still runs the recorded image (`tasklist` check), so a reused
  PID is never killed. It kills the process tree (`taskkill /T`), because `dotnet run` and `npm` start children.
- Private toolchain only: `CODELAB_DOTNET` or `E:\codelab-tools\dotnet10\dotnet.exe`; `CODELAB_NODE_DIR` or
  `E:\codelab-tools\node24` first on `PATH`.

## Gotchas

- The first start builds the solution and the Angular bundle; the health waits allow 4 and 6 minutes.
- A port already in use is refused before anything starts; stop the other process first.
- Starting the `Seq` service may need an elevated shell; without it run-local continues without Seq.
- If a start fails, run-local stops what it started and prints the tail of the failing log.
