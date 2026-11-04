# Codelab.Errors

## Purpose

One error contract for every Codelab host (it replaces the five error envelopes the source product had). Every error a
host returns, thrown, framework-generated, validation or refusal, is ONE `application/problem+json` body:

```json
{ "status": 409, "title": "Request failed", "detail": "...", "code": "RECORD_NOT_A_DRAFT",
  "correlationId": "9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b", "referenceCode": "9C1E5-B7A20" }
```

plus `errors` (field to messages) on a validation failure. No `type`, no `instance`, no framework `traceId`, and never
an exception message or type name. The client branches on `code` and renders `errors.<CODE>` from its own `ar`/`en`
bundles. Parts:

- `GlobalExceptionHandler`: the only `IExceptionHandler`. Logs a failure ONCE (identifiers only) and writes the envelope.
- `FailureClassifierTable` and `IFailureClassifier`: the ordered classifier table (first claim wins); also the
  `IFailureCodeResolver` of `JobFailureLogFilter`, so a job log and the wire use the same codes.
- `FrameworkStatusCodes` (status to code and sentence), `StatusCodePagesWriter` (empty 401/403/404/405/415 bodies) and
  `ProblemDetailsNormalizer` (every `IProblemDetailsService` and MVC problem body).
- `ModelBindingErrors`: MVC model binding and validation, with no binder text on the wire.
- `ClientErrorScrubber`: no exception text, type name or ad-hoc member reaches a client, whichever writer produced it.
- `CodedRefusal`: an expected business "no" returned as a value, mapped once by the endpoint.

## Use

```csharp
builder.AddCodelabObservability("starter-api");
builder.Services.AddCodelabErrors(errors => errors
    .Map<ResourceNotFoundException>(StatusCodes.Status404NotFound, ErrorCodes.NotFound, LogLevel.Information)
    .Map<BusinessRuleException>(e => new Failure(StatusCodes.Status422UnprocessableEntity, e.Code, "...", LogLevel.Information)));
...
app.UseCodelabObservability();   // first
app.UseCodelabErrors();          // exception handler + status-code pages
```

In a use case: `return CodedRefusal.Conflict(ServiceRequestErrorCodes.NotADraft);`. In the endpoint:
`refusal.ToResult()` (minimal API) or `refusal.ToActionResult()` (MVC). A validator returns
`CodedRefusal.Invalid(errors)` with every field error at once.

An adapter wraps ONLY its outbound call and rethrows `UpstreamUnavailableException` (502):
`catch (Exception ex) when (ex is not OperationCanceledException) { throw new UpstreamUnavailableException(name, ex); }`.

## Extend

- **A new failure type.** Map it in `AddCodelabErrors` (`Map<TException>`), or add an `IFailureClassifier` to
  `options.Classifiers`. Registration order IS priority: built-ins first (`RequestBodyClassifier`,
  `UpstreamUnavailableClassifier`, `ConcurrencyClassifier`, `TransientFaultClassifier`), then `Map` calls in order;
  insert into the list to go earlier. Add the code to the module's `ErrorCodes` and `errors.<CODE>` to both i18n bundles.
- **Domain exceptions.** `BusinessRuleException`, `AccessDeniedException` and `ResourceNotFoundException` live in
  Starter.Domain (phase 2), which must not reference this package; the host maps them with `Map<T>`.
- **An extra body member.** Add its name to `options.AllowedExtensionMembers`; anything else is scrubbed.

## Rules

- One handler, one envelope: never register a second `IExceptionHandler`, never write an error body by hand, never put
  `ex.Message` in a response. Use `CodedRefusal` for expected outcomes and a classifier for failure types.
- Classifiers are pure: no logging, no response writing, no I/O; they must tolerate a null `HttpContext` (jobs).
- A classifier's `Detail` reaches the wire: a vetted sentence, never a quoted value.
- Error codes are typed constants (`ErrorCodes`, a module's own class); enums are int with explicit values.
- Depends on Codelab.Observability only (see `module.rules.yaml` and the generated `CLAUDE.md`).

## Gotchas

- **Order: `UseCodelabObservability()` then `UseCodelabErrors()`.** Reversed, Serilog's request line logs every
  exception a second time. `FailureLoggingTests` pins "logged once".
- **.NET 10 suppresses the exception middleware's own "unhandled exception" line** when the handler returns true, so the
  source product's duplicate-event filter is not needed; the same test would catch a regression.
- **`Problem(detail: ...)` is scrubbed.** A detail survives only when a Codelab writer vetted it or it is a status-table
  sentence, so a hand-written detail on `Results.Problem` or MVC `Problem()` becomes the status's sentence.
- **Content negotiation.** When no problem-details writer accepts the request (an `Accept` without JSON), the envelope is
  still written as problem+json; a Codelab error is never a bare status.
- **`ConcurrencyClassifier` must stay before `TransientFaultClassifier`**: a lost update is a `DbUpdateException` too.
- **The unclassified code is `ErrorCodes.InternalError`** (`INTERNAL_ERROR`); a member named `Internal` reads as an
  internal host name to the scrub sweep.
- **Not carried from the source product:** the browser error-report scrubber, breadcrumb grammar and client-errors
  endpoint (they return with that endpoint), attachment and file-parse classifiers (attachment service, phase 6), the
  "exception handled" enricher, response and step spans, and the legacy `traceId` switch. The starter's
  `ClientErrorScrubber` is new code with a different job: it scrubs outgoing error bodies.
