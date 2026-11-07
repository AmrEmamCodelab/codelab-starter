# Runbook: rotate keys (signing, PublicId, S2S, tickets, receipts, upload tickets)

Every key ring in Codelab.Security works the same way: **all keys listed in `Keys[]` still verify or decrypt, and
`ActiveKeyId` names the one new values are written with.** A rotation is therefore four steps that never break a
live value:

1. **Generate** a new key (per environment, on the server or an administrator's workstation, never copied from
   another environment, never pasted into a ticket, chat or commit).
2. **Add it as the next key** of the ring on every node that reads the ring. `ActiveKeyId` stays on the old key.
3. **Switch active**: set `ActiveKeyId` to the new key id on every node.
4. **Retire** the old key after the longest lifetime of a value written with it (the table below).

Values live in `appsettings.Production.json` or app pool environment variables (`Codelab__Security__...`). After each
step, run the kit again (`Install-Api.ps1`, `Install-Attachment.ps1`, ...): it adds no settings, checks that every
ring's `ActiveKeyId` names one of its keys and that no two rings share material, then recycles the pool and checks
health. With several API nodes, finish a step on **all** nodes before starting the next one.

| Ring (API settings) | Who reads it | Retire the old key after |
| --- | --- | --- |
| `Codelab:Security:RequestSigning` (request and response signing) | API | the longest session: `Identity:Session:RefreshTokenDays` (default 7 days) plus a day. Sessions keep the key id they started with (`skid`). |
| `Codelab:Security:PublicIds` (PublicId tokens in URLs and payloads) | API | **Normally never.** Every PublicId carries its key id; a retired key breaks every stored or bookmarked link minted with it. Retire only with an accepted break (see Gotchas). |
| `Codelab:Security:Tickets` (SSO sign-in and activation tickets) | API only | `Codelab:Security:Tickets:MaxLifetimeSeconds` (default 7 days). |
| `Codelab:Security:UploadTickets` (upload tickets) | API seals, attachment service opens (its `Codelab:Security:Tickets`) | `Codelab:Security:UploadTickets:MaxLifetimeSeconds` (default 1 hour). |
| `Codelab:Security:Uploads` (upload receipts) | attachment service signs, API verifies | one day (a receipt is linked when the requester submits; a longer-open draft re-uploads). |
| `Codelab:Security:ServiceAssertions` (S2S, ES256) | the caller signs; each callee trusts its public key | immediately after the switch: an assertion lives at most `Server:MaxLifetimeSeconds` (default 60 s). |
| `Identity:Jwt:SigningKey` (access tokens) | API | a single key, no ring: see below. |
| `Observability:UserIdHash:Key` (user hashes in logs) | every service | a single key: see below. |

## Generate

A symmetric key (rings: 32 random bytes, base64) and a key id (`[a-z0-9]{1,8}`, unique in its ring, for example
the month and a letter: `k2611a`), in Windows PowerShell:

```powershell
$bytes = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Convert]::ToBase64String($bytes)
```

An S2S key pair (ES256, P-256): the private key as PKCS#8 PEM for the caller, the public key (SubjectPublicKeyInfo
PEM) for every callee. In PowerShell 7 (.NET 8 or later):

```powershell
$key = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
$key.ExportPkcs8PrivateKeyPem()        # the caller's ServiceAssertions:Client:PrivateKeyPem
$key.ExportSubjectPublicKeyInfoPem()   # the callee's ServiceAssertions:Server:TrustedServices[n]:PublicKeyPem
```

Every ring refuses, at start, key material that is published anywhere (the denylist), so a key from a document or a
sample never works.

## Request signing and PublicIds (API only)

```jsonc
// step 2: add          "Keys": [ { "KeyId": "k2605a", ... }, { "KeyId": "k2611a", "MaterialBase64": "<new>" } ]
// step 3: switch       "ActiveKeyId": "k2611a"
// step 4: retire       remove the k2605a entry (request signing: after the longest session; PublicIds: see Gotchas)
```

Steps 2 and 3 may be done in one change on a **single** node. With several nodes, step 2 must be on every node first:
a node that does not know the new key refuses what another node already writes with it.

