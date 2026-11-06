# deploy/sql/audit

## Purpose

DBA scripts that make the audit trail append-only in every environment outside Development. The application writes
`audit.AuditEntries` (the kernel `AuditSaveChangesInterceptor`) and the record history tables (`RecordHistoryEntry`
subtypes, mapped with `HasRecordHistory`), but it must never be able to rewrite or erase them.

| Script | What it does |
| --- | --- |
| `Grant-AuditAppendOnly.sql` | GRANT SELECT, INSERT and DENY UPDATE, DELETE to the application's database user on `audit.AuditEntries` and on every user table whose name ends in `History` (EF migrations history tables excluded), plus DENY UPDATE, DELETE on `SCHEMA::audit`. Lists the resulting permissions and warns when the user is a `db_owner` member. |

## Use

1. Apply the audit migration first (the `audit` schema and `audit.AuditEntries` come from the release's migration
   script, like every module schema).
2. Open `Grant-AuditAppendOnly.sql`, set the application's database user on the line marked `SET ME` (batch 1).
3. Run the whole file in SSMS or with sqlcmd against the application database:

   ```powershell
   sqlcmd -S <server> -d <database> -E -b -i deploy\sql\audit\Grant-AuditAppendOnly.sql
   ```

4. Check the permission list it prints: every table shows GRANT INSERT, GRANT SELECT, DENY DELETE, DENY UPDATE.

Re-run it after every release that adds a record history table. The script is idempotent (GO-separated batches, each
table statement through `sp_executesql`) and stops with an error when the user or `audit.AuditEntries` is missing.

## Extend

- **A record history table** named `<Record>History` is picked up automatically. A history table with another name
  goes into the explicit list in batch 2.
- **Another append-only schema**: add a `DENY UPDATE, DELETE ON SCHEMA::<schema>` line to batch 4.

## Rules

- The application login is never `db_owner` or `sysadmin`: DENY does not bind them (the script warns).
- Purging or re-masking audit rows is a DBA operation under a separate, audited account, never the application's.
- Scripts here are reviewed by the platform owner (`deploy/**`).

## Gotchas

- `__EFMigrationsHistory` ends in `History` too; the script excludes it by name, so migrations keep working.
- A DENY on the table wins over `db_datawriter` membership, so the user can keep that role for the other tables.
- The script was proven on a throwaway LocalDB database: inserts and reads succeed for the user, UPDATE on
  `audit.AuditEntries` and DELETE on a history table are refused, and a second run changes nothing.
