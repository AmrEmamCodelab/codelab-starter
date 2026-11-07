# Recipe: add an integration

Adds a call from the starter to another system: either a **typed outbound client** that calls an internal system
directly, or a **gateway profile** that relays through the integration gateway in the DMZ to a system outside. Both
run on Codelab.Integration ([MODULE.md](../../packages/dotnet/Codelab.Integration/MODULE.md)): validated options,
retry and timeouts, failure classification and certificate validation that is always on. There is no generator; this
recipe is done by hand.

## 1. Choose the path

| The upstream is... | Use |
| --- | --- |
| Inside the backend zone (another internal service, a DB-side API, the attachment service) | a **typed outbound client** (section 2) |
| Outside the backend zone (the internet, a provider in another network) | a **gateway profile** (section 3) plus a typed client method on the relay client (section 4) |
| An identity provider's token or JWKS endpoint | the built-in `sso` profile; set the OIDC back channel to the gateway relay in Codelab.Identity |
| An SMS provider | the built-in `sms` profile; set the Notifications SMS delivery mode to the gateway |

The API host has no egress in production: its outbound firewall allows SQL, the internal SMTP relay and the gateway
only. A direct call to the outside works on a developer machine and fails in production. Never add a firewall
exception; add a profile.

## 2. A typed outbound client (internal upstream)

1. Write the client in the calling module's `Infrastructure` (it implements a port declared in `Application`):

   ```csharp
   internal sealed class ArchiveClient(HttpClient http) : IArchivePort
   {
       public const string Name = "archive";
       // Methods call relative paths from a routes constants class; never a literal path at the call site.
   }
   ```

2. Add an options class with a `SectionName` constant and register the client in the module registration:

   ```csharp
   services.AddCodelabOutboundClient<ArchiveClient>(ArchiveClient.Name, configuration.GetSection(ArchiveOptions.SectionName));
   services.AddScoped<IArchivePort>(provider => provider.GetRequiredService<ArchiveClient>());
   ```

3. If the upstream is another Codelab service, sign the calls: chain `.AddCodelabServiceAssertion(configuration)` on the
   returned builder (Codelab.Security), and trust the caller's public key on the other side.
4. Mark a POST that the upstream de-duplicates with `MarkIdempotent()`; every other POST is never retried.
5. Map failures with `OutboundFailureClassifier.Classify(...)` to the module's own error codes (problem+json); never
   pass the upstream's text to the user.

## 3. A gateway profile (outside upstream)

A profile is one upstream the gateway relays to. Its contract (key, routes, DTOs, validator) is shared by the gateway
and the caller; its implementation lives in the gateway service.

1. **Key and routes.** Choose a key: lower-case letters, digits and hyphens, at most 32 characters (`GatewayProfileKey.IsValid`),
   for example `registry-lookup`. Its routes live under `/relay/{key}`. Put them in a constants class next to the
   contract:

   ```csharp
   public static class RegistryLookupRoutes
   {
       public const string Key = "registry-lookup";
       public const string Records = GatewayRoutes.RelayPrefix + "/" + Key + "/records";
   }
   ```

2. **DTOs and validator.** One request and one result class per route, JSON with web defaults (`GatewayJson.Options`),
   enums sent as integers with explicit values, optional members marked
   `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`. A static validator returns every problem at once
   (see `SmsRelayRequestValidator`); both sides run it. Put limits in a constants class, as `GatewayRelayLimits` does.
3. **The profile class**, in the gateway service:

   ```csharp
   internal sealed class RegistryLookupProfile(RegistryLookupUpstream upstream) : IGatewayProfile
   {
       public string Key => RegistryLookupRoutes.Key;

       public void Map(IEndpointRouteBuilder endpoints) =>
           endpoints.MapPost(RegistryLookupRoutes.Records, upstream.RelayAsync);
   }
   ```

   - The upstream call inside the gateway is itself a typed outbound client (section 2) with its own section, its own
     certificate trust and its own timeouts.
   - The handler validates the request, calls the upstream, and answers the result, or a refusal through
     `GatewayProblems.For(code)`: `GATEWAY_UPSTREAM_UNAVAILABLE` (503) when the upstream cannot be reached or times
     out, `GATEWAY_UPSTREAM_REJECTED` (502) when it refuses or answers something unusable. Never relay the upstream's
     error text.
   - Add no authentication to the profile. The gateway maps every profile behind the service-assertion scheme (one
     scheme, no JWT, no API key).
