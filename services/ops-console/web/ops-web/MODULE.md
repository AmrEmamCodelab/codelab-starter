# Ops Console web (`services/ops-console/web/ops-web`)

## Purpose

The operators' Angular 22 app of the Ops Console: a small, separate app (not part of `frontend/starter-web`) that
talks only to the Ops Console API's BFF (`/ops-api/*`, port 5188 locally) on the same origin. Arabic-first, RTL,
`@codelab/ng-dga` components only. Four screens:

| Route | Screen | BFF call |
| --- | --- | --- |
| `overview` | Overall health and every check, worst first | `GET /ops-api/health` |
| `reference?code=ABCDE-12345` | Reference-code lookup: the request, its log events, open or copy the Seq link | `GET /ops-api/reference-codes/{code}` |
| `jobs` | Recurring jobs with last and next run, failed first | `GET /ops-api/jobs` |
| `certificates` | Certificate expiry with Ok / Warning / Critical / Expired badges | `GET /ops-api/certificates` |
| `sign-in-required`, `access-denied` | The 401 and 403 pages (outside the shell) | - |

The shell shows the operator (`GET /ops-api/me`) and a certificate alert banner whenever the IdP signing certificate
(config `idpCertificateName`), or any certificate when none is configured, is at Warning or worse.

| Folder | Element | What lives there |
| --- | --- | --- |
| `src/app/core/` | core | config.json loader and base-path check, `ApiService` (the only `HttpClient` user) and `OpsApiClient`, the 401/403 interceptor, i18n (language, translations, `t` and `dateTime` pipes), route constants, `RemoteResource`, `OperatorStore`, spec helpers in `core/testing` |
| `src/app/patterns/ops-list/` | patterns | the read-only list (the admin-list pattern's table half: caption, column defs, cell templates, loading/empty/error with reference code) |
| `src/app/shared/certificates/` | shared | `CertificatesStore` (one load for the screen and the banner), status views, the alert banner |
| `src/app/layouts/shell/` | layouts | the DGA header, operator name, language switch, banner, outlet |
| `src/app/features/` | features | `overview`, `reference`, `jobs`, `certificates`, `access` |
| `tools/apply-base-href.mjs` | tool | writes config.json `basePath` into index.html `<base href>` |

## Use

Prepend the private Node to `PATH` (`E:\codelab-tools\node24` on the build machine). From this folder:

```powershell
npx ng serve                      # http://localhost:4281, proxies /ops-api to http://localhost:5188 (proxy.conf.json)
npx vitest run                    # specs on jsdom (the starter web app's Vitest setup, extended)
npx ng build --output-path <dir>  # production build; one build serves every environment
npx eslint . ; npx stylelint "src/**/*.{css,scss}"
node tools/apply-base-href.mjs <deployed web folder>   # after changing basePath in the deployed config.json
```

Runtime configuration is `public/config.json` (deployed next to `index.html`, overwritten per environment):

| Key | Meaning |
| --- | --- |
| `basePath` | Where the app is mounted, ending in `/` (`/ops/` as a child app of the existing site). Must equal the served `<base href>`; start-up stops with `BasePathMismatchError` otherwise. |
| `apiBaseUrl` | The BFF root on the same origin (`/ops-api`). Absolute and `//host` values are refused. |
| `defaultLanguage` | `ar` or `en` for a first visit; the operator's choice is remembered in browser storage. |
| `idpCertificateName` | The `name` of the IdP signing certificate in `/ops-api/certificates`, or `null`. |

The JSON the screens expect is typed in `src/app/core/api/ops-api.models.ts` (enums travel as integers:
`OpsHealthStatus` 1-3, `JobRunStatus` 1-4 mirroring the API, `CertificateStatus` 1-4).

## Extend

- A new screen: a folder under `src/app/features/<name>/` with a store (provided by the page) over `RemoteResource`,
  a key-constants object, `ar` and `en` keys in `src/assets/i18n/`, a route in `app.routes.ts` with a `RoutePaths`
  constant, and an entry in `layouts/shell/shell-navigation.ts`. Add the key object to
  `src/app/translation-bundles.spec.ts`.
- A new BFF call: a path in `OpsApiPaths` and a method on `OpsApiClient`; never `HttpClient` elsewhere.
- Lists use `app-ops-list`; detail values use `dga-info-field` in the `ops-page__grid` layout (the staff-detail look).

## Rules

- HTTP only through `ApiService`; same-origin, `withCredentials`, no token and no Authorization header: the BFF sits
  behind Windows authentication and carries the ES256 service assertion to `/api/ops` itself.
- Only `@codelab/ng-dga` components and native semantic elements; design tokens and logical CSS only; no inline styles.
- Every string has `ar` and `en` keys (parity and usage are tested). Codes, IDs, paths and names render `dir="ltr"`.
- Enums are integers with explicit values; route, query, storage, config and translation keys come from constants.
- The lint configs are the starter web app's, reused by import (`eslint.config.mjs`, `stylelint.config.mjs`); this app
  never edits them.

## Gotchas

- Windows authentication through the dev proxy: `ng serve` proxies plain HTTP to 5188, so run the Ops Console API with
  its development authentication (or open the BFF once in the same browser) when testing locally.
- A changed `basePath` needs `tools/apply-base-href.mjs` (or the installer's equivalent) on the deployed folder; the
  `<base href>` is in the static `index.html` and the browser resolves every script and style against it before any
  code runs.
- The front site must route `/ops/*` (deep links) to `/ops/index.html`, and serve `config.json` and `assets/i18n/*.json`
  uncached.
- A 401 or 403 from any call moves to the access page; the shell's `/me` call is the usual trigger.
- `jsdom` renders the ng-dga icon glyphs as text; assert with `toContain` on elements that carry an icon.
