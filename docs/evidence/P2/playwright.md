# P2 Playwright check

Re-run on 2026-11-05 with Playwright MCP (`playwright-local`, Chromium) against the **unmodified repository tree**
(no scratch copy, no patches), after D1 to D6 were fixed. Viewports: desktop 1440x900 and mobile 390x844. Languages:
ar (default) and en. The 16 screenshots in this folder were replaced by this run and are linked from
[README.md](README.md). The first run (2026-11-04, on a scratch web copy carrying the D1 and D2 patches) is
superseded.

## Setup

| Item | What ran |
| --- | --- |
| Stack | `run-local.ps1 -NoSeq` from the repo root: the API (`backend/src/Starter.Api`, Development, request and response signing Enforce) on http://localhost:5180 and the repo web app (`frontend/starter-web`, `ng serve`) on http://localhost:4280. Both health checks passed. |
| SQL | `ConnectionStrings__Starter` pointed at the LocalDB instance `(localdb)\P2Proof`, database `StarterP2Proof` (see note N1 in [README.md](README.md)). |
| Accounts | `breakglass` and `dev.viewer`. The passwords were read at run time from `backend/src/Starter.Api/appsettings.Development.local.json` and never written to a file or to this folder. |
| Driver | One Playwright script: for each viewport and each language it signs in, checks the shell, opens `/does-not-exist`, signs out, signs in as `dev.viewer`, opens `/admin`, then signs out and replays the saved access token. In-app navigation goes through the router (no page reload), so the in-memory session signing key is kept. |
| Stop | `run-local.ps1 -Stop` (stopped only the API and web PIDs it recorded). |

## Results

All rows ran on the repo code. No screen scrolled horizontally.

| # | Scenario | ar desktop | ar mobile | en desktop | en mobile | Result |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Break-glass login, Arabic RTL shell | `/`, `dir=rtl lang=ar`, h1 "لوحة المعلومات" | same | | | PASS |
| 2 | Switch to English with the language switcher, LTR mirrored | | | `/`, `dir=ltr lang=en`, h1 "Dashboard" | same | PASS |
| 3 | `/does-not-exist` shows the 404 page | `dir=rtl` | `dir=rtl` | `dir=ltr` | `dir=ltr` | PASS |
| 4 | Dev user (holds only `dashboard.view`) opens `/admin` | redirected to `/unauthorized`, "تم رفض الوصول" | same | same, "Access denied" | same | PASS |
| 5 | Log out, then replay the saved access token on `GET /api/me/permissions` | 401 problem+json `UNAUTHENTICATED` | same | same | same | PASS |

Scenario 5 detail, the same on all four runs:

- Before logout, the unsigned replay of the live token answers `401 application/problem+json` code
  `signature_missing` (authentication succeeds, then the signature check refuses it).
- After logout, the same request answers `401 application/problem+json` code `UNAUTHENTICATED` with `correlationId`
  and `referenceCode`: the bearer handler refuses the revoked `sid` itself, and the challenge now has a problem+json
  body (D3 fixed; the first run saw an empty body here).

The 403 on the wire (`GET /api/access/admin` as the dev user answers `403 application/problem+json` `FORBIDDEN`) is
covered by `SessionFlowTests.TheDevUserGetsTheDashboardButAccessAdmin403ProblemJson` and the contract tests; this run
checked the client-side guard (scenario 4).

## Network trace of a login (repo web code)

```
GET  /api/features                       200 application/json
POST /api/auth/login                     200 application/json
GET  /api/security/session-signing-key   200 application/json   (D2 fixed: the app fetches the key)
GET  /api/me/permissions                 200 application/json   (signed)
POST /api/auth/logout                    204
GET  /api/me/permissions                 401 application/problem+json   (the replay)
```

Browser console: the expected 401s from the replays, and one 404 for `/favicon.ico` (the app ships no favicon).

## Not yet repeatable

This check was driven through the Playwright MCP; it is not a committed, scripted test. A repeatable end-to-end suite
for this path (login, RTL and LTR, 404, 403, logout replay, on two viewports) is an open item for the frontend owner.
