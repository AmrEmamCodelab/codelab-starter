# P3 Playwright check

Run on 2026-11-05 with Playwright MCP (`playwright-local`, Chromium) against the **uncommitted Phase 3 working tree**
(on top of `c02a3c6`, the Phase 2 commit), no scratch copy and no patches. The other round 1 fixers were still
editing other areas while it ran, so it proves the tree as it stood at about 07:40 local time; rerun it after the
last fix (see [Rerun](#rerun)). Viewports: desktop 1440x900 and mobile 390x844. Languages: ar (default) and en. It
replaced the earlier en-desktop-only shots of scenarios 2 to 4, and added the ar and mobile sets. The screenshots
are listed in [README.md](README.md).

## Round 2 rerun (integrator, after the round 1 fixes)

Rerun on 2026-11-05 after every round 1 fix had landed, including the SSO browser binding (the SPA now sends
`binding` to `api/auth/sso/login` and `bindingProof` to the exchange) and the permission gates on the dialog hosts.
Same setup as below, on a new database `StarterP3EvidenceR2` on `(localdb)\P2Proof` (dropped afterwards), with
`Auth__Modes=basic,sso`, run tags ed3, em3, ad3 and am3, and workbooks made with `-Suffix r2<tag>`. Every screenshot of
scenarios 2 to 4 was overwritten by this run.

| Run | Role, grant, live menu, revoke, 403 | Import 3 rows, 1 rejected, 2 links | SSO round trip and end_session logout | Horizontal scroll |
| --- | --- | --- | --- | --- |
| en desktop | pass (menu after about 19 s, gone about 25 s after revoke, users call 403, /unauthorized) | pass (row 4: invalid e-mail) | pass | none |
| en mobile | pass | pass | pass | none |
| ar desktop | pass | pass | pass (rtl) | none |
| ar mobile | pass | pass | pass (rtl) | none |

SSO trace (each run): `GET :5180/api/auth/sso/login` > `GET :5190/authorize` > `POST :5190/authorize` >
`GET :5180/api/auth/sso/signin-oidc` > `GET :4280/auth/sso/callback` > `POST :5180/api/auth/sso/logout` >
`GET :5190/end-session` > `GET :4280/login`.

SQL before the drop: 8 imported accounts (2 per run), all with `PasswordHash` NULL and an `ActivationTicketId`;
`messaging.OutboxMessages` holds 8 `account-activation` rows with Status 1 (Pending), none with a ticket in the
payload; no `sample.three.*` account exists.

## Setup

| Item | What ran |
| --- | --- |
| Stack | `run-local.ps1 -NoSeq` from the repo root: the API (`backend/src/Starter.Api`, Development, request and response signing Enforce) on http://localhost:5180 and the web app (`frontend/starter-web`, `ng serve`) on http://localhost:4280. Both health checks passed. |
| `Auth:Modes` | `Auth__Modes=basic,sso` in the environment of the shell that ran `run-local.ps1` (the scalar comma form, read by `AuthModesOptions.Bind`). `GET /api/auth/modes` answered `{"basic":true,"activeDirectory":false,"sso":true}`. AD was off: there is no directory on this machine, and the AD chooser case is row 1a of [README.md](README.md). |
| Dev IdP | `E:\codelab-tools\dotnet10\dotnet.exe run --project backend/tools/Starter.DevIdp` (launch profile: Development, http://localhost:5190), started before the stack. The API's `Codelab:Identity:Sso` values are the committed ones in `appsettings.Development.json`. |
| SQL | `ConnectionStrings__Starter` pointed at a new database `StarterP3EvidenceR1` on `(localdb)\P2Proof` (migrations applied at start, dropped after the run). `(localdb)\MSSQLLocalDB` was not started. |
| Accounts | `breakglass` and the seeded `dev.viewer` (holds only `dashboard.view`). The SSO account `dev.admin` (binding SSO, login name `dev.admin`, direct grant `dashboard.view`) was created once in the users screen before the four runs; the runs only open that dialog and cancel it (`04a`). |
| Passwords | Read at run time from `backend/src/Starter.Api/appsettings.Development.local.json` by [driver/secret-relay.mjs](driver/secret-relay.mjs) and served on 127.0.0.1 only. They are not in any file in this folder or in the call text. |
| Workbooks | One fresh 3-row workbook per run: `tools\devseed\user-import\New-SampleUserImport.ps1 -Path <temp>\sample-<tag>.xlsx -Suffix r1<tag>`, so a rerun is never "user name taken". |
| Driver | [driver/p3-exit-scenarios.js](driver/p3-exit-scenarios.js), one MCP call per language and viewport. Each call uses fresh browser contexts: an admin and a viewer side by side (scenario 2 and 3), then an SSO context (scenario 4). In-app navigation goes through the router, never a reload, so the in-memory session and signing key are kept. |
| Stop | `run-local.ps1 -Stop` (stopped only the API and web PIDs it recorded), then the Dev IdP and the relay by PID, then `DROP DATABASE StarterP3EvidenceR1`. |

## Results

All four runs passed every row. No screen scrolled horizontally (`scrollWidth > innerWidth` was false on every
screenshot).

| # | Exit criterion | ar desktop | ar mobile | en desktop | en mobile | Result |
| --- | --- | --- | --- | --- | --- | --- |
| 2a | The viewer's menu has no Administration entry | `لوحة المعلومات` only | same | `Dashboard` only | same | PASS |
| 2b | Break-glass creates a role in the tree editor (`access.admin.view` + `users.view`) and assigns it to `dev.viewer` | saved | saved | saved | saved | PASS |
| 2c | The viewer's menu gains Administration **without relogin** (the open session, no reload, no user action) | after 19.1 s | 19.1 s | 19.1 s | 19.1 s | PASS |
| 2d | The viewer opens Administration, then Users | `/admin/users` | same | same | same | PASS |
| 2e | Revoking the role removes the menu entry in the open session | after 25.1 s | 25.1 s | 25.1 s | 25.1 s | PASS |
| 2f | The open users screen's next API call answers 403 | `GET /api/admin/users` 403 | same | same | same | PASS |
| 2g | Opening `/admin` again lands on the access-denied page | `/unauthorized`, "تم رفض الوصول" | same | same, "Access denied" | same | PASS |
| 3a | Excel import preview: 3 rows, 1 rejected with its reason | row 4 "البريد الإلكتروني غير صحيح." | same | row 4 "The e-mail address is not valid." | same | PASS |
| 3b | Commit creates 2 accounts, each with an activation link shown to the admin | 2 links | 2 links | 2 links | 2 links | PASS |
| 3c | The 2 accounts have no password, a ticket, and a Pending outbox row (SQL below) | yes | yes | yes | yes | PASS |
| 4a | SSO login through the dev IdP lands on the dashboard | `dir=rtl` | `dir=rtl` | `dir=ltr` | `dir=ltr` | PASS |
| 4b | Sign out goes through end_session back to `/login` (trace below) | yes | yes | yes | yes | PASS |

The live-update times are the app's background sync (`config.json` `permissionRefreshSeconds`), measured from the
admin's save to the viewer's repaint; the driver waits up to 90 s.

SQL after the four runs (`[identity].UserAccounts`, `messaging.OutboxMessages`, Status 1 = Pending, Binding 1 =
password). The 20 outbox rows are the 20 accounts imported on this database by the four recorded runs and by six
driver attempts before them; every one is Pending:

```text
UserName          Pw           Binding Ticket
sample.one.r1ed6  no password  1       ticket
sample.two.r1ed6  no password  1       ticket
sample.one.r1em2  no password  1       ticket
sample.two.r1em2  no password  1       ticket
sample.one.r1ad2  no password  1       ticket
sample.two.r1ad2  no password  1       ticket
sample.one.r1am2  no password  1       ticket
sample.two.r1am2  no password  1       ticket

Type               Status N
account-activation 1      20     (20 sample accounts in total; no sample.three.* account exists)
```

SSO navigation trace, the same on all four runs (top-level navigations and the logout call; query strings dropped):

```text
GET  :5180/api/auth/sso/login > GET :5190/authorize > POST :5190/authorize > GET :5180/api/auth/sso/signin-oidc
> GET :4280/auth/sso/callback > POST :5180/api/auth/sso/logout > GET :5190/end-session > GET :4280/login
```

## Observations (not fixed here)

- After the revoke, the open users screen keeps showing the list it already loaded until it calls the API again;
  that call answers 403. The criterion ("the route returns 403") holds on the next call and on the next navigation.
- The Angular router ignores a navigation to the URL already open, so "open `/admin/users` again" from that same
  screen does nothing; the driver opens `/admin` instead.
- Import result at 390 px: the activation-link column is cut off inside the table (the table scrolls in its own box;
  the page does not scroll sideways).
- The file picker button is the browser's own control, so it reads "Choose File" in Arabic too.
- The New user dialog keeps the password hint ("A password account gets an activation link...") after Single sign-on
  is chosen.
- A browser tab left open across an API restart showed "Could not load login options" on `/login` until it was
  reloaded in a fresh context.

## Rerun

1. Start the Dev IdP and the stack as in [Setup](#setup), on a new database.
2. `node docs\evidence\P3\driver\secret-relay.mjs backend\src\Starter.Api\appsettings.Development.local.json`
   (private Node; it listens on 127.0.0.1:5399 and stops itself after 3 hours).
3. Make one workbook per run with `New-SampleUserImport.ps1`, and create `dev.admin` once (users screen, Single
   sign-on, login name `dev.admin`, direct permission "Open the dashboard").
4. For each language and viewport, call `browser_run_code_unsafe` with:

   ```js
   async (page) => eval(await (await page.request.get('http://127.0.0.1:5399/driver')).text())(page,
     { lang: 'ar', view: 'mobile', tag: 'am3', workbook: 'C:/path/to/sample-am3.xlsx' })
   ```

   The call returns a JSON result per row (`pass`, timings, menus, statuses, trace) and writes the screenshots here.
   `dev.viewer` must start with `dashboard.view` only: a role left assigned by an interrupted run makes rows 2a and
   2f fail, so clear it first.

## Not yet repeatable in CI

This is a Playwright MCP driver, not a committed `@playwright/test` suite. A CI-run end-to-end suite (with
`@playwright/test` in the web app's dev dependencies and a config that starts the stack and the Dev IdP) is an open
item for the frontend owner.
