# Attachment service kit: settings an operator fills

`Install-Attachment.ps1` merges `appsettings.Production.template.json` into the server's
`<TargetPath>\appsettings.Production.json`. Like every kit on `Install-Common.psm1`, it **only adds keys the server file
does not have yet** and never overwrites a value (see [../../MODULE.md](../../MODULE.md)). On a first install the file
is created from the template with every operator value empty. The kit then lists the keys that are still empty
(their paths only, never their values) and skips the health probe, because the service refuses to start until they
are filled.

The template holds no secrets and no environment values. JSON has no comments, so the keys are documented here.

| Key | What to put there | Must match |
| --- | --- | --- |
| `Attachments:Storage:RootPath` | The attachment store: a fully qualified local folder or a UNC share, **outside** the app folder. The kit fills it from `-StorageRoot` when the key is missing. | none |
| `Attachments:Cors:AllowedOrigins` | The browser origins that upload directly to the service, for example the portal's `https://<host>` (scheme + host + port, no trailing slash). | the web front end's public origin |
| `Codelab:Security:Tickets:ActiveKeyId`, `Keys[]` (`KeyId`, `MaterialBase64`) | The sealed-ticket key ring. Upload tickets are sealed by the API and opened here. | **the API's upload-ticket ring** (`Codelab:Security:UploadTickets` in the API settings). Never the API's own `Codelab:Security:Tickets` ring (SSO, activation): with it every upload is refused, and a compromised service could open SSO and activation tickets. |
| `Codelab:Security:Uploads:ActiveKeyId`, `Keys[]` (`KeyId`, `MaterialBase64`) | The upload-receipt key ring. The service signs receipts and the API verifies them when it links a file. | **the same ring as the API** (`Codelab:Security:Uploads` in the API settings) |
| `Codelab:Security:ServiceAssertions:Server:TrustedServices[]` (`KeyId`, `PublicKeyPem`) | The API's S2S (ES256, P-256) **public** key and its key id. The API holds the private key in `Codelab:Security:ServiceAssertions:Client`. | the API's `ServiceAssertions:Client:KeyId` and private key |
| `Codelab:Security:ServiceAssertions:Server:Audience` | Pre-filled (`starter-attachments`). Change it only together with the API's `ServiceAssertions:Client:Audience`. | the API client audience |

Key material is generated per environment (the key-rotation runbook arrives in phase 8). It is never copied from
another environment and never committed. Restrict `appsettings.Production.json` to administrators and the app pool
identity.

After filling the keys, recycle the app pool (or run the kit again; a second run changes no settings) and check
`GET <site><AppPath>/health/ready` (the readiness route; `/health` alone is not mapped and answers 401).

## Storage access

For a **local folder**, the kit gives `IIS AppPool\<AppPoolName>` Modify on the root, inherited by subfolders and
files. For a **UNC share**, the app pool identity reaches the share as the server's computer account
(`<DOMAIN>\<SERVER>$`), or as the pool's custom identity if one is set. The share owner grants that account Modify;
the kit only reports this as an operator step.
