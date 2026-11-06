# tools/devseed

## Purpose

Development-only seed data generated on the developer's machine, never committed: the local secrets files
(`New-LocalSecrets.ps1`) and a sample user-import workbook ([user-import/](user-import/README.md)).

## Use

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\devseed\New-LocalSecrets.ps1          # write or complete
powershell -NoProfile -ExecutionPolicy Bypass -File tools\devseed\New-LocalSecrets.ps1 -Force   # regenerate both
```

`run-local.ps1` runs it for you when a file or a key is missing. It writes two gitignored files
(`appsettings.*.local.json`), loaded in Development only:

| File | Holds |
| --- | --- |
| `backend/src/Starter.Api/appsettings.Development.local.json` | break-glass and Development user passwords, session signing key, the PublicId, request-signing, API ticket (`Codelab:Security:Tickets`: SSO and activation, never shared), upload-ticket (`Codelab:Security:UploadTickets`) and receipt (`Codelab:Security:Uploads`) key rings, the S2S client key (`Codelab:Security:ServiceAssertions:Client`, ES256 PKCS#8 PEM, `starter-api` calling `starter-attachments`), the user-hash key |
| `services/attachment-service/src/Codelab.Attachments.Api/appsettings.Development.local.json` | the API's upload-ticket ring as its only ticket ring (`Codelab:Security:Tickets`), the same receipt ring, `Codelab:Security:ServiceAssertions:Server` (audience `starter-attachments`, trusting `starter-api` with the public half of the API's client key), `Attachments:Storage:RootPath` (`%LOCALAPPDATA%\codelab-starter\attachments`, created) and `Attachments:Cors:AllowedOrigins` (`http://localhost:4280`) |

Exit codes: 0 written, completed or already complete; 1 refused (an existing file is not a JSON object, or the API's
client key is not a PKCS#8 P-256 key); 2 usage. Tests: `node --test tools/devseed/tests`.

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
- When the Attachment service folder is absent (a fork removed it), only the API file is written, with a warning.
