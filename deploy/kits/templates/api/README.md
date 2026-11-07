# API kit: settings an operator fills

`Install-Api.ps1` merges `appsettings.template.json` into the server's `<TargetPath>\appsettings.Production.json`
(and the package's `appsettings.json` into the server's `appsettings.json`). Like every kit on `Install-Common.psm1`,
it **only adds keys the server file does not have yet** and never overwrites a value (see
[../../MODULE.md](../../MODULE.md)). On a first install the Production file is created from the template with every
operator value empty.

The kit then checks the effective settings (package `appsettings.json` < server `appsettings.json` < this template <
server `appsettings.Production.json` < the app pool's environment variables, the order .NET reads them) and lists
every gap by path and reason, never by value. **While a gap remains the API is not started**: `app_offline.htm` stays
in the folder (a "not configured" page), the health probe is skipped and the kit ends with exit code 2. Fill the
gaps and run the kit again; a second run changes no settings and brings the API online.

The template holds no secrets and no environment values. JSON has no comments, so the keys are documented here.

## Always required

| Key | What to put there | Gap reasons |
| --- | --- | --- |
| `ConnectionStrings:Starter` | The SQL Server connection string of the application login (not `db_owner`). | Missing, Empty |
| `Auth:WebBaseUrl` | The web front end's public origin, `https://<host>`. | NotHttps |
| `Users:Activation:LinkBaseUrl` | The origin activation links point at (usually the same as `Auth:WebBaseUrl`). | NotHttps |
| `Attachments:ServiceBaseUrl` | The attachment service root the browser and the API both reach, `https://...`. | NotHttps |
| `Portal:Contact:InboxEmail` | The contact-us inbox. An `example.*` address (the package default) is a placeholder. | Placeholder |
| `Notifications:Email:FromAddress` | The sender address of outgoing mail. An `example.*` address is a placeholder. | Placeholder |
| `Notifications:Email:Smtp:Host` | The internal SMTP relay. | Empty |
| `Identity:Jwt:SigningKey` | Access-token signing key: base64 of at least 32 random bytes. | Empty |
| `Identity:BreakGlass:Password` | The break-glass administrator's password (strong; kept in the vault). | Empty |
| `Observability:UserIdHash:Key` | The per-environment key that hashes user ids in logs. | Empty |
| `Codelab:Security:Cors:AllowedOrigins` | The browser origins allowed to call the API (`https://<host>`, no trailing slash). | Missing, NotHttps |
| `Codelab:Security:PublicIds` ring | `ActiveKeyId` + `Keys[]` (`KeyId`, `MaterialBase64`): the PublicId key ring. | Empty, ActiveKeyNotInRing |
| `Codelab:Security:RequestSigning` ring | The request and response signing ring. | Empty, ActiveKeyNotInRing |
| `Codelab:Security:Tickets` ring (+ `Enabled` true) | The API's own sealed-ticket ring (SSO sign-in, activation). Never given to another service. | NotEnabled, Empty |
| `Codelab:Security:UploadTickets` ring (+ `Enabled` true) | The upload-ticket ring. **The same ring** goes to the attachment service as its `Codelab:Security:Tickets`. | NotEnabled, Empty |
| `Codelab:Security:Uploads` ring (+ `ReceiptsEnabled` true) | The upload-receipt ring, **the same as** the attachment service's `Codelab:Security:Uploads`. | NotEnabled, Empty |
| `Codelab:Security:ServiceAssertions:Client:KeyId`, `PrivateKeyPem` | The API's S2S (ES256, P-256, PKCS#8) key. Its public half goes to the attachment service and the gateway as a trusted service. `ServiceName` and `Audience` come from the package. | Empty |

Every ring's material is its own: the kit reports `MaterialSharedAcrossRings` when two rings hold the same material
(for example the upload-ticket ring reusing the API's own ticket ring).

## Required when a feature is on

| When | Then required |
| --- | --- |
| `Codelab:Identity:Sso:Enabled` is true | `Codelab:Identity:Sso:Authority`, `ClientId`, `AuthorizationEndpoint`, `TokenEndpoint`, `RedirectUri` (https). |
| SSO is on and `Codelab:Identity:Sso:BackChannel` is `2` (GatewayRelay, the template default: the API host has no egress) | `Codelab:Integration:Gateway:BaseUrl` (https). Set `BackChannel` to `1` (Direct) only where the API may reach the identity provider itself. |
| `Notifications:Sms:Enabled` is true and `Notifications:Sms:Mode` is `2` (Gateway) | `Codelab:Integration:Gateway:BaseUrl` (https). |
| `Codelab:Identity:ActiveDirectory:Enabled` is true | `Codelab:Identity:ActiveDirectory:Host`, `BaseDn`, `Domain`. |
| The package carries a `Codelab:Ops` section (the Ops module is in the build) | `Codelab:Ops:TrustedCallerKeyId`, `Codelab:Security:ServiceAssertions:Server:Audience` and `Server:TrustedServices[]` (`ServiceName`, `KeyId`, `PublicKeyPem` of the Ops Console API). |

`Database:ApplyMigrationsOnStartup` is set to `false`: the DBA applies the release's scripts
([../../../sql/README.md](../../../sql/README.md)) before the first server is updated.

## Where values may live

A value may be in `appsettings.Production.json` or in an app pool environment variable (`Codelab__Security__...`,
`__` for `:`). The kit reads both (the environment wins, as in .NET); reading the pool's variables needs an elevated
shell. Restrict `appsettings.Production.json` to administrators and the app pool identity.

Key material is generated per environment and rotated with [../../../../docs/runbooks/key-rotation.md](../../../../docs/runbooks/key-rotation.md).
It is never copied from another environment and never committed.

## Inbound trace context

The API's `web.config` is package-owned (the publish output; the mirror replaces it every run), so the edge rule
that strips a caller's `traceparent`, `tracestate` and `baggage` lives in the **front site's** template
([../web/web.config](../web/web.config), rule `Strip inbound trace context` in `<location path="api">`): with the API
installed as the `/api` child application of that site (the default topology), the rule covers every request from
the internet. A request through the API's own binding (the Ops Console API, the gateway) is not stripped at the
edge; the API itself still ignores an inbound trace context (Codelab.Observability), so it never becomes the trace
or the reference code.
