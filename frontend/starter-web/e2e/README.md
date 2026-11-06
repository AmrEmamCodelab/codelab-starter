# Visual and accessibility harness (`frontend/starter-web/e2e`)

## Purpose

One command that proves a page looks right and is accessible in both languages and at both sizes. It opens each
route in the installed Microsoft Edge (`playwright-core`, channel `msedge`; nothing is downloaded), seeds the app
language, takes a full-page screenshot in `ar` and `en` at 1440 and 390 px, and:

| Check | Pass rule |
| --- | --- |
| Baseline mode (default) | at most **0.1 %** of pixels differ from the stored baseline (`pixelmatch` tolerance 0.1, anti-aliasing ignored) |
| Target mode (`--target`) | at most **5 %** of pixels differ from a design target image (the design-agent loop) |
| Language | `<html lang>` is the requested language (a screenshot in the wrong language proves nothing) |
| Accessibility | `axe-core` (WCAG 2.0/2.1 A and AA) finds **0 serious and 0 critical** violations |

Regions marked `data-visual-mask` (clocks, counters, generated numbers) are painted over in both images before
comparing. Each run writes `actual/`, `diff/`, `visual-report.json` and `visual-report.md` to `--out-dir`.

## Use

Start the app (`run-local.ps1`, web on `http://localhost:4280`), prepend the private Node to `PATH`, then from
`frontend/starter-web`:

```powershell
# Store or refresh baselines (review every changed image before committing it)
node e2e/visual.mjs --base-url http://localhost:4280 --routes e2e/routes/p4-public.json --langs ar,en --widths 1440,390 `
  --baseline-dir ../../docs/evidence/P4/baselines --update --out-dir ../../docs/evidence/P4/visual
# Compare with the baselines (CI-style rerun)
node e2e/visual.mjs --base-url http://localhost:4280 --routes e2e/routes/p4-public.json --baseline-dir ../../docs/evidence/P4/baselines
# Compare with a design target; {route}, {lang} and {width} are replaced per screenshot
node e2e/visual.mjs --base-url http://localhost:4280 --routes '[{"name":"service-guide","path":"/portal/service-guide"}]' `
  --target design/service-guide.{lang}.{width}.png --iteration 2 --out-dir out/service-guide-2
