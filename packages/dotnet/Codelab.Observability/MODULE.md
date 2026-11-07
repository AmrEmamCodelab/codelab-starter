# Codelab.Observability

## Purpose

One observability setup for every Codelab host (it replaces the three separate setups the source product had):

- **Serilog host bootstrap.** `AddCodelabObservability` configures the root logger (levels from the `Serilog` section,
  the standard properties, the PII destructuring policy) and one retained sub-logger (rolling CLEF file, opt-in Seq,
  any sink declared under `Serilog:WriteTo` or registered in DI), behind the host's PII predicate and the
  exception-text redaction. `CodelabHost.Run` wraps `Main` and leaves one Fatal in a node-local startup file when the
  host cannot start.
- **Correlation and reference codes.** One server-owned correlation id per request (the W3C trace id), echoed as
  `X-Correlation-ID` on every response and pushed onto every log line with its `ReferenceCode` (`9C1E5-B7A20`, what a
  user reads to the helpdesk). `CorrelationScope` carries an id into background work. No caller chooses it: an inbound
  `X-Correlation-ID` is only logged as `InboundCorrelationId`, and an inbound `traceparent`/`tracestate`/`baggage` is
  ignored (`PropagationScope.InboundPropagator` replaces the hosting propagator), so every request starts its own trace.
- **PII-safe logging.** `PiiMask` and the `[Pii(PiiKind.X)]` destructuring policy, the keyed `UserIdHash` (HMAC-SHA256
  of the NameIdentifier), `ExceptionTextRedaction` for values quoted by database errors, and `RetainedSinkPolicy` for
  framework lines that carry query strings or SQL text.
- **OpenTelemetry, opt-in.** Off by default; when on, ASP.NET Core and HttpClient spans exported over OTLP/HTTP.
- **Outbound propagation.** Deny by default; `AsInternalService()` sends trace context and the correlation id to a
  Codelab-owned service with certificate validation ON; `AsExternalService()` sends nothing.
- **Job failure log filter.** `JobFailureLogFilter` gives a failed background job the same one-line failure log as a
  request, with the code from `IFailureCodeResolver` (Codelab.Errors supplies its classifier table).
- **Health endpoints.** `MapCodelabHealth()` maps `/health/live` (no checks) and `/health/ready` (checks tagged `ready`).

