# Access module

## Purpose

Who may do what (module 3, roles and permissions). The Access module owns:

- **The permission catalog in code.** Each module declares its keys as constants of a `<Name>PermissionKeys` class
  and registers an `IPermissionCatalogSource`. `PermissionCatalog` (a singleton) is the union of every source, sorted
  by key. It is validated when it is built: a duplicate key, a key that breaks `PermissionKeyFormat` or a missing
  description stops the host at start.
- **The catalog in the database.** `PermissionSynchronizer`, run by `PermissionCatalogStartupTask` in the
  `ReferenceData` startup phase, writes the catalog to `access.Permissions` idempotently. It inserts new keys,
  refreshes changed ones and retires (never deletes) keys that left the code.
- **Roles.** A role (`access.Roles`) has a stable lower-case kebab `Code` (never changes), unique English and Arabic
  names, an active flag and its permissions (`access.RolePermissions`). Roles are never deleted; an inactive role
  grants nothing but keeps its permissions and assignments, so reactivating it restores them.
- **Assignments and direct grants, with a scope.** A user holds ordinary roles (`access.UserRoleAssignments`) and
  direct permissions (`access.UserPermissionGrants`), each at a `PermissionScope`: `Global = 1` (every
  organization) or `OwnOrganization = 2` (the user's own organization only).
- **Virtual roles.** One role per `VirtualRoleKind` (`AllInternalUsers = 1`, `AllExternalUsers = 2`,
  `AllAuthenticatedUsers = 3`), seeded by the `RolesAndGrants` migration with no permission. Every active user the
  kind matches holds the role's permissions, at `OwnOrganization` scope (least privilege; a global grant needs a real
  role or a direct grant). Their names and permissions are editable in the role editor; they are never assigned.
- **The live resolve.** `IUserPermissionResolver` reads the account through the kernel `IUserDirectory` port
  (implemented by Identity) and the grants from this module's tables, on every request, and `PermissionResolution`
  applies the one rule:
  - an unknown or inactive user holds nothing;
  - the break-glass administrator (`HoldsAllPermissions`) holds the whole catalog, globally;
  - anyone else holds the union of their direct grants, their ACTIVE assigned roles and the ACTIVE virtual roles
    matching their kind (`AllAuthenticatedUsers` always matches), filtered to the live catalog (retired keys grant
    nothing);
  - a key held through several sources gets the widest scope: `Global` beats `OwnOrganization`.
- **Organization scope for other modules.** `CurrentUserOrganizationScope` implements the kernel `IOrganizationScope`.
  The grant editor applies it too (`ScopedAccounts`): an `access.grants.view` / `access.grants.manage` holder at
  `OwnOrganization` scope finds, reads and edits only their organization's accounts (any other answers 404
  `ACCESS_USER_NOT_FOUND`).
- **Privilege checks for account administration.** `AdministrationGuard` implements the kernel `IAdministrationGuard`
  for the Users module: `CallerCoversAsync` (the caller holds every permission the target would hold while active, each
  at least as widely) and `IsLastAccessManagerAsync` (`ManagerContinuityGuard.CheckDeactivationAsync`).
- **The `[HasPermission]` handler.** `PermissionHandler` evaluates the kernel `HasPermissionAttribute` against the
  caller's live permissions. The access token carries identity only (`sub`, `sid`), never permissions.
- **The self-lockout guard.** `ManagerContinuityGuard` checks every role and grant write (see Rules).
- **The grant ceiling.** `GrantCeiling` checks every user grant write (`UserGrantWriteGuards` runs break-glass,
  ceiling, then self-lockout): no caller hands out more than they hold (see Rules).