## Tickets (API only)

As above, on the API's `Codelab:Security:Tickets`. Retire after `Codelab:Security:Tickets:MaxLifetimeSeconds`.

## Upload tickets (API and attachment service, in lockstep)

The API seals upload tickets with `Codelab:Security:UploadTickets`; the attachment service opens them with **the same
ring** under `Codelab:Security:Tickets`. Never use the API's own `Codelab:Security:Tickets` ring here.

1. Add the new key to the **attachment service's** `Codelab:Security:Tickets:Keys` (the opener learns it first) and
   to the API's `Codelab:Security:UploadTickets:Keys`.
2. Switch the API's `Codelab:Security:UploadTickets:ActiveKeyId` (and, to keep the files identical, the service's
   `Codelab:Security:Tickets:ActiveKeyId`).
3. After `UploadTickets:MaxLifetimeSeconds`, remove the old key from both.

## Upload receipts (attachment service and API, in lockstep)

The attachment service signs receipts with `Codelab:Security:Uploads`; the API verifies them with the same ring.

1. Add the new key to the **API's** `Codelab:Security:Uploads:Keys` first (the verifier learns it first), then to the
   service's.
2. Switch `ActiveKeyId` on the service (and on the API, to keep the files identical).
3. After a day, remove the old key from both.

## S2S keys (ES256 service assertions)

A caller holds one private key (`Codelab:Security:ServiceAssertions:Client:KeyId`, `PrivateKeyPem`); a callee trusts a
list (`Codelab:Security:ServiceAssertions:Server:TrustedServices[]`: `ServiceName`, `KeyId`, `PublicKeyPem`) and looks a
key up by its key id, so the old and the new key can be trusted side by side.

| Caller | Callees that trust its public key |
| --- | --- |
| API | the attachment service, the integration gateway |
| Ops Console API | the API (`/api/ops`; the API also names the caller's key in `Codelab:Ops:TrustedCallerKeyId`) |

1. Generate a new key pair with a new key id.
2. On **every callee**, add a `TrustedServices` entry with the same `ServiceName`, the new `KeyId` and the new public
   key. Keep the old entry.
3. On the caller, replace `Client:KeyId` and `Client:PrivateKeyPem` with the new pair.
4. Remove the old `TrustedServices` entry on every callee.

For the Ops Console, `Codelab:Ops:TrustedCallerKeyId` on the API names exactly one key: change it in the same window
as step 3. Ops Console calls fail between the two changes (operators retry); the application is not affected.

## Single keys without a ring

- **`Identity:Jwt:SigningKey`.** Replace the value and recycle every API node at once. Access tokens signed with the old
  key are refused (401); the web client refreshes once (refresh tokens are server-side and stay valid), so users stay
  signed in. With several nodes, change all of them in one window: a token signed by an updated node is refused by an
  old one until it is updated too.
- **`Observability:UserIdHash:Key`.** Replace it on every service in one window. Log entries before and after the
  change hash the same user differently; note the time of the change for investigations that span it.
- **`Identity:BreakGlass:Password`.** Change it and recycle the API: the break-glass account takes the new password on
  start and its open sessions end.

## Gotchas

- **The kit checks the ring, not the peer.** `Install-Api.ps1` reports an `ActiveKeyId` that names no key
  (`ActiveKeyNotInRing`) and material shared by two rings (`MaterialSharedAcrossRings`). It cannot see the attachment
  service's file: keep the upload-ticket and receipt rings identical on both sides yourself (same key ids and
  material).
- **Retiring a PublicId key breaks links.** Every PublicId minted under the old key (in e-mails, bookmarks, exported
  files, other systems) stops resolving. Keep old PublicId keys in the ring; rotate the active key only when a key is
  suspected compromised, and then decide explicitly to retire it.
- **A suspected compromise** skips the waiting period: add, switch and retire in one change. Values written with the
  old key stop working at once (sessions bound to it end, open tickets fail, users sign in again).
- **Never reuse material across rings or environments.** The upload-ticket ring in particular must not be the API's
  own ticket ring: the attachment service holds it, and a compromised service would then open SSO and activation
  tickets.
