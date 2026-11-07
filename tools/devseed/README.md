# tools/devseed

## Purpose

Development-only seed data generated on the developer's machine, never committed: the local secrets files
(`New-LocalSecrets.ps1`, init Step 3), the local database with its break-glass administrator
(`Initialize-LocalDatabase.ps1`, init Step 4), the fixture-key check (`Test-NoFixtureKeys.mjs`) and a sample
user-import workbook ([user-import/](user-import/README.md)). The API assembly references none of it.

## Use

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\devseed\New-LocalSecrets.ps1          # write or complete
powershell -NoProfile -ExecutionPolicy Bypass -File tools\devseed\New-LocalSecrets.ps1 -Force   # regenerate every file
powershell -NoProfile -ExecutionPolicy Bypass -File tools\devseed\New-LocalSecrets.ps1 -SkipService attachments,ops   # removed modules
# Every DbContext migration + break-glass admin; prints a one-time password once; rerun on an initialized DB skips
powershell -NoProfile -ExecutionPolicy Bypass -File tools\devseed\Initialize-LocalDatabase.ps1 -ConnectionString "Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true" -DatabaseName StarterDev
node tools/devseed/Test-NoFixtureKeys.mjs <path>   # exit 1 on a TEST-ONLY fixture key, fixture file or test protocol prefix in appsettings
```

`run-local.ps1` runs `New-LocalSecrets.ps1` for you when a file or a key is missing. It writes up to four gitignored
files (`appsettings.*.local.json`), loaded in Development only; `-SkipService attachments,sms,ops,gateway` leaves out
removed services (and the API's trust for them):

| File | Holds |
| --- | --- |
| `backend/src/Starter.Api/appsettings.Development.local.json` | break-glass and Development user passwords, session signing key, the PublicId, request-signing, API ticket (`Codelab:Security:Tickets`: SSO and activation, never shared), upload-ticket (`Codelab:Security:UploadTickets`) and receipt (`Codelab:Security:Uploads`) key rings, the S2S client key (`Codelab:Security:ServiceAssertions:Client`, ES256 PKCS#8 PEM, `starter-api` calling `starter-attachments`), the user-hash key |
| `services/attachment-service/src/Codelab.Attachments.Api/appsettings.Development.local.json` | the API's upload-ticket ring as its only ticket ring (`Codelab:Security:Tickets`), the same receipt ring, `Codelab:Security:ServiceAssertions:Server` (audience `starter-attachments`, trusting `starter-api` with the public half of the API's client key), `Attachments:Storage:RootPath` (`%LOCALAPPDATA%\codelab-starter\attachments`, created) and `Attachments:Cors:AllowedOrigins` (`http://localhost:4280`) |
| `services/integration-gateway/src/Codelab.Gateway.Api/appsettings.Development.local.json` | `Codelab:Security:ServiceAssertions:Server` (audience `integration-gateway`, trusting `starter-api` with the public half of the API's client key: one API key, two audiences) |
| `services/ops-console/src/Codelab.OpsConsole.Api/appsettings.Development.local.json` | the Ops Console API's own ES256 client key (`starter-ops-console` calling `starter-api`); the API file then holds `Codelab:Security:ServiceAssertions:Server` trusting its public half and `Codelab:Ops:TrustedCallerKeyId` |

Exit codes (all three tools): 0 done, complete or skipped; 1 refused (New-LocalSecrets: a file is not a JSON object or
a client key is not P-256; Initialize-LocalDatabase: a server not on this machine, no secrets file, a failed build or
start; Test-NoFixtureKeys: hits); 2 usage. Tests: `node --test "tools/devseed/**/*.test.mjs"` (set
`CODELAB_DEVSEED_SQL` to a throwaway local server to include the end-to-end database run).

`Initialize-LocalDatabase.ps1` uses the API's own Development path (no second migration or seed code): it builds the
API to `%TEMP%` (never its `bin`, so a running API is untouched), creates the empty database, starts the API once on a
free loopback port with `Database:ApplyMigrationsOnStartup=true` and a fresh `Identity:BreakGlass:Password`, waits for
`/health/live`, stops only that process, saves the password in the local secrets file (the configured source; the
seeder re-applies it on every start) and prints it once. It also saves the connection string as
`ConnectionStrings:Starter` in that file (after a run and on the already-initialized skip), so the API uses the init
database, and deletes the temporary copy of the secrets file in the build output once the API has stopped.

## Extend

- A new secret: add it to `New-ApiDocument` (or `New-ServiceDocument` when the service needs it, derived from the API
  document when both sides must agree). Existing files get it on the next run without `-Force`, because every run
  adds the keys a file lacks.
- A new service that shares a ring with the API: copy the ring from the final API document, never generate a second
  one, and add the pair (API section, service section) to `$SharedRings` so a mismatch is reported.
- Never give a service the API's own ticket ring (`Codelab:Security:Tickets`): it seals SSO sign-in cookies, SSO
  exchange tickets (refresh tokens) and activation tickets, and a holder of the key can open and forge them (the
  purpose binding does not stop a key holder). Give the service a dedicated ring for its one ticket purpose.

## Rules

- Every value is generated at run time from a CSPRNG (or a fresh CNG P-256 key); nothing is copied from a fixture,
  and the Codelab.Security key guards refuse published material.
- Existing values are never changed without `-Force`; unknown settings in a file are kept.
- It prints paths, added key paths and the break-glass user name; never a password, a key or a PEM.

## Gotchas

- The API's `UploadTickets` ring must equal the service's `Tickets` ring, and the receipt rings must be identical (the
  API seals upload tickets the service opens; the service signs receipts the API verifies). A service file from
  before the dedicated ring holds the API's own `Tickets` ring: the script warns that the service could open SSO and
  activation tickets; run it with `-Force`. Hand-editing one file breaks the pair: the script warns, it does not repair. Run
  it with `-Force` to regenerate both files (existing sessions, PublicIds, tickets and receipts stop working).
- Windows PowerShell 5.1 has no PEM export; the S2S key pair is made with CNG (`Pkcs8PrivateBlob`) and the public key
  is encoded as a P-256 `SubjectPublicKeyInfo`.
- When a service folder is absent (a fork removed it), its file is skipped with a warning.
- The Identity module has no must-change-password flag yet, so the one-time break-glass password is not forced to
  change at first sign-in.
- The API cannot start on a missing database (the Codelab.Security SQL stores prepare their schema before the
  migrations run), so `Initialize-LocalDatabase.ps1` creates the empty database first.
- `Test-NoFixtureKeys.mjs` reads digests of the fixture keys (never the keys) from
  `packages/.../published-material.fixtures.json`, or from its copy `fixture-key-digests.json` in a fork without
  `packages/`; after `tools/vectors/fixture-keys.mjs generate`, rerun it with `--refresh-digests`.
