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

## Extend

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
- The axe scan runs after the screenshot in the same page, so injected axe styles never appear in the image.
- Showcase routes (`docs/evidence/P4/showcase/routes.json`, `/showcase` in `e2e/routes/p4-public.json`) render only
  against a development build (`npm start` / `run-local.ps1` on :4280) or a staging config with `"showcase": true`.
  Against a production build with the shipped `config.json` they capture the 404 page, so never run `--update` from
  such a build.