- **Endpoints.** JSON under `/api`; errors are problem+json with a `code`; ids are PublicIds (`roles` for roles,
  `users` for users, the same label as the Identity and Users modules, so a user id from the users list opens the
  grant editor). Enums travel as their integer values.

  | Endpoint | Guard | Request | Response |
  | --- | --- | --- | --- |
  | `GET /api/me/permissions` | `[AuthenticatedUser]` (allowlisted) | | `{ permissions: [key] }` |
  | `GET /api/dashboard` | `dashboard.view` | | `{}` |
  | `GET /api/access/admin` | `access.admin.view` | | `{}` (placeholder) |
  | `GET /api/admin/access/permissions` | `access.roles.view` | | `[{ key, module, description, isRetired }]` sorted by key |
  | `GET /api/admin/access/roles` | `access.roles.view` | | `[{ id, code, nameEn, nameAr, isActive, isVirtual, virtualKind, permissionCount }]`, virtual roles first |
  | `GET /api/admin/access/roles/{id}` | `access.roles.view` | | role (below) |
  | `POST /api/admin/access/roles` | `access.roles.manage` | `{ code, nameEn, nameAr, permissions: [key] }` | 201, role |
  | `PUT /api/admin/access/roles/{id}` | `access.roles.manage` | `{ nameEn, nameAr, permissions: [key], isActive, confirmBreakGlassOnly? }` | role |
  | `GET /api/admin/access/users?search=` | `access.grants.view` | | `[{ id, userName, displayName, isActive }]`, at most 20, active and deactivated |
  | `GET /api/admin/access/users/{userId}/grants` | `access.grants.view` | | grants (below) |
  | `PUT /api/admin/access/users/{userId}/grants` | `access.grants.manage` | `{ roles: [{ roleId, scope }], permissions: [{ key, scope }], confirmBreakGlassOnly? }` | grants |

  - **role:** `{ id, code, nameEn, nameAr, isActive, isVirtual, virtualKind, permissions: [key], warnings: [code] }`.
  - **grants:** `{ user: { id, userName, displayName, isActive, kind, isBreakGlass }, roles: [{ roleId, code, nameEn,
    nameAr, isActive, scope }], permissions: [{ key, scope }], virtualRoles: [{ roleId, code, nameEn, nameAr,
    isActive, virtualKind }], effective: [{ key, scope, sources: [{ kind, roleId, scope }] }], warnings: [code] }`.
    `sources[].kind` is `PermissionSourceKind`: `Direct = 1`, `Role = 2`, `VirtualRole = 3`, `BreakGlass = 4`
    (`roleId` is null for 1 and 4).
  - `warnings` is empty except `ACCESS_ONLY_BREAK_GLASS_MANAGES` after a confirmed last-manager write.
  - Refusals (`AccessErrorCodes`): 400 `VALIDATION_FAILED`, `ACCESS_UNKNOWN_PERMISSION`, `ACCESS_UNKNOWN_ROLE`,
    `ACCESS_VIRTUAL_ROLE_NOT_ASSIGNABLE`; 403 `ACCESS_SUBJECT_REQUIRED`, `ACCESS_SCOPE_EXCEEDS_CALLER`; 404 `ACCESS_ROLE_NOT_FOUND`,
    `ACCESS_USER_NOT_FOUND`; 409 `ACCESS_ROLE_CODE_TAKEN`, `ACCESS_ROLE_NAME_TAKEN`, `ACCESS_BREAK_GLASS_NOT_EDITABLE`,
    `ACCESS_LAST_MANAGER`.

Layers are namespaces (ADR-004):

| Layer | Contents |
| --- | --- |
| `Domain` | `PermissionEntry`, `Role`, `RolePermissionEntry`, `UserRoleAssignment`, `UserPermissionGrant`, `VirtualRoleKind`, `RoleCodeFormat`, `AccessLimits` |
| `Application` | keys, catalog, id families (`RoleIds`, `UserIds`), ports, `PermissionResolution`, resolver, organization scope, `ManagerGraph` and the guard, commands and validators, use cases, `AccessChanges`, `AccessErrorCodes` |
| `Infrastructure` | EF Core (context, configurations, repositories, the two readers, migrations), the handler, the startup task, the registration |
| `Api` | thin controllers and response records |

The database schema is `access`: `Permissions`, `UserPermissionGrants`, `Roles`, `RolePermissions` and
`UserRoleAssignments`, created by `InitialCreate` and `RolesAndGrants`.

## Use

Guard an endpoint with a key constant, never a literal:

```csharp
[HasPermission(AccessPermissionKeys.DashboardView)]
public sealed class DashboardController(GetDashboard getDashboard) : ControllerBase { ... }
```

An endpoint that any signed-in user may call carries `[AuthenticatedUser]`, and its route is listed in
`backend/tests/Starter.Contract.Tests/EndpointAllowlist.cs`.

Narrow a query to the caller's organization through the kernel port (never by reading this module):

```csharp
var decision = await organizationScope.ScopeForAsync(RecordsPermissionKeys.View, cancellationToken);
var filter = decision.Narrow(requestedOrganizationId);           // a list: MatchesNothing, or the organization to filter on
if (filter.MatchesNothing) return EmptyPage;
if (!decision.Reaches(record.OrganizationId)) return Refusal.NotFound(RecordsErrorCodes.RecordNotFound); // one record
```

