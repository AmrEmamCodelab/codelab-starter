# Public portal (`features/portal`)

## Purpose

Module 15's user interface: the public pages every visitor can open without signing in, Arabic first with English.
P4 ships them with static bilingual content from the i18n bundles and a contact form validated against a mocked API
(unit tests). The form shows only when `config.json` sets `portalContactForm: true` (default false); until the
endpoint is deployed the page shows the ways to reach the team and a "form coming soon" notice, never a form that
can only fail;
P5 moves the content to bilingual settings and lookup data and delivers the contact-us backend.

| Piece | What it does | Files |
| --- | --- | --- |
| `PORTAL_ROUTES`, `PORTAL_PUBLIC_PATHS` | The six pages under `/portal` (children of the portal shell, paths from core `PortalPaths`), and their full paths for the app's `PUBLIC_ROUTE_PATHS`. `PortalRouteDataKeys.Document` passes a legal document to the legal page | `portal.routes.ts` |
| `PortalLinks`, `portalBreadcrumbs()` | Absolute links of the portal pages (through the layouts' `portalLink`), and the home-to-page breadcrumb trail every page header shows | `portal-links.ts` |
| `PortalLandingPageComponent` | `/portal`: on the landing dashboard pattern. A brand `dga-hero` with the page `h1` and two links, a `dga-hero-slider` of highlights (no autoplay), service cards (`dga-card`), public tiles derived from the content (guide steps, answered questions, languages) and quick links to the legal pages | `landing/` |
| `ServiceGuidePageComponent` | `/portal/service-guide`: on the portal content pattern. About the service, who can apply, documents to prepare, numbered steps (an ordered list with a spoken "Step n"), a `dga-alert` before-you-submit note and help links | `service-guide/` |
| `FaqPageComponent` + `filterFaq()` | `/portal/faq`: on the portal content pattern, one `dga-accordion` per category. The search box filters questions and answers as the reader types (every word must match; Arabic diacritics, tatweel, hamza, ta marbuta and alef maksura variants folded), opens the matches, announces the count and shows a no-results state | `faq/` |
| `ContactPageComponent` + `PortalContactStore` | `/portal/contact`: name, email, topic and message (`dga-text-input`, `dga-select`, `dga-textarea`). Client checks show once a field is left or after the first submit; the server's problem+json field errors show under their fields until edited; the outcome shows in `dga-page-feedback` (sent with the reference number, validation, rate-limited, offline, failed with the reference code). The store is provided per page | `contact/` |
| Contact rules | `contactFormErrors()`, `ContactLimits`, `ContactFields`, `ContactTopicIds` (lookup-style ids), `contactFailureFrom()` (`ContactFailureKind`), `successOutcome()`, `failureOutcome()`, `serverFieldMessageKey()` | `contact/contact-form.ts`, `contact/contact-outcome.ts` |
| `PortalContactClient`, `PortalApiPaths` | `POST api/portal/contact-messages` through core `ApiService`: anonymous (no bearer, unsigned) and self-handled (no app-wide error dialog) | `contact/portal-contact.client.ts` |
| `LegalPageComponent` | `/portal/privacy` and `/portal/terms`: one component, the document comes from route data (`PRIVACY_DOCUMENT`, `TERMS_DOCUMENT`), sections with paragraphs and lists, a table of contents and a closing contact link | `legal/` |
| Content lists | Every page's structure (sections, FAQ categories and entries, guide steps, slides, cards, tiles) as typed lists of translation keys, so P5 swaps the source without touching a template | `*/*-content.ts` |
| Texts | One `*-translation-keys.ts` catalogue per page under `portal.<page>.*`, plus `portal.common.*`; `PORTAL_TRANSLATION_KEYS` lists them all | `*-translation-keys.ts`, `portal-translation-keys.ts` |

## Use

The app mounts the pages inside the public portal frame:

```ts
{ path: RoutePaths.Portal, component: PortalShellComponent, children: PORTAL_ROUTES }
```

and spreads `PORTAL_PUBLIC_PATHS` into `PUBLIC_ROUTE_PATHS` (`public-routes.ts`). Links to a portal page use
`PortalLinks` (or the layouts' `portalLink(PortalPaths.X)`), never a literal path. Nothing else imports this feature.

The contact endpoint arrives in P5. Its contract, as this page expects it:

| Answer | The page shows |
| --- | --- |
| 200 `{ referenceNumber }` (or 204) | success banner (with the number when sent); the form is cleared |
| 400/422 problem+json with `errors` (`fullName`, `email`, `topicId`, `message`; any case) | each field's first translated message (`errors.<CODE>`, else "check this field") under it, and a validation banner |
| 429 | the rate-limit banner; the form keeps its values |
| status 0 | the offline banner |
| 5xx (or anything else) | the failure banner, with the `referenceCode` when the server sent one |

The body is `{ fullName, email, topicId, message, language }` with trimmed values; `ContactLimits` is the client copy of
the server's limits. The page design references are [portal-content](../../../../../../docs/design/patterns/portal-content.md)
and [landing-dashboard](../../../../../../docs/design/patterns/landing-dashboard.md).

## Extend

- **Another FAQ question or guide step:** add its keys to the page's `*-translation-keys.ts`, the entry to
  `FAQ_CATEGORIES` or `GUIDE_STEPS`, and the texts to both bundles. The specs and the landing tiles pick it up.
- **Another portal page:** a path in core `PortalPaths`, a route in `PORTAL_ROUTES` (its full path then lands in
  `PORTAL_PUBLIC_PATHS` by itself), a link in `PortalLinks`, a translation-key catalogue added to
  `PORTAL_TRANSLATION_KEYS`, and the page built on the portal content pattern.
- **Data-driven content (P5):** replace the static lists in `*-content.ts` with settings or lookup data of the same
  shape (keys or already translated text); the templates do not change.
- **Another contact field:** a member of `ContactFields`, its check in `contactFormErrors()`, its limit in
  `ContactLimits`, the control in the template and its keys in both bundles; the server error mapping follows the field
  name.

## Rules

- Every page is public: no guard and no permission key on a portal route, and every `dga-button` carries
  `data-public-action` (the permission-gates check enforces it). A page that needs a signed-in user does not belong here.
- HTTP only through `PortalContactClient` on core `ApiService`; the call is anonymous and self-handled, so a failure
  never opens the app-wide error dialog and a 401 never ends a session.
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
- The FAQ search runs on the translated text of the active language; switching the language re-filters the list.
- `portal-translations.spec.ts` reads the real bundles: it fails until the `portal.*` fragment is in both `ar.json` and
  `en.json`.
