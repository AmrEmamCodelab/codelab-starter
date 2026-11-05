# Page patterns

Every page of a starter app is one of these 8 patterns. A pattern is a reusable Angular component in
`frontend/starter-web/src/app/patterns/` that renders state and forwards user intent; the screen that places it owns
the data, the API calls (through its feature client and `ApiService`) and the permission gates. A new screen is
mostly configuration: pick the pattern, give it columns, fields or steps, translation keys and permission keys.

Code-level reference for every pattern: [patterns/MODULE.md](../../frontend/starter-web/src/app/patterns/MODULE.md).
Live examples in `ar` and `en`: the showcase (`/showcase`, dev and staging).

## Which pattern?

| The page... | Pattern | Who uses it | Doc |
| --- | --- | --- | --- |
| lists records for administration, with search, sort, paging and create/edit in a dialog | **Admin list + editor dialog** (`app-admin-list-pattern`, `app-editor-dialog-pattern`) | staff, administrators | [admin-list.md](patterns/admin-list.md) |
| edits a set of keys organised as a tree (permissions of a role, features) | **Tree editor** (`app-tree-editor-pattern`) | administrators | [tree-editor.md](patterns/tree-editor.md) |
| lists work items waiting for staff, with filters by status and assignment | **Work queue** | staff | [work-queue.md](patterns/work-queue.md) |
| shows one record to staff with its data, history and the actions they may take (approve, return, reject with a reason) | **Staff detail + decision dialog** (`app-decision-dialog`) | staff | [staff-detail.md](patterns/staff-detail.md) |
| collects a request from a requester in several steps with review and submit | **Multi-step wizard** | requesters | [wizard.md](patterns/wizard.md) |
| shows a requester their own request with its status history | **Requester detail + status tracker** (`app-status-tracker`) | requesters | [requester-detail.md](patterns/requester-detail.md) |
| shows static or managed public content (service guide, FAQ, privacy, terms, contact) | **Portal content page** | the public | [portal-content.md](patterns/portal-content.md) |
| is the first page after sign-in: summary cards, shortcuts and recent items | **Landing dashboard** | requesters, staff | [landing-dashboard.md](patterns/landing-dashboard.md) |

When two patterns seem to fit, choose by **who acts**: staff deciding on someone else's record use the staff detail;
a requester following their own record uses the requester detail. A list a requester sees of their own requests is a
work-queue variant without assignment filters, not an admin list.

## What every pattern guarantees

- **Components:** only `@codelab/ng-dga` ([component-catalog.md](component-catalog.md)) and native semantic
  elements (`table`, `dialog`, `fieldset`) styled with tokens ([tokens.md](tokens.md)). No third-party UI.
- **Text:** no raw text. The pattern's own labels live under `patterns.<name>.*` in `ar.json` and `en.json`; the
  screen passes its own keys (`titleKey`, `emptyKey`, column `headerKey`...).
- **Direction:** logical CSS only, so it mirrors in RTL without code. Direction icons mirror through the icon manifest.
- **Accessibility:** native elements first, named landmarks and controls, keyboard support and visible focus, errors
  announced (`role="alert"`). Checked by axe in the harness: 0 serious or critical.
- **Permissions:** a pattern never checks permissions. The screen gates the route (`permissionGuard()`) and every
  action button (`*hasPermission`); pattern-internal buttons carry `data-public-action`.
- **Loading, empty and error states** are part of the pattern, with the API error's reference code shown.

## Adding a pattern

A ninth pattern is a platform decision (owner review). It needs its folder under `src/app/patterns/`, unit specs, a
page in [patterns/](patterns/), a row in the table above and in `patterns/MODULE.md`, a showcase section, and its
translation keys under `patterns.<name>.*` in both languages.
