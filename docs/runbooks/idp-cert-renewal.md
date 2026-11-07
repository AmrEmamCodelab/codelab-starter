# Runbook: renew the identity provider's signing certificate

SSO sign-in (`Codelab:Identity:Sso`, OpenID Connect) trusts an id_token only when its signature verifies against a key
the API knows. The identity provider (IdP) signs with a private key whose certificate has an end date. If the IdP's
certificate expires, or the IdP switches to a new key the API does not know, **every SSO sign-in fails**. Basic and
Active Directory sign-in keep working, and the break-glass account still signs in.

This runbook covers the warning, the renewal with an overlap, and the checks afterwards. Coordinate it with the IdP's
owner: the starter never holds the IdP's private key.

| Piece | Where |
| --- | --- |
| How the API reads the IdP's keys | `Codelab:Identity:Sso:SigningKeyMode`: `Jwks` (the IdP's JWKS, cached 1 h, refetched on an unknown `kid` at most every 30 s) or `Pinned` (`Codelab:Identity:Sso:PinnedKeys`). See the Codelab.Identity `MODULE.md`. |
| No egress from the backend zone | With `Jwks`, the API fetches the key set through the integration gateway's SSO relay (`GET /relay/sso/jwks`). With `Pinned`, a rotation is a configuration change on the API. |
| Which certificates are watched | The API's `/api/ops/certificates`: one row `idp-signing-N` per signing key of the IdP's JWKS (`Codelab:Ops:IdentityProviderJwksUrl`), plus every certificate listed in `Codelab:Ops:Certificates`. |
| Thresholds | `Codelab:Ops:CertificateWarningDays` (default 30) and `CertificateCriticalDays` (default 7). |
| The alert in Ops Console | The certificates view (`GET /ops-api/certificates`), served by the [Ops Console API](../../services/ops-console/MODULE.md). |
| The Seq signal | Signal "Ops: Certificate expiry warnings" (`Has(CertificateStatus) and CertificateStatus in [2, 3, 4]`) and the alert "Ops: [env] Certificate expiry warnings", from `deploy/seq/templates`. |

## 1. Recognize the warning

**In Ops Console,** open the certificates view. Each row shows the name, subject, end date (`notAfter`), whole days
left and a status:

| Status | Value | Meaning | Act |
| --- | --- | --- | --- |
| Ok | 1 | More than the warning window left. | Nothing. |
| Warning | 2 | Fewer than `CertificateWarningDays` left. | Plan the renewal with the IdP owner (section 2). |
| Critical | 3 | Fewer than `CertificateCriticalDays` left, **or the certificate could not be read** (the row then has no dates and a `code`, such as the IdP's key set being unreachable). | Renew this week; for an unreadable row, fix the read first (section 5). |
| Expired | 4 | Past its end date. | SSO is down or about to be: section 6. |

**In Seq,** every time the certificates view loads, the Ops Console writes one Warning event
**`CertificateNeedsAttention`** per row that is not Ok, with the properties `CertificateName`, `CertificateStatus`
(the integer above) and `DaysLeft`. The signal "Ops: Certificate expiry warnings" collects them, and the alert of the
same name notifies the configured channels at most once a day per application. To see the history, filter on
`EventId.Name = 'CertificateNeedsAttention'` and group by `CertificateName`.

The alert fires only after something has read the list (an operator opening the view, or another reader of
`/api/ops/certificates` that logs the same property). Make opening the certificates view part of the weekly ops check.

## 2. Plan the renewal (Warning)

1. Ask the IdP owner for the new certificate's subject, its validity dates and the **switch date**: the day the IdP
   starts signing id_tokens with the new key.
2. Agree on an **overlap**: the IdP publishes the new key in its JWKS **before** the switch date and keeps the old key
   published until every token signed with it has expired (at least the id_token lifetime; a day is comfortable).
3. With `SigningKeyMode=Pinned`, get the new **public key** (SPKI PEM, or the certificate to extract it from) and its
   `kid`. Never accept or store the IdP's private key.

## 3. Before the switch date

**`Jwks` mode:**

1. Once the IdP publishes the new key, the Ops Console certificates view shows one more `idp-signing-N` row, Ok, with
   the new end date. That proves the API (through the gateway relay) sees the new key set.
2. Nothing else to change on the API: an unknown `kid` makes it refetch the key set.

**`Pinned` mode:**

1. Add the new key as a second entry next to the current one, on the API server (environment or secret store; never a
   committed file):
   `Codelab__Identity__Sso__PinnedKeys__1__Kid` and `Codelab__Identity__Sso__PinnedKeys__1__PublicKey`.
2. Recycle the API's app pool. A key that is not RSA 2048+ or EC, or a private key, refuses the start and names the
   setting: fix it before the switch date.
3. If the certificate is also listed in `Codelab:Ops:Certificates` (by thumbprint or file), add the new certificate's
   row so Ops Console watches it.

## 4. After the switch date

1. Sign in with SSO on the portal, with a real account. It must land on the dashboard.
2. In Seq, check the API for the last hour: no id_token validation failures from Codelab.Identity, and no warning
   "OIDC JWKS refresh failed; serving the key set fetched at ..." (the key set could not be refreshed and a stale one
   was used).
3. In Ops Console, the new `idp-signing-N` row is Ok.
4. When the IdP removes the old key from its JWKS (`Jwks` mode), its row disappears from the view. In `Pinned` mode,
   remove the old `PinnedKeys` entry and its `Codelab:Ops:Certificates` row once the overlap has passed, then
   recycle the API's app pool.

## 5. A row that cannot be read (Critical with a code)

- **The IdP's key set is unreachable:** check the integration gateway's `/health/ready` from the API server
  (`curl -sS https://<gateway host>/health/ready`; the relay itself answers only service-assertion calls), then the
  gateway's own log in Seq for the upstream failure, then the `Codelab:Ops:IdentityProviderJwksUrl` setting on the
  API. Never add `-k`: a certificate that does not validate is a finding of its own.
- **A key without an `x5c` certificate chain:** its end date cannot be known. Ask the IdP owner for the end date and
  track it in the change calendar; the row stays Critical as a reminder.
- **A `Codelab:Ops:Certificates` entry not found:** the thumbprint or file path is wrong, or the app pool identity
  cannot read the store or file. Only the public certificate is read.

## 6. Expired: SSO is down

1. Tell the helpdesk: users sign in with basic or AD sign-in where enabled; administrators use the break-glass account.
2. Ask the IdP owner to publish (or re-publish) a valid key. With `Jwks` mode the API picks it up on the next sign-in;
   with `Pinned` mode add it as in section 3 and recycle the app pool.
3. Verify as in section 4, then record the incident with the reference codes users reported (Ops Console traces each
   one to its request and log events).

Do not disable signature validation, add `HS256` or `none` to `AllowedAlgorithms`, or pin a key you did not get from
the IdP owner: each of these lets anyone forge a sign-in.
