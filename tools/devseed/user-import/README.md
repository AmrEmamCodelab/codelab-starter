# tools/devseed/user-import

## Purpose

Writes the 3-row sample workbook for the Excel user import check (phase 3 exit: preview 3 rows, reject 1 with a
reason, create 2 accounts without a password, each with an activation link and a Pending outbox row). The workbook is
built with System.IO.Compression only (inline strings, one sheet), so no spreadsheet program is needed and nothing is
committed: the file goes to the temp folder by default.

| Row | User name | Binding | Expected |
| --- | --- | --- | --- |
| 2 | `sample.one.<suffix>` | password | valid: account created, activation link, Pending `account-activation` outbox row |
| 3 | `sample.two.<suffix>` | password | valid: same |
| 4 | `sample.three.<suffix>` | password | rejected: `USERS_IMPORT_ROW_EMAIL_INVALID` (malformed e-mail address) |

There is no password column: the import never sets a password.

## Use

```powershell
powershell -NoProfile -File tools\devseed\user-import\New-SampleUserImport.ps1
# -Path <file.xlsx>        where to write it (default: %TEMP%\sample-users.xlsx)
# -Suffix <letters/digits> the user-name suffix (default: 4 random hex characters, so a rerun is not "taken")
# -OrganizationCode <code> make the rows external members of that existing, active, external organization
```

Then, signed in with `users.import`: admin, Users import, choose the file, Preview (2 valid, 1 rejected with its
reason), Commit (2 created with copyable activation links). The API needs `Users:Activation:LinkBaseUrl` and sealed
tickets enabled (`Codelab:Security:Tickets`), otherwise the commit answers `USERS_ACTIVATION_LINK_UNCONFIGURED` or
`IDENTITY_ACTIVATION_UNAVAILABLE`.

## Extend

The rows are in `New-SampleUserImport.ps1` (`New-SampleRow`). Keep the data neutral and under `example.test`. The
column names are the import template (`ImportColumns.Template` in the Users module); the import matches headers
ignoring case, spaces, `_` and `-`, so the order is free.

## Rules

- Never a real name, address or password in the sample; the generated file must pass `tools/extract/scrub-sweep.mjs`
  and `tools/extract/secret-scan.mjs` (both read inside .xlsx files).
- Never write the file inside the repository by default.

## Gotchas

- `Email` is the last column on purpose: the scrub sweep joins a row's cell texts, so a word in the next cell would
  extend the e-mail domain and the reserved `example.test` would no longer be recognised.
- Tests: `node --test "tools/devseed/user-import/**/*.test.mjs"` (runs the script with Windows PowerShell 5.1 and
  reads the zip back with node:zlib).
