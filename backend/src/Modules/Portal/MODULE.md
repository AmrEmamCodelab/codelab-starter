# Portal module

## Purpose

The public portal's backend (module 15, completing the P4 portal pages). The Portal module owns:

- **Contact us.** An anonymous, rate-limited form. It stores each message in `portal.ContactMessages` under a unique
  `CU-YYYY-NNN` reference number (`IRequestNumberAllocator`, prefix `PortalNumberPrefixes.ContactUs`). Once the
  message is saved, the module announces it to the configured inbox through the kernel `INotificationService`
  (template `contact-received`, sent through the outbox).
- **Portal content.** The FAQ, service guide, privacy and terms pages. Each page holds bilingual plain-text sections
  (`portal.ContentSections`), plus one header row per page (`portal.ContentPages`, which records when it was last
  edited).
  - The starter wording is seeded by migration (`ContentSeed`, the text of the former static bundles).
  - The public read is anonymous and cached on each node.
  - An administrator replaces a page's sections in the admin editor.
  - The kernel audit interceptor records every change.

Tables (schema `portal`):

| Table | Columns |
| --- | --- |
| `ContactMessages` | `Id`, `ReferenceNumber` (unique), `FullName`, `Email`, `TopicId` (a `contact-topics` lookup id), `Message`, `Language` (int `AppLanguage`), `ReceivedAt` |
| `ContentPages` | `Page` (int `PortalPage`, the key), `UpdatedAt` (null for the seeded text) |
| `ContentSections` | `Id`, `Page` (FK Restrict), `TitleAr`, `TitleEn`, `BodyAr`, `BodyEn`, `SortOrder` |

`PortalPage` (int): `Faq` = 1, `ServiceGuide` = 2, `Privacy` = 3, `Terms` = 4. On the wire, each page is its kebab key
(`PortalPages`): `faq`, `service-guide`, `privacy`, `terms`.

## Use

Permissions (`PortalPermissionKeys`, declared by `PortalPermissionCatalogSource`): `portal.content.view` and
`portal.content.manage`. Section ids on the wire are PublicIds of the family `portal-sections` (`PortalSectionIds`).
Errors are problem+json with a `code`.

| Endpoint | Access | Body / answer |
| --- | --- | --- |
| `POST /api/portal/contact-messages` | anonymous (allowlisted), `ClientRateLimitOptions.PolicyName` rate limit, signature-exempt | `{fullName, email, topicId, message, language: 'ar'\|'en'}` -> 201 `{referenceNumber}`; 400 field errors; 409 `PORTAL_CONTACT_DISABLED`; 429 |
| `GET /api/portal/content/{page}` | anonymous (allowlisted), cached | `{page, updatedAt, sections: [{id, titleAr, titleEn, bodyAr, bodyEn, sortOrder}]}`; 404 `PORTAL_PAGE_NOT_FOUND` |
| `GET /api/admin/portal/content/{page}` | `portal.content.view` | the same shape, never cached |
| `PUT /api/admin/portal/content/{page}` | `portal.content.manage` | `{sections: [{id: string\|null, titleAr, titleEn, bodyAr, bodyEn, sortOrder}]}` -> 200 the page as stored; 400, 404, 409 `PORTAL_SECTION_NOT_ON_PAGE` |

**Contact switch.** While the runtime setting `portal.contact-enabled` (`SettingKeys.PortalContactEnabled`, read
through the kernel port `IRuntimeSettings` on every submit) is false, the form is refused with 409
`PORTAL_CONTACT_DISABLED` before anything is validated, numbered, stored or sent. A change on the admin settings screen
reaches every node within the settings cache TTL (30 seconds).

**Contact rules.** The rules apply to trimmed values and mirror the form's `ContactLimits`:

- `fullName`: 1 to 100 characters.
- `email`: at most 254 characters, in the shape local part, `@`, dotted domain, with no white space.
- `message`: 10 to 2000 characters. A shorter message gets the field message `TOO_SHORT`.
- `language`: `ar` or `en`.
- `topicId`: an active `contact-topics` value. An unknown or retired topic gets the field error and code
  `PORTAL_TOPIC_NOT_FOUND`.

On a refusal, the module allocates no number and stores nothing.

**Replace rules.** A section with an `id` updates that section. A section without one is added. A section the list
leaves out is removed.