`ScopeDecision.Reaches` and `ScopeDecision.Narrow` are the one owner of the rule: a global decision reaches every
record; an own-organization decision only its organization's records, and nothing when the user has no organization;
a denied decision reaches nothing. Answer 404 for one record outside the scope (do not reveal it exists) and 403 for a
write that would place a record outside it.

To grant keys from another module (for example account seeding), inject the kernel port:

```csharp
await grants.EnsureGrantedAsync(userId, [AccessPermissionKeys.DashboardView], cancellationToken);
```

The call is idempotent and grants at `Global` scope. It throws `InvalidOperationException` for a key that is not in
the catalog, or when the catalog has not been synchronized yet. Account seeding runs in the `Accounts` phase, after
`ReferenceData`, so the catalog is always there in a normal start.

The frontend reads `GET /api/me/permissions` after sign-in and again on focus, navigation and an interval, so a grant
or a revocation shows without relogin. It gates routes and buttons with the generated `PermissionKeys`
(`npm run api:gen` reads `docs/generated/permissions.json`) and builds the permission tree from the dotted key
segments of `GET /api/admin/access/permissions`.

## Extend

- **A permission in another module.** Add a constant to that module's `<Name>PermissionKeys`, then list it in the
  module's `IPermissionCatalogSource`. Register the source in the module's `Add<Name>Module` with
  `services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, <Name>PermissionCatalogSource>())`.
  The scaffolder (`node tools/scaffold/new-permission.mjs --module <Name> --action <kebab>`) does all of this. Then
  regenerate the docs and the client.
- **A permission in this module.** Add it to `AccessPermissionKeys` and `AccessPermissionCatalogSource`. The
  synchronizer writes it on the next start; no migration is needed.
- **A virtual role kind.** Add a member with the next explicit value to `VirtualRoleKind`, its matching rule to
  `VirtualRoleKinds.Reaches`, and a NEW migration that inserts its role row (code, names, active, kind). Never edit
  `VirtualRoleSeed` or `RolesAndGrants`: they are frozen with the migration.
- **A schema change.** Change the entity and its configuration, then run:

  ```powershell
  $env:DOTNET_ROOT = 'E:\codelab-tools\dotnet10'
  dotnet-ef migrations add <Name> --project backend/src/Modules/Access/Starter.Modules.Access.csproj --context AccessDbContext --output-dir Infrastructure/Persistence/Migrations
  ```

  First freeze the current `AccessMigrationModel` as `<Previous>Model` (as `InitialCreateModel` was) and point the
  previous migration's `BuildTargetModel` at it. Then reshape the output to the clean-code rules (see Gotchas),
  delete the generated `*.Designer.cs` and `AccessDbContextModelSnapshot.cs`, and update `AccessMigrationModel` so
  `dotnet-ef migrations has-pending-model-changes` reports no changes (`AccessModelTests` checks it too).
- **A real audit trail (phase 5).** Replace the log line in `AccessChanges.Record` with the audit port; the use cases
  already pass every change through `AccessChanges.CommitAsync`.

## Rules

- **Read users only through the kernel ports.** `IUserDirectory` (by token subject, for the resolve) and
  `IAccountAdministration` (by id, for the editor, the picker and the guard), both implemented by Identity. Never
  reference the Identity or Users module or their tables (`mustNotDependOn: [Identity, Users]`, plus the content
  rule `access.no-identity-reference`). User ids are plain `long` columns with no cross-schema foreign key.
