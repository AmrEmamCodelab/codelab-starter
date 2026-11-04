# Codelab.Integration

## Purpose

The outbound call engine, version 0.1. Every HTTP call a starter service makes to another system goes through a
Codelab outbound client:

- **Typed clients.** `AddCodelabOutboundClient<TClient>(name, ...)` registers a typed `HttpClient` with named,
  start-up-validated options.
- **Retry and timeout policy.** Microsoft.Extensions.Http.Resilience / Polly.Core, outermost first: total timeout,
  retry, attempt timeout. All values come from options.
- **Failure classification.** `OutboundFailureClassifier` maps a status code or an exception to `OutboundFailureKind`
  (stored as int, explicit values) and says whether it is transient. The retry policy and the host's error mapping use
  the same ruling.
- **Certificate validation that is always on.** This fixes the source system's internal calls, whose certificate check
  always passed. There is no "accept anything" mode:
  - `SystemTrust` (default): no callback at all, so .NET's own chain validation against the OS store.
  - `PinnedAuthority`: only the configured internal CA bundle. System roots are not trusted, the certificate must allow
    TLS server authentication, and the host name must match.
  - `PinnedLeaf`: only SHA-256-pinned server certificates, still checked for host name and validity dates.
- **Correlation hook.** A handler slot (`AddCodelabOutboundHandler<THandler>()`) puts host handlers outermost, so a
  correlation-id handler from the observability package or the host runs once per call. This package does not depend
  on the observability package.

The gateway contracts (dispatcher, auth strategies, idempotency keys, call log and response interpretation) arrive in
0.5, in phase 8.

## Use

```csharp
// The host's own typed options section name lives in a constant, never a string literal at the call site.
services.AddCodelabOutboundClient<ArchiveClient>(ArchiveClient.Name, configuration.GetSection(ArchiveOptions.SectionName));

// Or in code:
services.AddCodelabOutboundClient<ArchiveClient>(ArchiveClient.Name, options =>
{
    options.BaseAddress = new Uri("https://archive.example.test/");
    options.Certificate.Mode = CertificateTrustMode.PinnedAuthority;
    options.Certificate.AuthorityBundlePath = archiveOptions.CaBundlePath;
});

services.AddCodelabOutboundHandler<CorrelationIdHandler>(); // outermost, once per call

// Retry a POST only when the upstream de-duplicates it:
var request = new HttpRequestMessage(HttpMethod.Post, "orders") { Content = body }.MarkIdempotent();
```

Configuration shape (one section per client):

```json
{
  "BaseAddress": "https://archive.example.test/",
  "Retry": { "MaxRetries": 2, "BaseDelay": "00:00:00.400", "MaxDelay": "00:00:10" },
  "Timeout": { "AttemptTimeout": "00:00:10", "TotalTimeout": "00:00:30" },
  "Certificate": { "Mode": 2, "AuthorityBundlePath": "C:\\certs\\internal-ca.pem", "CheckRevocation": false }
}
```

`Certificate.Mode` is the integer value: 1 is `SystemTrust`, 2 is `PinnedAuthority`, 3 is `PinnedLeaf`. Defaults are
in `OutboundDefaults`.

## Extend

- **Another handler for every client:** call `AddCodelabOutboundHandler<T>()`. Slot handlers run in registration
  order, outside the retry policy.
- **A handler for one client, per attempt:** call `AddHttpMessageHandler` on the `IHttpClientBuilder` that
  `AddCodelabOutboundClient` returns. It runs inside the retry policy, once per attempt.
- **A new failure kind:** add an `OutboundFailureKind` member with the next explicit value and never renumber. Map it
  in `OutboundFailureClassifier`, then add a row to `OutboundFailureClassifierTests`.
- **A new trust mode:** add a `CertificateTrustMode` value, an `IServerCertificateValidator` and its case in
  `ServerCertificatePolicy` and in `OutboundCertificateOptionsValidation`. Add a real-handshake test to
  `ServerCertificateValidationTests`. The validator must refuse by default.

## Rules

- No always-true certificate validator, anywhere. `ServerCertificatePolicy` is the only file that may set a
  validation callback. `NoAlwaysTrueValidatorTests` fails the build otherwise.
- A pinned anchor never excuses a host-name mismatch or a missing certificate (`HandshakeErrors`).
- Retries fail closed. Only GET, HEAD and OPTIONS, or a request marked with `MarkIdempotent`, are retried, and only
  after a transient failure. A refused certificate, a 4xx other than 408 and 429, and a cancellation are never retried.
- Every number lives in `OutboundDefaults` or the options. Options are validated at start-up (`ValidateOnStart`) and
  on every read, and all failures are reported in one message.
- This package depends on no other Codelab package (see `module.rules.yaml`). The public API is tracked in
  `PublicAPI.Unshipped.txt`.

## Gotchas

- `HttpClient.Timeout` is set to infinite on purpose: the resilience pipeline owns timeouts. A client timeout would
  cut across the retries.
- `MaxRetries = 0` leaves the retry strategy out of the pipeline. Polly itself refuses 0 attempts, so the engine does
  not add a retry strategy with a predicate that never fires.
- A Retry-After value is honoured only on 429 and 503, and is capped at `Retry.MaxDelay`. Its date form is measured
  against the injected `TimeProvider`.
- Revocation checking is off by default, as in .NET. A backend zone without egress usually cannot reach a CRL or OCSP
  endpoint. Turn on `CheckRevocation` where the internal CA publishes one.
- In the TLS tests on Windows, Schannel cannot serve a certificate with an ephemeral key, so the run-time test
  certificates are round-tripped through PKCS#12.
- Two `AddCodelabOutboundClient` calls with the same `TClient` and different names register the typed client twice,
  and the last one wins. Use one typed client per upstream.
