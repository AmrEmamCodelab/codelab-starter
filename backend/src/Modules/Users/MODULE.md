# Users module

## Purpose

Users and organizations (module 2). The Users module owns:

- **Organizations** (`users.Organizations`): a unique `Code` (trimmed, upper-case, ASCII letters, digits, `_`, `-`,
  at most 50), an English and an Arabic name, an `OrganizationKind` (`Internal = 1`, a unit of the portal's own
  organization; `External = 2`, an outside entity) and an active flag. Organizations are never deleted; a deactivated
  one keeps its accounts but accepts no new member.
- **The user admin endpoints.** Accounts belong to the Identity module; this module reads and changes them only
  through the kernel `IAccountAdministration` port and joins this module's organization names onto them.
- **The Excel user import** (`users.ImportBatches`): preview one `.xlsx`, then commit the previewed batch. Rebuilt
  without any shared or default password: every password-bound account gets a sealed one-time activation ticket
  instead, every Active Directory or SSO account gets none.
- **Activation links and the activation e-mail.** `AccountActivations` asks Identity to issue a ticket at once (an
  administrator's reset takes effect immediately: the password is cleared and every older link superseded). Then
  `ActivationDeliveryRules.DeliveryFor` decides (`ActivationDelivery`: `AdministratorLink = 1`, `Email = 2`):
  - **the account has an e-mail address:** a Pending `account-activation` outbox message whose payload names the
    ticket by id only (`AccountActivationPayload`: `userPublicId`, `ticketId`, `expiresAt`); the response carries no
    link. At drain time `AccountActivationEmailHandler` (the one `IOutboxHandler` of that type) asks Identity for a
    **fresh** ticket, builds the link (`Users:Activation:LinkBaseUrl` + `/activate#ticket=` + ticket) and sends it at
    once through `INotificationService.SendImmediateEmailAsync` (template `account-activation`, placeholders
    `activationLink` and `expiresAt` as `yyyy-MM-dd HH:mm UTC`, in the recipient's language from Identity's
    `INotificationRecipients`). The ticket exists only in that e-mail.
  - **no e-mail address:** the administrator receives the link in the response and hands it over; no outbox message.

## Use

Permissions (`UsersPermissionKeys`, declared by `UsersPermissionCatalogSource`): `users.view`, `users.manage`,
`users.import`, `organizations.view`, `organizations.manage`. Ids on the wire are PublicIds with the labels `users`
(the same label as Identity's session user and Access's grant editor), `organizations` and `user-imports`. Enums travel
as integers. Every error is problem+json with a `code`; the client renders `errors.<CODE>`.

| Endpoint | Permission | Body / answer |
| --- | --- | --- |
| `GET /api/admin/users?search&organizationId&kind&isActive&page&pageSize` | `users.view` | `{items:[user], total}` |
| `GET /api/admin/users/{id}` | `users.view` | `user` |
| `POST /api/admin/users` | `users.manage` | `{userName, displayName, email?, loginName?, upn?, kind, binding, organizationId?}` -> 201 `{user, activation?}` |
| `PUT /api/admin/users/{id}` | `users.manage` | `{displayName, email?, loginName?, upn?, kind, binding, organizationId?}` -> `user` |
| `POST /api/admin/users/{id}/deactivate` / `reactivate` / `unlock` | `users.manage` | 204 |
| `POST /api/admin/users/{id}/activation` | `users.manage` | `activation` (password binding; an admin password reset) |
| `GET /api/admin/organizations?search&kind&isActive&page&pageSize` | `organizations.view` | `{items:[organization], total}` |
| `GET /api/admin/organizations/{id}` | `organizations.view` | `organization` |
| `POST /api/admin/organizations` | `organizations.manage` | `{code, nameEn, nameAr, kind}` -> 201 `organization` |
| `PUT /api/admin/organizations/{id}` | `organizations.manage` | `{code, nameEn, nameAr, kind}` -> `organization` |
| `POST /api/admin/organizations/{id}/deactivate` / `reactivate` | `organizations.manage` | 204 |
| `POST /api/admin/user-import/preview` (multipart field `file`, one `.xlsx`) | `users.import` | `{batchId, rows:[{rowNumber, userName, displayName, email, kind, binding, organizationCode, valid, errors}], validCount, invalidCount}` |
| `POST /api/admin/user-import/{batchId}/commit` | `users.import` | `{created:[{id, userName, binding, activationLink?, expiresAt?, activationDelivery?, activationErrorCode?}], rejected:[{rowNumber, errors}]}` |

`user` is `{id, userName, displayName, email, loginName, upn, kind, binding, organizationId, organizationName,
isActive, hasPassword, isLockedOut, isBreakGlass}` (`organizationName` in the request language, Arabic by default);
`organization` is `{id, code, nameEn, nameAr, kind, isActive}`; `activation` is `{activationLink: string|null, expiresAt,
delivery}` with `delivery` 1 (AdministratorLink: `activationLink` set) or 2 (Email: `activationLink` null, the user
receives it by e-mail). Lists are 1-based pages of at most 100 (default 20).

The import sheet: the first worksheet, the first row with text is the header, matched ignoring case, spaces, `_` and
`-` in any order: `UserName`, `DisplayName`, `Kind` (`internal`|`external`), `Binding` (`password`|`ad`|`sso`;
`active directory` also reads as AD) are required; `Email`, `LoginName`, `Upn`, `OrganizationCode` are optional.
There is no password column. Row errors (`UsersImportRowErrorCodes`, `USERS_IMPORT_ROW_*`) cover required fields,
lengths, e-mail shape, kind and binding words, user names and e-mail addresses repeated in the file, user names
already taken, and the organization rules below. A sample: `tools/devseed/user-import/New-SampleUserImport.ps1`.

Configuration: `Users:Activation:LinkBaseUrl` (`ActivationOptions`; the web origin, e.g. `http://localhost:4280`),
`Users:Import:MaxRows` (default 1000, at most 5000) and `Users:Import:BatchLifetimeMinutes` (default 30, at most
1440) (`ImportOptions`). Identity must have sealed tickets on (`Codelab:Security:Tickets`).

## Extend

- Activation delivery: the rules (who is e-mailed, when a queued e-mail is stale) live only in
  `ActivationDeliveryRules`; the payload shape only in `ActivationPayloads`. A new stale state is a new
  `ActivationEmailSkip` member with the next explicit value. The e-mail text is the Notifications module's
  `account-activation` template (a Notifications migration), never a string here.
- New endpoint: `node tools/scaffold/new-endpoint.mjs --module Users ...`, then a use case in `Application/` with its
  unit tests; account data only through `IAccountAdministration` (extend the kernel port, implemented by Identity,
  when a screen needs more).
- New import column: add it to `ImportColumn` and `ImportColumns.Template`, read it in `ImportColumns.RowOf`, check it
  in `ImportRowRules` or `ImportRowValidator`, map it in `CommitUserImport`, and extend the tests and the sample.
- New permission: `node tools/scaffold/new-permission.mjs --module Users --action <kebab>`.
- A schema change: a new migration next to `InitialCreate`, reshaped the same way, with `UsersMigrationModel` updated;
  `UsersModelTests` proves the model has no pending change.

## Rules

- **No default, shared or generated password, ever.** No body, sheet column or response carries a password. A
  password-bound account gets an activation link; an AD or SSO account none.
- **The sealed ticket stays out of storage.** It goes only into the link returned to the administrator (an account
  without an e-mail address) or into the e-mail `AccountActivationEmailHandler` sends at drain time; the outbox
  payload names it by id; no table, log or error message here holds it (the handler logs the message id and the fresh
  ticket id only). It is passed only to `SendImmediateEmailAsync`, which stores nothing, never to `NotifyUserAsync`.
  The import batch stores the rows as read (names, e-mail addresses), never a ticket.
- **Supersede semantics.** Every issue (the request-time one and the drain-time one) supersedes the account's older
  links, so the newest e-mailed link is the valid one; one-time use is enforced by Identity at redemption
  (`IDENTITY_ACTIVATION_REJECTED` for a used or superseded link).
- **A queued activation e-mail is idempotent against activation.** At drain time the handler re-reads the account and
  completes without issuing anything when `ActivationDeliveryRules.SkipReasonOf` says it is stale (not found, no longer
  password bound, break-glass, deactivated, password already set, address removed), so a redelivery never clears a
  password the user just set.
- **Membership** (`OrganizationMembership`, one owner for the form and the import): an external user belongs to an
  active external organization (required); an internal user to an active internal organization or none. An edit that
  keeps the current organization is allowed after it was deactivated. An organization's kind cannot change while
  accounts belong to it (`USERS_ORGANIZATION_KIND_IN_USE`).
- The break-glass administrator is configured, never edited, deactivated or re-issued here (`USERS_BREAK_GLASS_PROTECTED`);
  an administrator cannot deactivate their own account (`USERS_CANNOT_DEACTIVATE_SELF`).
- **Organization scope** (`AccountAccess`, `ScopedOrganizations`, the import validator; the rule itself is the kernel
  `ScopeDecision.Reaches` / `Narrow`). A holder of `users.view`, `users.manage`, `users.import`, `organizations.view` or
  `organizations.manage` at `OwnOrganization` scope reaches only their own organization: lists narrow to it (empty when
  the holder has no organization), any other account or organization answers 404 (`USERS_USER_NOT_FOUND`,
  `USERS_ORGANIZATION_NOT_FOUND`), creating or moving an account outside it is 403 `USERS_ORGANIZATION_OUT_OF_SCOPE`
  (an account without an organization is outside it too), an import row outside it is
  `USERS_IMPORT_ROW_ORGANIZATION_OUT_OF_SCOPE`, and creating an organization needs `organizations.manage` at `Global`.
- **Privilege** (`AccountAccess.FindForChangeAsync`, over the kernel `IAdministrationGuard` that Access implements).
  Edit, deactivate, reactivate and activation reissue are refused with 403 `USERS_TARGET_OUTRANKS_CALLER` when the
  target holds (or, deactivated, would hold) a permission the caller does not hold as widely: an activation link or a
  new e-mail, login name or binding hands the account to whoever controls it. Read and unlock are not privilege-checked
  (they hand over nothing). An administrator cannot change their own kind, binding or organization
  (`USERS_CANNOT_CHANGE_OWN_ACCESS`), and the last access manager cannot be deactivated (`USERS_LAST_ACCESS_MANAGER`).
  Identity ends the account's sessions when its binding, e-mail, login name or UPN changes.
- Deactivation is instant: Identity bumps the user's session version, so the next API call of every open session
  answers 401. Reactivation lets the user sign in again.
- **Import caps** (`ImportLimits`, checked while reading, never trusted from the file): upload 5 MiB, 256 zip entries,
  32 MiB inflated in total (counted on the bytes actually read), 100,000 shared strings, 64 columns (more are
  ignored), 1024 characters per cell, `Users:Import:MaxRows` data rows. No DTD, no external XML resolution. No
  spreadsheet package (the rules file forbids one).
- A batch is committed only by the administrator who previewed it, once (the status is the concurrency token), before
  it expires. The commit re-validates every row; a duplicate that appeared since the preview is a rejected row.
- Never reference the Identity or Access module; enums are integers with explicit values; keys, codes, routes and
  limits are constants.

## Gotchas

- **The outbox is not transactional with Identity's save:** the account and its ticket are written by Identity, then
  the outbox row by the kernel outbox, in separate saves. A crash in between leaves an account whose activation has no
  outbox row (and, for an e-mailed account, no link anywhere): the administrator issues the activation again from the
  users list.
- **Delivery is at least once.** A drain that sends the e-mail and crashes before the outbox marks it Sent delivers it
  again; while the user has not activated yet, that sends a second e-mail whose link supersedes the first. Two
  administrator re-issues in a row likewise send two e-mails; only the newest link works.
- **An e-mailed activation waits for the drain** (the `outbox-drain` job, every 5 minutes, `Messaging:Outbox:Enabled`).
  The `expiresAt` in the response is the request-time ticket's; the e-mailed link is issued later and expires later by
  that delay. Where e-mail is not delivered (outbox off, `Notifications:Email` still the FileDrop default in a real
  deployment) the user receives nothing: the deployment kit must configure the relay.
- **Failures retry, then fail.** A missing link base at drain time, an Identity refusal (for example sealed tickets
  switched off) or a send failure throws, so the outbox retries with back-off and marks the row Failed after
  `OutboxLimits.MaxAttempts`; the administrator then issues the activation again. A payload that cannot be read is
  logged and completed (retrying cannot fix it).
- Without `Users:Activation:LinkBaseUrl` the host still starts; creating or committing a password-bound account then
  answers `USERS_ACTIVATION_LINK_UNCONFIGURED` (422) before anything is created. Every deployment kit sets it.
- If Identity refuses the ticket after an account was created, the create answers that refusal (the account exists;
  issue the link again from the list) and the import lists the account with `activationErrorCode` instead of a link.
- The preview's "user name taken" check asks Identity once per row (search, exact match); Identity's own create is the
  final word. Previewed batches are not purged yet (a clean-up job arrives with the jobs module, phase 5); the
  `ExpiresAt` index is there for it.
- Use `UserKind.@Internal` / `OrganizationKind.@Internal` in code: the scrub sweep reads a member access that ends in `Internal` as a host name.
- `CLAUDE.md`, `BannedSymbols.generated.txt`, `.editorconfig` and `CodeMetricsConfig.txt` here are generated by the
  rules engine; never edit them by hand. Keep the `// scaffold:...` markers.
