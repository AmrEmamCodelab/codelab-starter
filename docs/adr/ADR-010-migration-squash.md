# ADR-010: Migration squash at v1.0

- **Status:** Accepted
- **Date:** 2026-11-07
- **Deciders:** Amr Emam (platform owner)

## Context

Before v1.0 the starter grew a migration chain per DbContext: seed migrations (lookups, notification templates, attachment types) and follow-up schema migrations. Every fork would inherit and replay that history. The owner's demo database has already applied the chain, so the squash must not invalidate it.

## Decision

- At v1.0 each DbContext has exactly one migration, the class `InitialCreate`. The later schema changes and every seed (lookup types and values, notification templates, attachment types) are folded into it. The permission catalog is **not** a migration seed: `InitialCreate` for Access creates the tables and the virtual roles only, and `PermissionSynchronizer` (Access module, Application layer) fills the catalog from the code-declared permissions at startup. This differs from the build plan ("permission catalog seed included") and needs platform-owner approval as a deviation.
- `InitialCreate` keeps its original migration id. A database that applied the old chain already has that id in its history, so EF applies nothing. The history rows of the folded migrations are left alone, because EF ignores ids it does not know.
- After v1.0 the chain is **never squashed again**, because forks will have applied databases. `/new-lookup`, any new seed and every upstream release add normal incremental migrations.
- `backend/tests/Starter.Integration.Tests/MigrationSquash` enforces this rule. `SingleInitialCreateTests` checks every DbContext by reflection. When v1.0 is tagged, set `SquashedMigrations.ReleasedThroughMigrationId` to the last shipped id. From then on, only migrations newer than that id are allowed. `FreshDatabaseSeedTests` builds a new SQL Server database from the migrations and checks every seed.

## Consequences

- A fork starts with one readable migration per module, and a new database gets all reference data from the migrations alone.
- Tests that asserted the old migration lists were updated in the same change.
- Wording or values changed after v1.0 need a new migration. Editing `InitialCreate` or its seed classes is never allowed.