## Use

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddCodelabObservability("starter-api");      // or AddCodelabObservability(new ObservabilityOptions { ... })
builder.Services.AddCodelabErrors();                 // Codelab.Errors, optional
var app = builder.Build();
app.UseCodelabObservability();                       // FIRST: request line + correlation id
app.UseCodelabErrors();                              // then the error contract
app.UseAuthentication();
app.UseCodelabUserLogContext();                      // after authentication: UserIdHash on every line
app.UseAuthorization();
app.MapCodelabHealth();
```

`Main` can be `return CodelabHost.Run(args, BuildAsync, ObservabilityOptions.For("starter-api"));`.

Configuration (section `Observability`, bound to `ObservabilitySettings`):

| Key | Default | Meaning |
| --- | --- | --- |
| `DeploymentEnvironment` | environment name, lowercase | Tier: `development`, `test`, `staging`, `preproduction`, `production`. Anything else refuses to start. |
| `LogFile:Enabled` / `Directory` / `RetainedFileCountLimit` / `FileSizeLimitBytes` | `true` / `{app}/logs` / 31 files / 50 MB | Rolling CLEF file `{ServiceName}-.json`. |
| `Seq:ServerUrl` / `ApiKey` / `QueueSizeLimit` | off / none / 100,000 | Seq sink; https outside Development; the key is an app-pool variable. |
| `Tracing:Enabled` / `OtlpEndpoint` | `false` / none | OTLP/HTTP traces URL, used as given (e.g. `http://localhost:4318/v1/traces`). |
| `UserIdHash:Key` | none | Base64 of at least 32 random bytes, generated per environment by init. Required in Production. Outside Development a published key (harvest-source or starter fixture, from Codelab.Security's `PublishedMaterial/*.json`, linked at build time) refuses to start. |

Levels come from the `Serilog` section (`Serilog:MinimumLevel:Default`, `Serilog:MinimumLevel:Override:<category>`).
A Hangfire host adds the filter itself: `services.AddHangfire((sp, c) => c.UseFilter(sp.GetRequiredService<JobFailureLogFilter>()))`.

## Extend

- **A host PII rule.** Set `ObservabilityOptions.IsUnsafeForRetainedSinks` to a predicate that also calls
  `RetainedSinkPolicy.IsUnsafeByDefault`, so the default exclusions stay on.
- **A personal-data property.** Mark it `[Pii(PiiKind.Email)]` (or `Phone`, `LoginName`, `Url`, `Recipient`; the default
  `Hidden` keeps nothing) and log the object with `{@Value}`.
- **A new log property.** Add the name to `LogPropertyNames` (it is the Seq contract; a rename is breaking).
- **Another sink.** Declare it under `Serilog:WriteTo` (with `Serilog:Using`) or register an `ILogEventSink` in DI; both
  land inside the retained policy.
- **A readiness check.** `services.AddHealthChecks().AddCheck(name, check, tags: [HealthEndpoints.ReadyTag])`.
- **Failure codes off the request thread.** Implement `IFailureCodeResolver` (Codelab.Errors already does).
- **A failure line with a run-time level and many fields.** Use `StructuredLogState` with `ILogger.Log` (the analyzers
  require source-generated logging, and `[LoggerMessage]` methods are limited to five parameters).

## Rules

- The public API is stable since 1.0.0 (`PublicAPI.Shipped.txt`): a breaking change needs a new major version; a fix ships as a patch release (ADR-013).
- Log identifiers, never content: no names, national ids, contact details or request bodies in a template; use the
  `UserIdHash`, an internal id, or a `[Pii]`-marked object. Never log the raw NameIdentifier.
- One setup: hosts call `AddCodelabObservability`; no host builds its own Serilog pipeline.
- No committed user-hash key: init generates it; Production without one refuses to start.
- Outbound clients declare `AsInternalService()` or `AsExternalService()`; certificate validation is never disabled here.
- Constants, not literals, for property names, headers and configuration keys; enums are int with explicit values.
- See `module.rules.yaml` and the generated `CLAUDE.md` (this package depends on no other Codelab package).

## Gotchas

- **The root `ReadFrom.Configuration` defect is fixed and pinned by a test.** The harvested bootstrap read the whole
  `Serilog` section on the root logger, so a `Serilog:WriteTo` sink skipped the PII predicate and the redaction. Now
  only `MinimumLevel`/`LevelSwitches` are read at the root (`SerilogConfigurationSlices`) and everything else inside the
  retained sub-logger. `ObservabilityBootstrapTests` fails if a configured sink ever sees an unsafe event again.
- **Pipeline order matters.** `UseCodelabObservability()` must come BEFORE `UseCodelabErrors()`. The other way round,
  the exception passes through Serilog's request-logging middleware first and is logged twice with its stack.
- **The request line uses the host's logger.** The host owns its logger (`preserveStaticLogger`), so the request
  logging middleware is given the DI `Serilog.ILogger`; with the static `Log.Logger` the request line was silently lost.
- **`Release` is the host's version, found by the application name.** `AddCodelabObservability(serviceName)` loads
  the assembly named by `IHostEnvironment.ApplicationName` (WebApplicationFactory sets it to the entry point's
  assembly). `Assembly.GetEntryAssembly()` is the test runner under a test host, so `ObservabilityOptions.For` (entry
  assembly) is only right in a real process; pass `HostAssembly = typeof(Program).Assembly` when building the options
  by hand.
- **Source overrides are root-only in Serilog.** The retained sub-logger filters; it never overrides levels.
- **Without a key outside Production** the hash uses a random per-process key and a startup Warning says so: hashes
  then differ across restarts and nodes.
- **A caller's `traceparent` never becomes the correlation id.** ASP.NET Core hosting adopts the inbound trace id
  by default, which let an anonymous caller choose its reference code and plant sightings under another user's code.
  `AddCodelabObservability` registers `PropagationScope.InboundPropagator` (extracts nothing) in DI, and
  `CorrelationId.For` also refuses an Activity whose trace began outside the process (second line of defence).
  Consequence: an internal call (`AsInternalService()`) still SENDS trace context, but the receiving Codelab host
  starts a new trace; join the two by the receiver's `InboundCorrelationId`. Pinned by
  `RequestPipelineTests.A_caller_supplied_traceparent_never_sets_the_correlation_id_or_the_trace`.
- **Not carried from the source product (yet):** trusted-parent trace adoption for internal callers, the heartbeat and
  OTLP export health listener, the PII-safe span processor and step spans, the Serilog SelfLog file, and baggage
  allowlists (baggage never leaves a host now). They return with the gateway and Ops Console work (phase 8) if needed.
