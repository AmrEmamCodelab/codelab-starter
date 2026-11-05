# Phase 2 evidence

Evidence for every Phase 2 exit criterion (build plan, "Phase 2: Walking skeleton"). Collected on 2026-11-04 by task
p2-int-proof against the uncommitted P2 working tree, before the integrator registered the backend projects in
`codelab-starter.slnx` and applied the `tools/build.ps1` requests. Every command ran with the private toolchain
(`E:\codelab-tools\dotnet10\dotnet.exe`, Node 24.22.2 from `E:\codelab-tools\node24`).

Status key: **PASS** (evidence green), **RED** (a defect blocks it), **PENDING** (not yet run).

**Re-run on 2026-11-05 (fixer round 1, area docs).** D1 to D6 are fixed in the tree. The Playwright check was re-run
on the unmodified repo (rows 2a to 2e, all 16 screenshots replaced), and `tools\build.ps1 -Target CI` was run on the
working tree (rows 1a, 3, 4a, 5 and the [CI step table](#ci-step-table)). The 2026-11-04 text below the exit-criteria
table is kept as history.

**Re-run on 2026-11-05 (integrator round 3).** `tools\build.ps1 -Target CI`: 18 of 18 steps PASS, "CI PASSED", exit 0;
step 6 ran `Starter.Integration.Tests` 26/26 (`CODELAB_TEST_SQL=(localdb)\P2Proof`). The Playwright check was re-run against
`run-local.ps1` (API :5180, web :4280, signing Enforce; `ConnectionStrings__Starter` pointed at `(localdb)\P2Proof`):
all 5 scenarios pass on both viewports and both languages, and all 16 screenshots were replaced.

## Exit criteria

| # | Exit criterion | Evidence | Status |
| --- | --- | --- | --- |
| 1a | CI green on all jobs, including the integration tier | [CI step table](#ci-step-table): **18 of 18 steps PASS** on 2026-11-05 (integrator round 2), "CI PASSED", exit 0; step 6 ran `Starter.Integration.Tests` **26/26** on LocalDB | PASS |
| 1b | Integration tier runs against a throwaway SQL Server | `Starter.Integration.Tests`: **22/22 passed** (see [test results](#test-results)); each fixture created and dropped its own `StarterIt_<guid>` | PASS (on `(localdb)\P2Proof`, see note N1) |
| 1c | `Program.cs` has at most 150 lines, asserted by an architecture test | `backend/src/Starter.Api/Program.cs` has **33 lines**; `ProgramTests.ProgramCs_HasAtMost150Lines` passed | PASS |
| 2a | Playwright: break-glass login shows the Arabic RTL shell | [playwright.md](playwright.md) scenario 1; [desktop](01-login-shell-ar-desktop.png), [mobile](01-login-shell-ar-mobile.png) | PASS (repo tree, unpatched, 2026-11-05) |
| 2b | Playwright: switch to English (LTR, mirrored) | scenario 2; [desktop](02-switch-language-en-desktop.png), [mobile](02-switch-language-en-mobile.png) | PASS (repo tree) |
| 2c | Playwright: `/does-not-exist` gives 404 | scenario 3; [ar desktop](03-not-found-ar-desktop.png), [ar mobile](03-not-found-ar-mobile.png), [en desktop](03-not-found-en-desktop.png), [en mobile](03-not-found-en-mobile.png) | PASS (repo tree) |
| 2d | Playwright: a gated route without permission gives 403 | scenario 4; [ar desktop](04-forbidden-ar-desktop.png), [ar mobile](04-forbidden-ar-mobile.png), [en desktop](04-forbidden-en-desktop.png), [en mobile](04-forbidden-en-mobile.png); redirected to `/unauthorized` on all four; the wire 403 problem+json `FORBIDDEN` is covered by `SessionFlowTests.TheDevUserGetsTheDashboardButAccessAdmin403ProblemJson` | PASS (repo tree) |
| 2e | Playwright: logout, then a replayed token for the logged-out `sid` gets 401 | scenario 5; [ar desktop](05-logout-replay-ar-desktop.png), [ar mobile](05-logout-replay-ar-mobile.png), [en desktop](05-logout-replay-en-desktop.png), [en mobile](05-logout-replay-en-mobile.png); the replay answers 401 problem+json `UNAUTHENTICATED` on all four | PASS (repo tree; D3 fixed) |
| 3 | Contract tests green: permission, PublicId, validator and problem+json | CI step 6 on 2026-11-05: `Starter.Contract.Tests` **29/29** (incl. `AllErrorsAreProblemJson` and `AProtectedEndpointWithoutCredentialsAnswers401ProblemJson`); the logout replay in [playwright.md](playwright.md) answers 401 problem+json on the wire | PASS (D3 fixed) |
| 4a | Rules dogfood: `/new-module Demo` then `/new-endpoint` passes architecture and contract tests with no manual edits | CI step 18 on 2026-11-05: the fork-model test scaffolds Demo with the generator, then the feed-mode check runs every suite: `Starter.Modules.Demo.Tests` 5/5, `Starter.Architecture.Tests` **35/35**, `Starter.Contract.Tests` **29/29** | PASS (D3, D4 fixed) |
| 4b | An edit adding `HttpClient` to a feature component is refused by the hook | `.claude/hooks/tests/fe-dogfood.test.mjs` **6/6** (Write and Edit with HttpClient refused with `[base.fe-http-via-api-service]`, fetch refused, shared/ import allowed); all hook tests **73/73** | PASS |
| 4c | CI ESLint also fails it, and a feature importing from `shared/` passes lint | `frontend/starter-web/tools/lint-fixtures.spec.ts` **10/10** (HttpClient and fetch in a feature fail, a feature importing shared, core and `@codelab/ng-dga` passes); `eslint .` on the app exits 0 | PASS |
| 5 | Fork-model test | [fork-model.md](fork-model.md), run 3 (CI step 18 on the real repo, 2026-11-05): init, one expected conflict (`CONTEXT.md`) only, removed-area drops, no `packages/`, Codelab.* and `@codelab/*` from the `-ci.1` feed it packed, fork `dotnet test` all green, frontend build green, frontend test 327/327 | PASS (D1, D3 to D7 fixed) |
| 6 | ScrubSweep zero hits | 2026-11-05 CI step 1 (integrator round 2): 1554 files, **clean**; the secret scan is clean too | PASS |
| 7 | Drift check green for `npm run api:gen` and the generated docs | `dotnet run --project backend/tools/Starter.DocsGen -- --out docs/generated --check`: "OK (7 generated file(s) up to date)", exit 0; `npm run api:gen:check --workspace frontend/starter-web`: exit 0 | PASS |

## Test results

Current counts are in the [CI step table](#ci-step-table) (step 6). The table below is the 2026-11-04 run, before
D3 and D4 were fixed.

Run by p2-int-proof with `dotnet test <csproj>` and `CODELAB_TEST_SQL=(localdb)\P2Proof`:

| Project | Total | Passed | Failed | Failing tests |
| --- | --- | --- | --- | --- |
| `backend/tests/Starter.Architecture.Tests` | 29 | 28 | 1 | `ModuleBoundaryTests.ModulesAndKernelRespectMayDependOnAndMustNotDependOn` (D4) |
| `backend/tests/Starter.Contract.Tests` | 24 | 22 | 2 | `ProblemJsonContractTests.AllErrorsAreProblemJson`, `ProblemJsonContractTests.AProtectedEndpointWithoutCredentialsAnswers401ProblemJson` (D3) |
| `backend/tests/Starter.Integration.Tests` | 22 | 22 | 0 | none |

Integration tests that passed include `SessionFlowTests.BreakGlassLogsInAndOpensTheDashboard`,
`SessionFlowTests.TheDevUserGetsTheDashboardButAccessAdmin403ProblemJson`,
`SessionFlowTests.LogoutAnswers204AndTheReplayedAccessTokenGets401`,
`SessionFlowTests.RefreshRotatesTheTokenAndRefusesTheOldOne`, `SessionFlowTests.AWrongPasswordIsARefusalProblemJson`,
`SessionFlowTests.BreakGlassHoldsEveryCatalogPermission`, `ProductionSeedingTests.TheDevUserIsAbsentInProduction`,
`ProductionSeedingTests.TheBreakGlassAccountIsSeededInProduction`,
`HostAndDatabaseTests.EveryModuleDbContextIsMigratedIntoItsOwnSchema`,
`HostAndDatabaseTests.FeaturesShowFlagsOnlyAndNoIdentities` and the three `TheSecurityStoreTablesExist` rows.
After the run no `StarterIt_*` database was left on the instance.

Other suites reported by the builder tasks (not re-run here): Starter.Application.Tests 53, Starter.Api.Tests 85,
Identity 78, Access 47, DocsGen 94, frontend `npm test` 279, `@codelab/ng-dga` 140, scaffold 51, run-local 21.

## Defects found

**Status on 2026-11-05: D1 to D7 are fixed in the tree** (verified by the Playwright re-run and by CI steps 6 and 18). The
text below records them as found on 2026-11-04.

Each one blocks or weakens an exit criterion. The patches are proposals for the owning task; p2-int-proof edited none
of these files.

**D1 (p2-fe-core), `frontend/starter-web/angular.json`: `ng build` and `ng serve` fail** with "The
../../node_modules/@codelab/ng-dga/assets asset path must be within the workspace root". `run-local.ps1` therefore
cannot start the web app. Patch, in `projects.starter-web.architect.build.options`:

```json
"preserveSymlinks": true,
"assets": [
  { "glob": "**/*", "input": "public" },
  { "glob": "**/*", "input": "src/assets", "output": "assets" }
],
```

That removes the `../../node_modules/@codelab/ng-dga/assets` entry; the fonts and icon font are still bundled through
the package's `@font-face` url rebasing. If the licence texts must also ship, add a prebuild copy step instead of the
asset entry. Verified: with this patch the scratch copy builds and serves.

**D2 (p2-fe-core), the app never obtains the session signing key.** The login response carries no `signing` block
(Codelab.Security issues the key from `GET /api/security/session-signing-key`), so `SessionKeyStore` stays empty, every
signed call after login answers 401 `signature_missing`, and the session ends at once (`/login?reason=expired`). Patch:

- `src/app/core/api/clients/api-paths.ts`, after `AuthLogout`:
  ``SessionSigningKey: `${API_PREFIX}/security/session-signing-key`,``
- `src/app/core/api/clients/auth.client.ts`: add `import type { SigningSessionBlock } from '@codelab/ng-security';` and

  ```ts
  signingKey(): Observable<SigningSessionBlock> {
    return this.api.get<SigningSessionBlock>(ApiPaths.SessionSigningKey, { context: selfHandledContext() });
  }
  ```

- `src/app/core/auth/auth.service.ts`, `adoptSession` becomes:

  ```ts
  private async adoptSession(response: SessionResponse): Promise<void> {
    this.tokens.save(response);
    if (this.signingKeys) {
      const block = response.signing ?? (await firstValueFrom(this.client.signingKey()));
      await this.signingKeys.adopt(block);
    }
    this.scheduleRefresh();
  }
  ```

  The tokens are saved first because the key endpoint needs the bearer token. Add specs for both paths (block in the
  response, block fetched). Verified end to end with signing Enforce: login, key fetch, signed `/api/me/permissions`
  200, signed `/api/access/admin` 403, and response verification all work. If signing is Off the endpoint answers 404;
  the owner should decide whether to skip adoption on 404.

**D3 (p2-be-identity), 401 and the bearer challenge have no problem+json body.** Protected MVC endpoints without
credentials, and a revoked token (logout replay), answer 401 with an empty body. This is the contract-test failure
above. Patch in `backend/src/Modules/Identity/Infrastructure/Security/BearerAuthentication.cs` (`Configure`), as
p2-be-guard-tests proposed:

```csharp
options.Events.OnChallenge = static async context =>
{
    context.HandleResponse();
    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
    await StatusCodePagesWriter.WriteAsync(new StatusCodeContext(context.HttpContext, new StatusCodePagesOptions(), static _ => Task.CompletedTask));
};
```

The 403 path is already problem+json on the wire (`FORBIDDEN`, seen in scenario 4), so `OnForbidden` needs no change.

**D4 (p2-be-identity), `backend/src/Modules/Identity/module.rules.yaml`:** `mayDependOn` lacks the kernel. Patch:
`mayDependOn: [StarterDomain, StarterApplication, StarterInfrastructure, CodelabSecurity, CodelabErrors]`, then
`node tools/rules-engine/compile.mjs`.

**D5 (shared `codelab-starter.slnx`, integrator):** the backend projects are not registered, so the fork-model test on the
real repo cannot build or test the backend in the fork. See [fork-model.md](fork-model.md).

**D6 (fork-model test harness) and D7 (`tools/build.ps1 -Pack` feed-mode check):** described with patches in
[fork-model.md](fork-model.md#defects-specific-to-the-fork-model).

## Notes

- **N1.** `(localdb)\MSSQLLocalDB` on this machine reports Running but accepts no connections (named-pipe error 121,
  also from `sqlcmd`). p2-int-proof did not restart it (other tasks may use it). It created its own instance
  `(localdb)\P2Proof` and used it for the integration tests, the fork-model run and the Playwright API. To reuse it:
  `$env:CODELAB_TEST_SQL='(localdb)\P2Proof'`. The owner can delete it with `sqllocaldb stop P2Proof` and
  `sqllocaldb delete P2Proof`.
- **N2.** The Playwright MCP writes a console log under the MCP's own workspace root (outside this repo). The one file
  this check caused there was removed afterwards; no screenshot or snapshot was written outside this folder.

## CI step table

`powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Target CI`, run on 2026-11-05 by the integrator
(round 2) on the uncommitted working tree with the private toolchain and `CODELAB_TEST_SQL=(localdb)\P2Proof`.
Result: "CI PASSED", exit 0.

| Step | Name | Result | Notes |
| --- | --- | --- | --- |
| 1 | scrub sweep | PASS | 1554 files, clean |
| 2 | secret scan | PASS | 1554 files, clean |
| 3 | rules check | PASS |  |
| 4 | rules tests | PASS | 245/245 |
| 5 | hook tests | PASS | 75/75 (incl. protected-path parity and the fe-dogfood tests) |
| 6 | dotnet build+test | PASS | build -warnaserror; Contract 29/29, Architecture 29/29, Integration 26/26, Identity 88, Access 48, Api 85, Application 53, DocsGen 94, DocsGate 76, Security 417 and the other package suites |
| 7 | docs gate | PASS |  |
| 8 | conflict markers | PASS |  |
| 9 | content rules | PASS | 0 errors, 0 warnings |
| 10 | scanner tests | PASS | incl. fork, scaffold (with the dogfood run), devseed and run-local tests |
| 11 | npm build+lint+test | PASS | incl. ng-dga and starter-web |
| 12 | vectors verify | PASS |  |
| 13 | package integration test | PASS |  |
| 14 | handover offline | PASS |  |
| 15 | provenance origins | PASS |  |
| 16 | generated drift | PASS | DocsGen --check and api:gen:check |
| 17 | frontend lint | PASS | ESLint, Stylelint, i18n parity, inline styles |
| 18 | fork-model test | PASS | see [fork-model.md](fork-model.md) |