- **Resolve live; never bump sessions for a grant change.** The resolver has no cache, so a grant, a revocation, a
  role edit, a role deactivation or a virtual-role edit applies on the very next request: the menu updates on the next
  permission refresh and a revoked endpoint answers 403 (not 401). Grant and role writes do NOT bump the user's session
  version (only Identity's account writes do). Adding a cache needs invalidation on every write in this module and on
  every account state change.
- **Catalog rows come only from the synchronizer.** The code is the catalog. A retired key keeps its row, so old grants
  keep their foreign key, but the resolver ignores it and the editors refuse to grant it (`ACCESS_UNKNOWN_PERMISSION`).
- **Virtual roles come only from migrations** and are never assigned (`ACCESS_VIRTUAL_ROLE_NOT_ASSIGNABLE`).
- **The self-lockout guard (exact rule).** A *manager* is an active account, other than break-glass, that holds BOTH
  `access.roles.manage` and `access.grants.manage` (any scope) through direct grants, active assigned roles and active
  virtual roles of its kind. A role write (permissions, deactivation) or a grant write that takes the number of
  managers from one or more to zero is refused with 409 `ACCESS_LAST_MANAGER`. The caller may resend it with
  `confirmBreakGlassOnly: true`; it is then allowed, with the warning `ACCESS_ONLY_BREAK_GLASS_MANAGES` and a warning
  log line, only while an active break-glass account exists (Identity requires one at every start). A write that
  changes no manager key, or a world that had no manager before it (bootstrapping by break-glass), is always allowed.
- **The break-glass account's grants are not edited** (`ACCESS_BREAK_GLASS_NOT_EDITABLE`): it holds everything.
- **The grant ceiling (exact rule).** A user grant write is judged only on the entries it adds or widens; an entry the
  account already holds at that scope stays, whoever granted it, and removing or narrowing is always allowed. A
  caller never adds or widens their own grants. Otherwise each added or widened role is assigned at most as widely as
  the caller holds `access.grants.manage`, and each added or widened direct grant needs the caller to cover the key at
  that scope (`PermissionCoverage`, the rule `AdministrationGuard` also uses: held globally, or held at
  own-organization scope for an own-organization grant in the caller's organization). Anything else is refused with
  403 `ACCESS_SCOPE_EXCEEDS_CALLER` and nothing is written.
- **Thin controllers.** Each binds, is guarded per action, calls one use case and maps the `Outcome` with
  `OutcomeResults` (or `AccessResults.ToCreatedResult` for 201). Refusals use `AccessErrorCodes`; the client renders
  `errors.<CODE>`.
- **Fail open nowhere.** The handler never calls `context.Fail()`, and it never succeeds without the key:
  - an unauthenticated caller gets 401 from the challenge;
  - an authenticated caller without the key, without a readable subject or with an inactive account gets 403.

## Gotchas

- **The audit trail is a log line until phase 5.** `AccessChanges` logs event 3110 (kind, target id, counts) after
  each committed write and 3111 (warning) for a confirmed break-glass-only write. Internal ids only, no names.
- **Account deactivation is guarded through the kernel.** The Users module asks `IAdministrationGuard.IsLastAccessManagerAsync`
  before it deactivates an account and refuses the last manager (`USERS_LAST_ACCESS_MANAGER`); there is no
  break-glass confirmation on that path. A module that switches accounts off another way must ask the same port.
- **The ceiling does not look inside roles.** An own-organization grants manager may assign, at own-organization
  scope, a role whose permissions they do not hold themselves. Keep `access.roles.manage` (who defines what a role
  grants) with people you trust for every role they can assign.
- **The break-glass check scans accounts.** `IAccountAdministration` has no break-glass filter, so the guard pages
  through active accounts (`BreakGlassScanPageSize` x `BreakGlassScanMaxPages`) and fails closed (refuses) beyond.
  It only runs on a confirmed last-manager write.
- **The migrations are reshaped by hand.** `dotnet-ef` writes a `*.Designer.cs` file and an `<auto-generated>` header,
  and both are banned by the base rules (`base.no-generated-file-name`, `base.no-auto-generated-marker`). The
  generated `Up` method is also longer than the method limit and uses bare lengths. So each migration file holds the
  migration, `[DbContext]` and `[Migration]` included; `AccessMigrationIds` holds the ids and constraint names;
  `AccessMigrationModel` holds the latest model and the `AccessDbContextModelSnapshot`; earlier target models are
  frozen copies (`InitialCreateModel`).
- **Pass `--output-dir` and do not pass `--namespace` to the EF 9 tool.** With `--namespace`, the 9.0 tool writes the
  snapshot into a folder tree named after the namespace. The tool also writes a second snapshot file: delete it.
- **Type names avoid the `Permission` suffix** (`PermissionEntry`, `RolePermissionEntry`, `EffectiveGrant`):
  CA1711 refuses type names that end in `Permission`.
- **`AccessLimits.PermissionKeyMaxLength` mirrors `PermissionKeyFormat.MaxLength`.** A unit test keeps the two equal.
- **The internal member of `UserKind`, written as a plain member access, trips the scrub host scanner** (it reads as
  a host name under the internal suffix). Write `UserKind.@Internal` until the scanner fix lands.
- **Three id families share the label `users`** (`UserIds` here, and one each in Identity and Users) so their tokens
  are interchangeable; a kernel-owned family would remove the copies.
- **The placeholder use cases read placeholder stores.** `GetDashboard` and `GetAccessAdminOverview` take a port; the
  adapters in `Infrastructure/PlaceholderStores.cs` return empty bodies. Replace the adapter when data arrives.
- **`EnsureGrantedAsync` before the catalog is synchronized throws.** This happens only when a startup task is
  registered in the wrong phase. Seeding belongs in `StartupPhase.Accounts`.