# Every option
node e2e/visual.mjs --help
```

Exit codes: `0` pass, `1` a screenshot, the language check or the accessibility scan failed, `2` usage (bad
arguments or routes file), `3` environment (a runtime package missing, Edge not starting, app unreachable).

**Routes file.** An array of routes, or an object that adds file-wide masks and the language storage key:

```json
{
  "routes": [
    { "name": "portal-faq", "path": "/portal/faq", "waitFor": "main", "mask": [".last-updated"] }
  ],
  "mask": ["[data-clock]"],
  "languageStorageKey": "starter.language"
}
```

`name` (lower-case, digits, dashes) names the files: `<name>.<lang>.<width>.png`. `waitFor` waits for a visible
element before the screenshot. Ready-made files: `routes/login.json`, `routes/p4-public.json` (portal pages and the
showcase).

**Unit tests** (pure logic: arguments, routes, diff ratio, masking, verdict, report):
`npx vitest run --config e2e/vitest.config.mjs`.

## Scripted user flows (`flows.mjs`)

The phase-5, phase-6 and phase-7 exit criteria as runnable scripts, one per flow, in the same installed Edge (no download). Each step is
logged with its result and a screenshot; the run writes `flows-report.json`, `flows-report.md` and `screenshots/` to
`--out-dir`, so the report is the phase evidence.

| Flow (`--flows`) | What it proves |
| --- | --- |
| `settings-jobs` | The admin edits `notifications.page-size`, runs `outbox-drain` with "Run now", and the run history shows a succeeded run by the system actor. The setting is reset to its default at the end. |
| `notification-bell` | The admin sends a test notification from the notifications page; the bell shows the template title in en and in ar, and its e-mail lands in the pickup folder. |
| `activation` | An Excel-imported user (a generated `.xlsx`) gets the activation e-mail, opens the link, sets a password and signs in; the reused link is refused. |
| `contact-faq` | A contact message returns a `CU-YYYY-NNN` number of this year and the contact inbox gets the e-mail; an admin edit of the first FAQ title shows on `/portal/faq` in en and ar. The titles are put back at the end. |
| `attachments` | Phase 6: on `/attachments/demo` the admin uploads a PDF (the browser posts it to the attachment service with the upload-ticket header, across origins; the API links the receipt), sees it listed after a reload, downloads identical bytes, and a file of `MaxFileBytes + 1` is refused with `errors.ATTACHMENTS_FILE_TOO_LARGE` in en and in ar plus a reference code (row and banner). The uploaded PDF is removed at the end. |
| `service-requests` | Phase 7, steps 1-8, run four times (ar and en, at 1440 and at 390 px): the requester fills the 3-step wizard with a PDF, saves the draft, resumes it from My requests and submits it (`SR-YYYY-NNN`; `SR-2026-001` on a fresh database); the reviewer claims it in the queue and starts the review (SLA badge shown); the requester sees the timeline update and a new notification; the return dialog blocks an empty reason; the reviewer returns it with a reason; the requester sees the reason and resubmits; the reviewer approves and no action is left for anyone; the timeline covers submit, review, return, resubmit, review, approve with at least two actors; the admin oversight shows it read-only; a second request is rejected with a reason and the requester has no resubmit. Step 9 (an SLA breach raises an escalation) needs an advanced `TimeProvider` and is proven in the test host (`Starter.Integration.Tests` `SlaBreachEscalationTests`). |

```powershell
# App running (run-local.ps1); private Node first on PATH; from frontend/starter-web
node e2e/flows.mjs --base-url http://localhost:4280 --out-dir ../../docs/evidence/P5/flows
node e2e/flows.mjs --base-url http://localhost:4280 --flows activation,contact-faq --headed
node e2e/flows.mjs --base-url http://localhost:4280 --flows attachments --out-dir ../../docs/evidence/P6/flows
# Phase 7 against a reviewer slot (run-local.ps1 -Slot 1: web :4380, its own database)
node e2e/flows.mjs --base-url http://localhost:4380 --flows service-requests --out-dir ../../docs/evidence/P7/ui/service-requests
node e2e/flows.mjs --help
```

- **Sign-in:** the break-glass user from the local secrets file (`--secrets-file`, default the API's
  `appsettings.Development.local.json`), or `STARTER_E2E_USER` / `STARTER_E2E_PASSWORD`. The password, the activation
  link and the new user's password are redacted from every step detail.
- **E-mail:** the API's `FileDrop` mode writes `.eml` files to `%TEMP%\starter-mail` (`--mail-dir` for another
  `Notifications:Email:PickupDirectory`). This folder is the local SMTP sink. When the mail is not there yet, the flow
  runs `outbox-drain` from the jobs screen instead of waiting for the 5-minute schedule.
- **Service Request accounts:** the requester is the first Development user (`Identity:DevUsers:0` of the secrets
  file, `dev.viewer`); the flow grants it `service-requests.create`, `service-requests.view-own`, `attachments.view`
  and `attachments.upload` on its access page for the run and takes back exactly the keys it added. The reviewer and
  the admin oversight are the break-glass user: a separate account, because the engine refuses the staff rules to the
  record's owner. Each variant files two requests and leaves them (terminal) in the database.
- **Exit codes:** `0` all flows passed, `1` a step failed, `2` usage, `3` environment (no credentials, Edge not starting,
  app unreachable).
- **Pure parts** (arguments, credentials, step log, `.eml` parsing, `.xlsx` building, reference numbers and run data)
  are unit-tested under `tests/`. `lib/flow-runtime.mjs` and `flows/*.mjs` are the only files that drive the browser.

## Extend

- **A new flow** goes in `flows/<name>.mjs` and exports `run<Name>({ recorder, credentials, options })`. Add its name
  to `FlowNames` and its runner to `FLOW_RUNNERS` in `flows.mjs`. Put each selector, key and expected text in
  `lib/flow-constants.mjs`, prefer `data-testid` hooks, and undo every change with `recorder.cleanup(...)`.
- **A new check** goes in a pure module under `lib/` with its test under `tests/`, and is called from `judge.mjs`
  (per screenshot) or `report.mjs` (per run). `runtime.mjs` stays the only file that touches the browser, the
  packages and PNG bytes.
- **A new default** (threshold, width, mask, storage key) is a constant in `lib/constants.mjs`; nothing else writes a
  bare value.
- **A new language** is added to `SUPPORTED_LANGUAGES` and `BROWSER_LOCALES` together with the app's own language list.

## Rules

- Baselines are reviewed like code: look at every image `--update` wrote before committing it, and never refresh
  baselines to make a failing comparison pass without understanding the change.
- Mask only content that changes between runs. Masking a region to hide a layout difference defeats the check.
- No browser download and no network: the harness drives the installed Edge and loads `axe-core` from
  `node_modules` into the page.
- The thresholds are the phase exit criteria (0.1 % baseline, 5 % design target, 0 serious or critical). Change
  them only with the platform owner.

## Gotchas

- `playwright-core`, `pixelmatch`, `pngjs` and `axe-core` are root devDependencies; without `npm install` at the
  repository root the run stops with exit 3 and says so.
- The language is seeded into `localStorage` (`starter.language`) before the app boots. A route that forces its own
  language, or a fork that renames the key, fails the language check: pass `--language-storage-key` or set
  `languageStorageKey` in the routes file.
- Screenshots are full page: a page that grows by a few rows shows up as padding difference at the bottom (reported
  with a size note), not as a shifted image.
- Fonts render slightly differently between machines and Edge versions. Compare baselines on the machine (or the
  build agent image) that recorded them; re-record after an Edge or font update.
- Flows change real data in the local database: each run imports one new `e2e-<token>` user and files one contact
  message. They do not delete these records. Run the flows against a local or test database only.
- `notification-bell` needs a "send test notification" control on the notifications page
  (`data-testid="notifications-send-test"`, permission `notifications.test`). Until that control exists, the flow
  fails at that step and names the missing selector.
- `attachments` needs the attachment service (`run-local.ps1` without `-NoAttachments`) and a development build
  (the demo page matches only there). Its oversized size comes from `ATTACHMENT_MAX_FILE_BYTES` in
  `lib/flow-constants.mjs`, which a unit test keeps equal to the service's `Codelab:Security:Uploads:MaxFileBytes` in
  `appsettings.json`; a machine that overrides the limit locally must pass the same value there. The demo type allows
  five files: a run that stops before its clean-up step leaves its PDF, so remove leftovers on the demo page.
- The axe scan runs after the screenshot in the same page, so injected axe styles never appear in the image.
- Showcase routes (`docs/evidence/P4/showcase/routes.json`, `/showcase` in `e2e/routes/p4-public.json`) render only
  against a development build (`npm start` / `run-local.ps1` on :4280) or a staging config with `"showcase": true`.
  Against a production build with the shipped `config.json` they capture the 404 page, so never run `--update` from
  such a build.
