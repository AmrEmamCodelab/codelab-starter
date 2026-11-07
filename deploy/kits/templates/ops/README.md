# Ops Console kit: settings an operator fills

The Ops Console is **optional**. A fork that does not ship it removes `Install-Ops.ps1` and this folder (init's
"Ops Console" option).

`Install-Ops.ps1` installs two child applications under an **existing** site (no new site, binding or certificate):

| Application | Default path | Pool | What it is |
| --- | --- | --- | --- |
| Console API | `/ops-api` | `StarterOpsApi` | The console's server side (BFF). **Windows Authentication on, Anonymous off.** It checks the operator's AD group, and it is the only caller of the API's `/api/ops/*` routes, with a Codelab.Security ES256 service assertion. |
| Console screens | `/ops` | `StarterOpsWeb` | The built screens (static files). The build's `index.html` must carry `<base href="/ops/">`. |

The kit merges `appsettings.template.json` into the console API's `<ApiTargetPath>\appsettings.Production.json`. Like
every kit on `Install-Common.psm1`, it **only adds keys the server file does not have yet** and never overwrites a value
(see [../../MODULE.md](../../MODULE.md)). It then checks the keys in `required-settings.json`. Each one that is
missing, empty or malformed is a **gap** (reported by path, never by value). With a gap the kit **refuses to start the
console API**: it leaves `app_offline.htm` in place, listing the gaps, and exits with code 2. Fill the gaps and run the
kit again.

The template holds no secrets and no environment values. JSON has no comments, so the keys are documented here.

| Key | What to put there | Must match |
| --- | --- | --- |
| `Codelab:OpsConsole:Api:BaseUrl` | The API's absolute **https** base URL as this server reaches it (the internal API site, never the public front site: the front site answers 404 for `/api/ops/*`). | the API site binding |
| `Codelab:OpsConsole:OperatorGroup` | The AD group whose members may use the console (`DOMAIN\Group`). Anyone else gets 403. | an AD group the ops team owns |
| `Codelab:OpsConsole:Seq:BaseUrl` | The Seq server's absolute **https** URL (reference-code lookups link to it). | `deploy/seq` install |
| `Codelab:OpsConsole:Seq:ApiKey` | A **Read-only** Seq API key, created once by `deploy/seq/apply-seq-config.ps1 -IncludeReadKeys` (the token is printed once). | the Seq read key titled `Ops: read ops-console` |
| `Codelab:Security:ServiceAssertions:Client:KeyId`, `PrivateKeyPem` | The console's S2S (ES256, P-256) **private** key and its key id. | the API's `Codelab:Security:ServiceAssertions:Server:TrustedServices[]` entry for `starter-ops-console` (public key) and `Codelab:Ops:TrustedCallerKeyId` |
| `Codelab:Security:ServiceAssertions:Client:ServiceName`, `Audience` | Pre-filled (`starter-ops-console`, `starter-api`). Change them only together with the API's trusted-service entry and audience. | the API settings |

Key material is generated per environment (see the key-rotation runbook). It is never copied from another environment
and never committed. Restrict `appsettings.Production.json` to administrators and the `IIS AppPool\StarterOpsApi`
identity.

## Required settings (`required-settings.json`)

Same format as the gateway kit's file (see [../gateway/README.md](../gateway/README.md)):
`path`, `format` (`1` any non-empty value, `2` an absolute https URL) and an optional `when`.

## Health

After a run without gaps the kit probes, over https with normal certificate validation (there is no `-k`):

- `GET <HealthBaseUrl>/ops-api/health/ready`, signed in as the account running the kit (Windows Authentication).
- `GET <HealthBaseUrl>/ops/`, which must return the console's `index.html` (its `<base href>`), not the site's own
  fallback page.

`-HealthBaseUrl` must be https and use a name the site's certificate covers.
