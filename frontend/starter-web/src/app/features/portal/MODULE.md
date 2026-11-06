# Public portal (`features/portal`)

## Purpose

Module 15's user interface: the public pages every visitor can open without signing in, Arabic first with English.
Since P5 the FAQ, service guide, privacy and terms pages read their sections from `GET api/portal/content/{page}`
(bilingual plain text an administrator edits) and render the active language; the page chrome (titles, search, help
blocks) stays in the i18n bundles. The contact form posts to `POST api/portal/contact-messages` and shows the
`CU-YYYY-NNN` reference number of the 201 receipt. The shipped `public/config.json` sets
`portalContactForm: true` (the parser default stays false for a config without the key, and then the page shows the
ways to reach the team and a "form coming soon" notice); whether submissions are accepted is the backend runtime
setting `portal.contact-enabled` (off: 409 `PORTAL_CONTACT_DISABLED`). The landing page keeps its static highlights.

| Piece | What it does | Files |
| --- | --- | --- |
| `PORTAL_ROUTES`, `PORTAL_PUBLIC_PATHS` | The six pages under `/portal` (children of the portal shell, paths from core `PortalPaths`), and their full paths for the app's `PUBLIC_ROUTE_PATHS`. `PortalRouteDataKeys.Document` passes a legal document to the legal page | `portal.routes.ts` |
| `PortalLinks`, `portalBreadcrumbs()` | Absolute links of the portal pages (through the layouts' `portalLink`), and the home-to-page breadcrumb trail every page header shows | `portal-links.ts` |
| `PortalLandingPageComponent` | `/portal`: on the landing dashboard pattern. A brand `dga-hero` with the page `h1` and two links, a `dga-hero-slider` of highlights (no autoplay), service cards (`dga-card`), public tiles derived from the content (guide steps, answered questions, languages) and quick links to the legal pages | `landing/` |
| Portal content | `PortalContentClient` (`GET api/portal/content/{page}`, anonymous and self-handled), `PortalContentPages`, `localizeSections()` (active language, `sortOrder`, paragraphs split on blank lines, anchor slugs `part-<n>`), `contentTocSections()`, `PortalContentStore` (per page: `status` `PortalContentStatus`, `sections`, `empty`, `load()`, `retry()`), `PortalContentStateComponent` (loading, empty and error `dga-page-feedback` plus a retry button) | `content/` |
| `ServiceGuidePageComponent` | `/portal/service-guide`: on the portal content pattern. The guide's sections from the API (heading and paragraphs each), a table of contents, then the static help block linking to the FAQ and contact pages | `service-guide/` |
| `FaqPageComponent` + `filterFaq()` | `/portal/faq`: on the portal content pattern, the questions from the API (one section per question: title = question, body = answer) in one `dga-accordion`. The search box (shown once questions are loaded) filters questions and answers as the reader types (every word must match; Arabic diacritics, tatweel, hamza, ta marbuta and alef maksura variants folded), opens the matches, announces the count and shows a no-results state | `faq/` |
| `ContactPageComponent` + `PortalContactStore` | `/portal/contact`: name, email, topic and message (`dga-text-input`, `dga-select`, `dga-textarea`). Client checks show once a field is left or after the first submit; the server's problem+json field errors show under their fields until edited; the outcome shows in `dga-page-feedback` (sent with the reference number, validation, rate-limited, closed by an administrator, offline, failed with the reference code). The store is provided per page | `contact/` |
| Contact rules | `contactFormErrors()`, `ContactLimits`, `ContactFields`, `ContactTopicIds` (lookup-style ids), `contactFailureFrom()` (`ContactFailureKind`, `PortalErrorCodes`), `successOutcome()`, `failureOutcome()`, `serverFieldMessageKey()` | `contact/contact-form.ts`, `contact/contact-outcome.ts` |
| `PortalContactClient`, `PortalApiPaths` | `POST api/portal/contact-messages` through core `ApiService`: anonymous (no bearer, unsigned) and self-handled (no app-wide error dialog) | `contact/portal-contact.client.ts` |
| `LegalPageComponent` | `/portal/privacy` and `/portal/terms`: one component, the document comes from route data (`PRIVACY_DOCUMENT`, `TERMS_DOCUMENT`: title and subtitle keys plus the content page), the API sections with their paragraphs, a table of contents and a closing contact link | `legal/` |
| Static lists | The landing page's slides, cards, quick actions and tiles; `FAQ_CATEGORIES` and `GUIDE_STEPS` remain only for the landing tiles' counts (the FAQ and guide pages read the API) | `*/*-content.ts` |
| Texts | One `*-translation-keys.ts` catalogue per page under `portal.<page>.*`, plus `portal.common.*`; `PORTAL_TRANSLATION_KEYS` lists them all | `*-translation-keys.ts`, `portal-translation-keys.ts` |

## Use

The app mounts the pages inside the public portal frame:

```ts
{ path: RoutePaths.Portal, component: PortalShellComponent, children: PORTAL_ROUTES }
```

and spreads `PORTAL_PUBLIC_PATHS` into `PUBLIC_ROUTE_PATHS` (`public-routes.ts`). Links to a portal page use
`PortalLinks` (or the layouts' `portalLink(PortalPaths.X)`), never a literal path. Nothing else imports this feature.

The content pages read `GET api/portal/content/{page}` (`faq`, `service-guide`, `privacy`, `terms`):
`{ page, updatedAt, sections: [{ id, titleAr, titleEn, bodyAr, bodyEn, sortOrder }] }`. Loading and an empty page show
an information banner; a failure (offline, 5xx, 404) shows an error banner with a retry; the page chrome always renders.

The contact endpoint's contract, as this page expects it:

| Answer | The page shows |
| --- | --- |
| 201 `{ referenceNumber }` (`CU-2026-001`; 200 or 204 read the same) | success banner (with the number when sent); the form is cleared |
| 400/422 problem+json with `errors` (`fullName`, `email`, `topicId`, `message`; any case) | each field's first translated message (`errors.<CODE>`, else "check this field") under it, and a validation banner |
| 409 `PORTAL_CONTACT_DISABLED` (an administrator turned `portal.contact-enabled` off) | an info banner (`ContactFailureKind.Closed`): the form is closed, try later; no reference code, since nothing is broken |
| 429 | the rate-limit banner; the form keeps its values |
| status 0 | the offline banner |
| 5xx (or anything else) | the failure banner, with the `referenceCode` when the server sent one |

The body is `{ fullName, email, topicId, message, language }` with trimmed values; `ContactLimits` is the client copy of
the server's limits. The page design references are [portal-content](../../../../../../docs/design/patterns/portal-content.md)
and [landing-dashboard](../../../../../../docs/design/patterns/landing-dashboard.md).

## Extend

- **Another FAQ question, guide section or legal clause:** an administrator adds it in the portal content admin
  screen (`PUT api/admin/portal/content/{page}`); no code change. Plain text only, paragraphs separated by a blank line.
- **Another portal page:** a path in core `PortalPaths`, a route in `PORTAL_ROUTES` (its full path then lands in
  `PORTAL_PUBLIC_PATHS` by itself), a link in `PortalLinks`, a translation-key catalogue added to
  `PORTAL_TRANSLATION_KEYS`, and the page built on the portal content pattern.
- **API content on another page:** a member of `PortalContentPages` (the server must know the page key), a
  `PortalContentStore` in the page's `providers`, and `<app-portal-content-state>` above the sections.
- **Another contact field:** a member of `ContactFields`, its check in `contactFormErrors()`, its limit in
  `ContactLimits`, the control in the template and its keys in both bundles; the server error mapping follows the field
  name.

## Rules

- Every page is public: no guard and no permission key on a portal route, and every `dga-button` carries
  `data-public-action` (the permission-gates check enforces it). A page that needs a signed-in user does not belong here.
- HTTP only through `PortalContactClient` and `PortalContentClient` on core `ApiService`; the calls are anonymous and
  self-handled, so a failure never opens the app-wide error dialog and a 401 never ends a session.
- Content from the API is plain text rendered through interpolation (never `innerHTML`); paragraphs split on blank
  lines and a single line break shows as one (`white-space: pre-line`).
- No raw text and no content in templates: every string is a `portal.*` key in both `ar.json` and `en.json`;
  `portal-translations.spec.ts` checks presence, placeholders and Arabic letters.
- Starter-neutral content only: no product, organization or authority names, phone numbers or email addresses in the
  texts; a project writes its own.
- Topic ids are lookup ids: compare only through `ContactTopicIds`.

## Gotchas

- The landing page sits at the portal mount's empty child path, so its full path equals the mount's (`portal`). The
  route-table gate exempts only the outermost route with a public full path; the app's gate must treat the public
  portal frame as a layout (or otherwise exempt its empty child) for the landing page to pass.
- `dga-page-feedback` moves focus to itself for every new state object. The page builds that object from the store's
  outcome and the active language, so switching the language while a banner shows moves focus to it again.
- The contact form binds each control one-way (`[value]` + `(valueChange)`), so a server field error can be cleared the
  moment the reader edits that field. A spec that types into several fields and submits in one task must let the page
  render first, or the reset after a successful send is not seen by the controls.
- The FAQ search runs on the API text of the active language; switching the language re-filters the list without a
  new request (the store keeps both languages).
- Section anchors are positional (`part-1`...), never the server's public ids (which need not fit the table of
  contents' slug rule), so an anchor link can point elsewhere after an administrator reorders the sections.
- The table of contents pipes each entry's `titleKey` through the translation pipe; the content pages pass the
  already translated title, which renders as written because it is not a key.
- Page specs give `FakePortalContentClient` (`portal.testing.ts`) an answer or a failure per page; without either the
  page stays in its loading state.
- `portal-translations.spec.ts` reads the real bundles: it fails until the `portal.*` fragment is in both `ar.json` and
  `en.json`.
