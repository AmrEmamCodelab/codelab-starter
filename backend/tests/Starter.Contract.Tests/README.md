# Starter.Contract.Tests

Rule `base.endpoint-contract` over the real host (`WebApplicationFactory<Program>`, every module), with no database.

## What it checks

| Test | Contract |
| --- | --- |
| `AllEndpointsHavePermission` | Every endpoint has `[HasPermission]` or an `EndpointAllowlist` entry with the access it really has (anonymous or authenticated-only). Stale entries fail. |
| `AllAnonymousWritesAreRateLimited` | Every anonymous POST/PUT/PATCH/DELETE carries `[EnableRateLimiting]` (the `codelab-client` policy), so sign-in and refresh cannot be called without limit (`AnonymousWriteRateLimitContract`). |
| `AllIdsArePublicId` | Route ids and id-shaped DTO members are `PublicId<T>` (`Codelab.Testing` `PublicIdContract`). |
| `AllWritesHaveValidator` | Every request body type has an `IValidator<T>` in a module or the host (`EndpointContracts.ValidatorsIn`). |
| `AllErrorsAreProblemJson` | Every endpoint probed anonymously answers its error as Codelab.Errors problem+json; plus 401, 404 and 400 validation explicitly. 403 and coded refusals need seeded data and are asserted in `Starter.Integration.Tests`. |

`EndpointAccessContractTests`, `AnonymousWriteRateLimitContractTests` and `TestHostSupportTests` unit-test the support code.

## Use

```powershell
E:\codelab-tools\dotnet10\dotnet.exe test backend/tests/Starter.Contract.Tests
```

## Extend

- **`EndpointAllowlist.cs` is a closed list.** Adding an entry is a security decision: give the reason and get a
  review. Keys are `METHOD /route` as the routing table prints them (`*` for any method).
- Entries carry their owner (a module name, `Kernel` or `Platform`). Entries of a module the host no longer
  references are ignored, so `tools/remove-module.ps1` needs no edit here; remove the dead lines when convenient.

## Gotchas

- The host runs in Development without a database: the startup-task runner is removed, migrations and the
  security-store schema are off, and the connection string points at a database that is never opened. An endpoint
  whose error path needs SQL belongs in the integration tests.
- `Support/Hosting/` is compiled into `Starter.Integration.Tests` too (linked). Keep it free of contract-only code.
- Settings go in twice (host setting and last configuration source): Codelab.Observability reads configuration while
  Program registers services, and Program loads the developer's local secrets file in Development, which the
  test values must beat.
- The Codelab.Errors envelope has no `type` member (RFC 9457 reads it as `about:blank`), so `type` is not required.