- The list may be empty. It holds at most 100 sections.
- Each section needs both titles (at most 200 characters) and both bodies (at most 8000).
- `sortOrder` runs from 0 to 100000.
- Field errors are indexed, for example `sections[2].titleEn`. An id that appears twice in the list gets `DUPLICATE`.
- An id that is not a section of this page answers 409.
- A replace that changes nothing writes nothing.

**Contact inbox options** (`Portal:Contact`, validated on start):

- `InboxEmail` is required. The starter default is the placeholder `contact@example.invalid`; every deployment sets its
  own.
- `InboxLanguage` is the language of the inbox e-mail, as an int (default 1, Arabic). The topic name in the e-mail is
  in this language.

**Caching.** `GetPublishedPortalContent` serves each page from `IPortalContentCache` (`MemoryPortalContentCache`, key
`portal:content:<page int>`) for `PortalLimits.ContentCacheTtl` (60 seconds, a constant). A replace drops the page's
entry on the node that made it, after the save. Other nodes see the change within the TTL.

## Extend

- **Add a portal page** (for example an accessibility statement):
  1. Add a `PortalPage` member with the next explicit value.
  2. Add its key constant and its entry in `PortalPages`.
  3. Write a new migration, reshaped like `InitialCreate`, that inserts its `ContentPages` row and its seeded sections.
     Never edit `InitialCreate`. Freeze `PortalMigrationModel` first if the schema changes.
  4. On the frontend, add a `PortalContentPages` member and a page with `PortalContentStore`.
  5. Add a row to the page table above.
- **Change seeded wording in a project:** use the admin editor. A starter-wide change is a new migration with
  `UpdateData`.
- **Another contact field:**
  1. Add the column through a new migration.
  2. Add `[PersonalData]` when the field holds what the sender typed.
  3. Add the member to `SubmitContactMessageCommand` and its validator.
  4. If the field belongs in the inbox e-mail, add a placeholder to the `contact-received` template through a
     Notifications migration.
- **New endpoint or permission:** use `new-endpoint` or `new-permission` with `--module Portal`.

## Rules

- **Store, then notify** (`portal.store-before-notify`). The message is validated, numbered and saved before the
  inbox is notified. A notification failure after the save is a post-commit side effect (ERROR-HANDLING row 8): it is
  logged with the reference number only, and the sender keeps the receipt.
- **The anonymous write is rate-limited** (`portal.anonymous-write-rate-limited`, contract test
  `AllAnonymousWritesAreRateLimited`). Never disable the limit; tune `Codelab:Security:RateLimiting` instead, including
  `TrustedProxies` behind a load balancer.
- **Personal data is marked.** `FullName`, `Email` and `Message` carry `[PersonalData]`, so the audit trail masks
  them. Logs never carry the sender's details.
- **Content is plain text.** Paragraphs are separated by a blank line. Never store HTML or Markdown.
- **The cache is invalidated after every changing replace,** and no entry outlives the TTL.
- **The module never references another module.** It uses kernel ports (`IRequestNumberAllocator`,
  `INotificationService`, `IRuntimeSettings`, `ILookupValues`) and its own ports; it reads no other module's tables.
  Contact topics are read only through `ILookupValues`. Never add SQL that names the `lookups` schema: the
  architecture test `DataBoundaryTests.NoModuleSqlNamesAnotherModulesSchema` fails the build on it.

## Gotchas

- **Topics come from the Lookups module's cache.** `LookupContactTopics` (behind the Portal port `IContactTopics`)
  reads the `contact-topics` value through the kernel port `ILookupValues`, so a topic retired through the Lookups
  admin API is refused at once on that node and within the Lookups cache TTL on the other nodes. The Portal module
  needs the Lookups module in the host, not its tables.
- **`topicId` is a number on the wire.** It is a lookup id seeded by migration, not a database identity. The contract
  test allows it through `EndpointAllowlist.NumericLookupIdMembers` (`SubmitContactMessageCommand.TopicId`).
- **Numbers have gaps.** A number is taken even when the save then fails, and SQL sequences are not transactional.
- **The page's `updatedAt` stays null until the first edit.** Seeded text has no edit time.
- **Concurrent replaces: the last one wins.** A replace that names a section another editor removed gets 409 and
  should reload.
- **Contact messages are not shown anywhere yet.** They reach staff through the inbox e-mail. An admin inbox screen,
  and a retention or purge job, are follow-ups.
