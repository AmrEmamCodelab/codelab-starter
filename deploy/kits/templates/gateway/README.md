# Integration gateway kit: settings an operator fills

`Install-Gateway.ps1` merges `appsettings.template.json` into the server's `<TargetPath>\appsettings.Production.json`.
Like every kit on `Install-Common.psm1`, it **only adds keys the server file does not have yet** and never overwrites a
value (see [../../MODULE.md](../../MODULE.md)). On a first install the file is created from the template with every
operator value empty.

The kit then checks the keys listed in `required-settings.json` and the site's TLS certificate. Each one that is
missing, empty or malformed is a **gap** (reported by path, never by value). With a gap the kit **refuses to start the
gateway**: it leaves `app_offline.htm` in place, listing the gaps, and exits with code 2. Fill the gaps and run the kit
again; a run without gaps removes the page and probes `GET <site>/health/ready` over https.

The template holds no secrets and no environment values. JSON has no comments, so the keys are documented here.

| Key | What to put there | Must match |
| --- | --- | --- |
| `Codelab:Integration:Gateway:Profiles` | The list of enabled relay profile keys (`sso`, `sms`), exact lower case, for example `[ "sso", "sms" ]`. Ships as `[ "sso" ]`. A profile that is not listed answers 404 `GATEWAY_PROFILE_DISABLED`. | the profiles the API calls |
| `Gateway:Sso:TokenEndpoint`, `Gateway:Sso:JwksUri` | The OIDC provider's token endpoint and JWKS document: absolute **https** URLs, as reached from the DMZ. Required when `sso` is listed. | the provider's discovery document |
| `Gateway:Sms:UrlTemplate` | The SMS provider URL, absolute **https**, with the placeholders `{to}`, `{text}`, `{language}`, `{sender}` where the provider wants them. Required when `sms` is listed. | the client's SMS provider |
| `Gateway:Sms:BodyTemplate` | The POST body with the same placeholders (JSON: write them inside quotes). Required when `sms` is listed and `Gateway:Sms:Method` is `2` (POST, the default). A GET provider sets `Method` to `1` and leaves the body empty. | the SMS provider |
| `Gateway:Sms:AuthHeaderName`, `Gateway:Sms:AuthHeaderValue` | The provider credential header, both or neither. The value is a secret: prefer the environment variable `Gateway__Sms__AuthHeaderValue` on the app pool over the file. | the SMS provider account |
| `Gateway:Sms:Sender` | The sender id the provider registered, for `{sender}`. Optional. | the SMS provider account |
| `Codelab:Security:ServiceAssertions:Server:TrustedServices[]` (`ServiceName`, `KeyId`, `PublicKeyPem`) | The API's S2S (ES256, P-256) **public** key and its key id. The API holds the private key in `Codelab:Security:ServiceAssertions:Client`. | the API's `ServiceAssertions:Client:ServiceName`, `KeyId` and private key |
| `Codelab:Security:ServiceAssertions:Server:Audience` | Pre-filled (`integration-gateway`). Change it only together with the API's `Codelab:Integration:Gateway:Audience`. | the API's gateway client audience (`GatewayDefaults.Audience`) |

Other upstream settings (`Retry`, `Timeout`, `Certificate` of each section, the SMS `Method`, `BodyFormat`,
`LanguageCodes`, `AcceptedMember`, `MessageIdMember`) have defaults in the package `appsettings.json`; the gateway
host documents them ([services/integration-gateway/MODULE.md](../../../../services/integration-gateway/MODULE.md)). The gateway validates every enabled profile's section at start, so a value the kit
cannot judge (a template placeholder, a pinned thumbprint) still stops a bad start; the health probe then fails.

The API reaches the gateway at `Codelab:Integration:Gateway:BaseUrl` (API settings). That URL must be the https origin
of this site (the default `-AppPath /` keeps the relay routes at the root: `/relay/sso/token`, `/relay/sso/jwks`,
`/relay/sms`).

## Required settings (`required-settings.json`)

`requirements[]` holds one entry per key the gateway cannot start without:

| Field | Meaning |
| --- | --- |
| `path` | The .NET configuration path. One `*` segment stands for every item of an array (`TrustedServices:*:KeyId`). |
| `format` | `1` = any non-empty value. `2` = an absolute **https** URL. |
| `when` | Optional. Either a path that must be `true`, or a list of conditions `{ "path", "equals" }` that must all hold: the value at `path`, or one item of the list at `path`, equals `equals` exactly (case-sensitive, like the host's profile keys). A `*` in a condition path is the same item as in `path`. |

When the gateway host gains a setting it cannot start without, add it to the template and to this file in the same
change.

## TLS certificate

The kit checks every https binding of the site. A certificate is usable when it is in `LocalMachine\My`, has its
private key, is inside its validity dates, carries Server Authentication (or no EKU) and chains to a root this server
trusts. Without a usable one the kit reports the gap `Tls:Certificate`. Pass `-CertificateThumbprint` (and `-HostName`
for SNI, `-HttpsPort` when not 443) to add the binding or move it to a new certificate. A thumbprint that names an
unusable certificate is refused before anything changes.

The health probe never skips certificate validation (there is no `-k`): `-HealthBaseUrl` must be https and must use a
name the certificate covers.

Key material is generated per environment (see the key-rotation runbook). It is never copied from another environment
and never committed. Restrict `appsettings.Production.json` to administrators and the app pool identity.
