# Access module

## Purpose

Who may do what. The Access module owns:

- **The permission catalog in code.** Each module declares its keys as constants of a `<Name>PermissionKeys` class
  and registers an `IPermissionCatalogSource`. `PermissionCatalog` (a singleton) is the union of every source, sorted
  by key. It is validated when it is built: a duplicate key, a key that breaks `PermissionKeyFormat` or a missing
  description stops the host at start.
- **The `[HasPermission]` handler.** `PermissionHandler` evaluates the kernel `HasPermissionAttribute` against the
  caller's live permissions. The access token carries identity only (`sub`, `sid`), never permissions.
- **The live per-user resolve.** `IUserPermissionResolver` reads the account through the kernel `IUserDirectory` port
  (implemented by Identity) and the grants from this module's tables, on every call:
  - an unknown or inactive user holds nothing;
  - a user with `HoldsAllPermissions` (break-glass) holds the whole catalog;
  - anyone else holds their direct grants that are still in the catalog.
- **Direct user grants.** `UserPermissionGrants` implements the kernel `IUserPermissionGrants`, which account seeding
  uses. The role and grant editor arrives in phase 3.
- **The catalog in the database.** `PermissionSynchronizer`, run by `PermissionCatalogStartupTask` in the
  `ReferenceData` startup phase, writes the catalog to `access.Permissions` idempotently. It inserts new keys,
  refreshes changed ones and retires (never deletes) keys that left the code.
- **Endpoints.**

  | Endpoint | Guard | Returns |
  | --- | --- | --- |
  | `GET /api/me/permissions` | `[AuthenticatedUser]` (allowlisted) | `{ permissions: [keys] }` |
  | `GET /api/dashboard` | `dashboard.view` | `{}` |
  | `GET /api/access/admin` | `access.admin.view` | `{}` (placeholder) |

Layers are namespaces (ADR-004):

| Layer | Contents |
| --- | --- |
| `Domain` | `PermissionEntry`, `UserPermissionGrant`, `AccessLimits` |
| `Application` | keys, catalog, ports, resolver, grants, synchronizer, use cases, `AccessErrorCodes` |
| `Infrastructure` | EF Core, the handler, the startup task, the registration |
| `Api` | controllers |

The database schema is `access`, with tables `Permissions` and `UserPermissionGrants`, created by one migration,
`InitialCreate`.

## Use

Guard an endpoint with a key constant, never a literal:

```csharp
[HasPermission(AccessPermissionKeys.DashboardView)]
public sealed class DashboardController(GetDashboard getDashboard) : ControllerBase { ... }
```

An endpoint that any signed-in user may call carries `[AuthenticatedUser]`, and its route is listed in
`backend/tests/Starter.Contract.Tests/EndpointAllowlist.cs`.

To grant keys from another module (for example account seeding), inject the kernel port:

```csharp
await grants.EnsureGrantedAsync(userId, [AccessPermissionKeys.DashboardView], cancellationToken);
```

The call is idempotent. It throws `InvalidOperationException` for a key that is not in the catalog, or when the
catalog has not been synchronized yet. Account seeding runs in the `Accounts` phase, after `ReferenceData`, so the
catalog is always there in a normal start.

The frontend reads `GET /api/me/permissions` after sign-in. It gates routes and buttons with the generated
`PermissionKeys` (`npm run api:gen` reads `docs/generated/permissions.json`).

## Extend

- **A permission in another module.** Add a constant to that module's `<Name>PermissionKeys`, then list it in the
  module's `IPermissionCatalogSource`. Register the source in the module's `Add<Name>Module` with
  `services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, <Name>PermissionCatalogSource>())`.
  The scaffolder (`node tools/scaffold/new-permission.mjs --module <Name> --action <kebab>`) does all of this. Then
  regenerate the docs and the client.
- **A permission in this module.** Add it to `AccessPermissionKeys` and `AccessPermissionCatalogSource`. The
  synchronizer writes it on the next start; no migration is needed.
- **A schema change.** Change the entity and its configuration, then run:

  ```powershell
  $env:DOTNET_ROOT = 'E:\codelab-tools\dotnet10'
  dotnet-ef migrations add <Name> --project backend/src/Modules/Access/Starter.Modules.Access.csproj --context AccessDbContext --output-dir Infrastructure/Persistence/Migrations
  ```

  Reshape the output to the clean-code rules, as `InitialCreate` was (see Gotchas). Then update
  `AccessMigrationModel` so the snapshot matches. `dotnet-ef migrations has-pending-model-changes` must report no
  changes.
- **Roles (phase 3).** Add them to the resolver as a union with the direct grants. Keep the resolve live.

## Rules

- **Read users only through `IUserDirectory`.** Never reference the Identity module or its tables (`mustNotDependOn:
  [Identity]`, plus the content rule `access.no-identity-reference`). User ids are plain `long` columns with no
  cross-schema foreign key.
- **Resolve live.** The resolver has no cache, so a grant, a revocation or a deactivation applies on the very next
  request. Adding a cache needs invalidation on every grant change and on every account state change.
- **Database rows come only from the synchronizer.** The code is the catalog. A retired key keeps its row, so old
  grants keep their foreign key, but the resolver ignores keys that are not in the catalog.
- **Thin controllers.** Each binds, is guarded, calls one use case and maps the `Outcome` with `OutcomeResults`.
  Refusals use `AccessErrorCodes`; the client renders `errors.<CODE>`.
- **Fail open nowhere.** The handler never calls `context.Fail()`, and it never succeeds without the key:
  - an unauthenticated caller gets 401 from the challenge;
  - an authenticated caller without the key, without a readable subject or with an inactive account gets 403.

## Gotchas

- **The migration is reshaped by hand.** `dotnet-ef` writes a `*.Designer.cs` file and an `<auto-generated>` header,
  and both are banned by the base rules (`base.no-generated-file-name`, `base.no-auto-generated-marker`). The
  generated `Up` method is also longer than the method limit and uses bare lengths. So `InitialCreate.cs` holds the
  migration, `[DbContext]` and `[Migration]` included. `AccessMigrationModel.cs` holds the target model and the
  `AccessDbContextModelSnapshot`, built from `AccessLimits` and `nameof`. A later migration must freeze its own
  target model before `AccessMigrationModel` changes.
- **Pass `--output-dir` and do not pass `--namespace` to the EF 9 tool.** With `--namespace`, the 9.0 tool writes the
  snapshot into a folder tree named after the namespace.
- **The entity is `PermissionEntry`, not `Permission`.** CA1711 refuses type names that end in `Permission`.
- **`AccessLimits.PermissionKeyMaxLength` mirrors `PermissionKeyFormat.MaxLength`.** The Domain layer does not
  reference the kernel Application layer, so the value is repeated. A unit test keeps the two equal.
- **The placeholder use cases read placeholder stores.** `GetDashboard` and `GetAccessAdminOverview` take a port
  (`IDashboardSummaryStore`, `IAccessAdminOverviewStore`) like every scaffolded use case; the phase 2 adapters in
  `Infrastructure/PlaceholderStores.cs` return empty bodies. Replace the adapter, not the use case, when data arrives.
- **`EnsureGrantedAsync` before the catalog is synchronized throws.** This happens only when a startup task is
  registered in the wrong phase. Seeding belongs in `StartupPhase.Accounts`.