4. **Register and enable** it in the gateway host: `services.AddGatewayProfile<RegistryLookupProfile>()`, then add the
   key to `Codelab:Integration:Gateway:Profiles` in the gateway's settings. A registered profile that is not enabled
   answers 404 `GATEWAY_PROFILE_DISABLED`; an enabled key without a registered profile, or two profiles with one key,
   stop the gateway at start-up.
5. **Deployment.** The gateway kit must report the profile's new settings (the upstream URL, its credentials) as
   gaps and refuse to start without them. Upstream host names and credentials never go in the repository.

## 4. The caller's typed client (relay through the gateway)

The API calls the gateway through `GatewayRelayClient` (registered once by `AddCodelabGatewayClient(configuration)`).
It signs every attempt with the API's ES256 service assertion and maps every failure to a `GatewayRelayException`.

1. Wrap it in a typed client in the calling module's `Infrastructure`, behind the module's port:

   ```csharp
   internal sealed class RegistryLookupClient(GatewayRelayClient gateway) : IRegistryLookupPort
   {
       public Task<RecordAnswer> FindAsync(RecordQuery query, CancellationToken cancellationToken) =>
           gateway.PostJsonAsync<RecordQuery, RecordAnswer>(RegistryLookupRoutes.Records, query, cancellationToken);
   }
   ```

   `PostJsonAsync` is never retried; `GetJsonAsync` is retried when the failure is transient. Both refuse a route
   outside `/relay/{key}/...`.
2. Map `GatewayRelayException.Code` to the module's own error code: `GATEWAY_UPSTREAM_UNAVAILABLE` is "try again
   later", `GATEWAY_UPSTREAM_REJECTED` is "the upstream refused", `GATEWAY_PROFILE_DISABLED` is a deployment fault.
   Log `ReferenceCode` (the gateway's) next to your own so operators can follow the call across both logs.
3. The caller's settings need nothing new: `Codelab:Integration:Gateway` (`BaseUrl`, `Audience`) and the S2S identity
   in `Codelab:Security:ServiceAssertions:Client` are shared by every profile.

## 5. Tests to add

- **Contract:** a JSON snapshot test per DTO (exact member names and integer values), the route constants, and the
  validator rows (one test per refused field). See `GatewayContractTests` and `GatewayRequestValidationTests` in
  `packages/dotnet/Codelab.Integration.Tests`.
- **Caller:** the typed client against a recording primary handler (`ConfigurePrimaryHttpMessageHandler`): the route,
  the body, an assertion the gateway's `ServiceAssertionValidator` accepts, and each failure mapping. See
  `GatewayRelayClientTests`.
- **Gateway:** the profile against a fake upstream in the gateway's tests (never a real provider): a success, an
  upstream timeout (503 `GATEWAY_UPSTREAM_UNAVAILABLE`), an upstream refusal (502 `GATEWAY_UPSTREAM_REJECTED`), an
  unsigned or replayed call (401) and the disabled profile (404 `GATEWAY_PROFILE_DISABLED`).
- **Module:** the use case that uses the port, with a fake port, including the mapped failure.

## 6. Verify

```powershell
E:\codelab-tools\dotnet10\dotnet.exe test packages/dotnet/Codelab.Integration.Tests
```

Then run the gateway service's tests and the calling module's tests. In an integration environment, confirm the call
works through the gateway and that the same call made directly from the API host fails (no egress).

## Rules

- Every route, key, code, limit and config key is a constant; no literal paths, keys or codes at a call site.
- One inbound scheme on the gateway: the Codelab.Security ES256 service assertion.
- Certificate validation is always on; trust an internal CA with `PinnedAuthority`, never with a callback that returns
  true.
- Secrets (tokens, client secrets, codes) are never logged; a token response is disposed after use.
- A module change updates its `MODULE.md`, and a contract change in `packages/` needs platform-owner review.
