# Phase 3 evidence

**Exit scenarios 2 to 4 were re-run in ar and en at desktop and mobile:** see [playwright.md](playwright.md) for the
setup (stack, `Auth:Modes`, Dev IdP, database), the results per language and viewport, and how to rerun the
driver in [driver/](driver/). That run superseded the round 1 shots of rows 2 to 4 below; every `02*`, `03a`,
`03b` and `04*` screenshot now exists as `-{ar,en}-{desktop,mobile}.png`. `04c-devidp-picker-desktop.png` is the
old round 1 name of `04c-devidp-picker-en-desktop.png` and can be removed.

The rest of this page is the round 1 record (rows 1a, 1b and the static admin screens are still current).

Evidence for the Phase 3 Playwright exit criteria (build plan, "Phase 3: Identity and access complete"). Collected on
2026-11-05 by the phase 3 integrator (round 1) against the uncommitted P3 working tree with Playwright MCP
(`playwright-local`) and the private toolchain (`E:\codelab-tools\dotnet10\dotnet.exe`, Node 24.22.2).

Stack: `run-local.ps1 -NoSeq` (API :5180, web :4280, request and response signing Enforce) with
`ConnectionStrings__Starter` pointed at a throwaway database `StarterP3Evidence` on `(localdb)\P2Proof` (dropped
afterwards; `(localdb)\MSSQLLocalDB` was not started), and `Starter.DevIdp` on :5190. For the three-mode runs the API
got `Auth__Modes=basic,ad,sso` plus placeholder `Codelab__Identity__ActiveDirectory__{Host,Domain,BaseDn}` values
(the AD provider only has to boot; there is no directory on this machine).

| # | Exit criterion | Result | Screenshots |
| --- | --- | --- | --- |
| 1a | `Auth:Modes=[basic,ad,sso]`: the login chooser shows three methods | PASS (3 tabs: user name, organization account, single sign-on) | `01-login-chooser-three-modes-{ar,en}-{desktop,mobile}.png` |
| 1b | No `Auth:Modes` (basic default): only the basic form, no chooser | PASS (0 tabs, 2 text boxes) | `05-login-basic-only-{ar,en}-{desktop,mobile}.png` |
| 2 | Break-glass creates a role in the tree editor and grants it to `dev.viewer`; in a second browser context that user's menu gains "Administration" with no relogin; revoking removes it and the route answers 403 | PASS (menu `Dashboard` -> `Dashboard, Administration` on the live permission sync; `/admin/users` opened; after revoke the same session lands on `/unauthorized` and the menu is `Dashboard` again) | `02a` to `02i`, each `-{ar,en}-{desktop,mobile}`; re-run in [playwright.md](playwright.md) |
| 3 | Excel import of the `tools/devseed/user-import` sample: preview 3 rows, 1 rejected with its reason, commit 2 accounts with no password, activation links shown, 2 Pending outbox rows | PASS (row 4 "The e-mail address is not valid."; 2 links; SQL below) | `03a-import-preview-*`, `03b-import-commit-links-*` (ar/en, desktop/mobile), `15-import-*` |
| 4 | SSO login through the dev IdP, then logout through end_session back to `/login` | PASS (navigation trace below) | `04a` to `04e`, each `-{ar,en}-{desktop,mobile}` |
| - | Admin screens in ar and en at 1440x900 and 390x844 | captured | `10-admin-home-*`, `11-users-list-*`, `12-organizations-*`, `13-roles-list-*`, `14-role-editor-*`, `15-import-*` |

SQL after the import commit (`messaging.OutboxMessages`, Status 1 = Pending; `identity.UserAccounts`):

```text
Id Type               Status Payload (start)
1  account-activation 1      {"userPublicId":"m1_...","ticketId":"...","expir...
2  account-activation 1      {"userPublicId":"m1_...","ticketId":"...","expir...
sample.one.eca3 no password Binding 1
sample.two.eca3 no password Binding 1
```

SSO navigation trace (secrets elided): `GET /login` -> `GET :5180/api/auth/sso/login` -> `GET :5190/authorize?response_type=code&client_id=starter-web&...&code_challenge_method=S256`
-> `POST :5190/authorize` -> `GET :5180/api/auth/sso/signin-oidc?code=…&state=…` -> `GET :4280/auth/sso/callback` (ticket in
the fragment) -> dashboard -> Sign out -> `POST /api/auth/sso/logout` 200 -> `GET :5190/end-session?id_token_hint=…&client_id=starter-web&post_logout_redirect_uri=http://localhost:4280/login`
-> `GET :4280/login`. The SSO account `dev.admin` (binding SSO, login name `dev.admin`) was created in the users screen
(`04a`) and given `dashboard.view` so the signed-in dashboard renders.

Defect found and fixed during this run: the import preview (`POST /api/admin/user-import/preview`, multipart) was
refused with `signature_invalid` because the SPA signs a `FormData` body as `UNSIGNED-BODY` and the endpoint was not
marked `[SignatureExempt(SignatureExemption.BodyOnly, ...)]`. The integration test host runs without signing, so the
suite did not catch it.

Cosmetic observations (not fixed): the third login tab label is clipped at 1440 px in English ("Single sign-o");
the preview summary reads "1 have errors".
