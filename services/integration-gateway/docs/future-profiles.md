# Integration gateway: profiles planned for later

This is the one document in the starter that names the national integrations. They are **out of scope for v1**
(decision log: v1 login modes are basic, Active Directory and SSO; the national integrations become gateway profiles
later). Nothing here is implemented: there is no code, no configuration section and no profile key for them in v1.
When a project needs one, it is built as a gateway profile through the extension point in [MODULE.md](../MODULE.md)
(Extend) and the recipe `docs/recipes/add-integration.md`.

| Later profile | What it would relay | Shape it would take |
| --- | --- | --- |
| Nafath (national single sign-on) | The national SSO login: start a sign-in for a national id, poll or receive its result, read the verified identity. | A profile with its own key, start and status routes under `/relay/{key}/`, the provider's client credentials held by the gateway (environment or secret store), and the callback endpoint the national service requires exposed on the DMZ host behind its own allow-list. The backend adds a fourth login mode that calls the profile through `GatewayRelayClient`. |
| NIC / Yakeen (national identity lookup, National Information Center) | Look up a person by national or resident id and date of birth, returning the verified identity attributes. | A profile with one lookup route, the service account held by the gateway, strict request validation (id formats, Hijri or Gregorian date), response field allow-listing, and call logs that never store the person's data. A local simulator for development and tests (a `FakeUpstream` route, never a deployed service). |

Rules that will apply to both, as to every profile:

- The caller is authenticated only by the ES256 service assertion; the profile adds no other credential.
- Upstream TLS is validated by the Codelab.Integration certificate policy (system trust, pinned authority or pinned
  leaf), never accepted blindly.
- Failures are the gateway failure codes; no upstream text reaches the caller.
- Personal data in requests and answers is never logged.
- The profile is enabled per environment in `Codelab:Integration:Gateway:Profiles`; a disabled profile answers 404
  `GATEWAY_PROFILE_DISABLED`.

The scrub sweep allows the national-integration terms in this file only (`tools/extract/scrub-exceptions.txt`).
