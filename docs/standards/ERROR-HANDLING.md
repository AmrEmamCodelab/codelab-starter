# Error handling (C#/.NET)

The starter sends the **wire** a stable `code` from the module's `ErrorCodes` (the Angular client translates it) and sends exception detail to the **log**. Every error response is one problem+json envelope with `code`, `correlationId` and `referenceCode` (Codelab.Errors). A **refusal** is an expected outcome returned as `CodedRefusal`; a **failure** is an exception that `GlobalExceptionHandler` classifies.

> Codelab.Errors (`GlobalExceptionHandler`, `IFailureClassifier`, `CodedRefusal`) ships in phase 1; the domain exceptions live in `Starter.Domain` from phase 2. Verify names in code before copying.

## Steps

**Implementing:** match each `throw`, `catch`, controller refusal and log call you write to a row of the table, and write it that row's way. Done when every one maps to a row.

**Reviewing:** list each `throw`, `catch`, controller refusal and log call the diff adds or changes, then match it to its row and the red flags. Done when every item matches its row or is reported with `file:line`, the row and the fix.

**Lookup by what you are writing:** Application service → 1, 5, 6 · Infrastructure adapter → 2 · controller or endpoint → 3, 5, 6 · new failure type → 4 · background job → 7 · post-commit side effect (notification, outbox) → 8.

## Table, in pipeline order

| # | Stage | Rule |
| --- | --- | --- |
| 1 | Thrown: `GlobalExceptionHandler` | Throw a domain exception: `BusinessRuleException` (422) for a rule broken where no result can be returned, `AccessDeniedException` (403), `ResourceNotFoundException` (404, also for another organization's data). The ordered `IFailureClassifier` table owns the mapping; an unclassified type answers 500. |
| 2 | Thrown: dependency call | Wrap only the outbound call: `catch (Exception ex) when (ex is not OperationCanceledException)`, rethrown as `UpstreamUnavailableException` (502). |
| 3 | Thrown: controller | Let failures propagate. The handler logs one line with identifiers and writes `code`, `correlationId` and `referenceCode`. |
| 4 | Thrown: new failure type | Add a pure `IFailureClassifier` to the classifier table (shared by the HTTP handler and the job failure filter), a constant in `ErrorCodes`, and `errors.<CODE>` in both the `ar` and `en` i18n bundles. |
| 5 | Refusal | Return `CodedRefusal` with the catalogue code; the endpoint maps it once (404, 403, 409 with `code`). Permissions come from `[HasPermission]`, never from a role check inside an action. |
| 6 | Request body | Model binding answers malformed bodies with problem+json. Validators are pure and return every validation error in one refusal; they never throw. |
| 7 | Background job | Let a recurring job throw so the scheduler retries. A job that records a run row catches, stores the classified code (never `ex.Message`) and logs with identifiers. |
| 8 | Load-bearing catch after commit | Catch each post-commit side effect separately (for example one notification), log identifiers, and return success for the committed operation. Prefer the outbox, which retries for you. |

Each stage holds only while the host registers it (exception handler, status-code pages, problem-details customization, model-binding response factory and the job failure filter, all through `AddCodelabErrors`). Confirm the registration before relying on a stage.

BAD/GOOD examples follow for rows 1, 2, 5 and 7. The sample module is `ServiceRequests`.

## Example for row 1: domain exception

```csharp
// BAD
if (actor.OrganizationId is null)
    throw new ArgumentException("Requester has no owning organization."); // unclassified: 500

// GOOD
if (actor.OrganizationId is not { } organizationId)
    throw new AccessDeniedException("Requester has no owning organization."); // 403
var request = await serviceRequests.GetByIdAsync(requestId, ct);
if (request?.OwningOrganizationId != organizationId)
    throw new ResourceNotFoundException(ResourceNames.ServiceRequest, requestId); // 404, same as a missing request
```

## Example for row 2: dependency wrap

```csharp
// BAD
try { return Project(await http.GetFromJsonAsync<RegionDirectoryResponse>(RegionDirectoryPaths.All, ct)); }
catch (Exception) { return []; } // an outage reads as "no regions"; cancellation and mapping bugs vanish

// GOOD
RegionDirectoryResponse? response;
try { response = await http.GetFromJsonAsync<RegionDirectoryResponse>(RegionDirectoryPaths.All, ct); }
catch (Exception ex) when (ex is not OperationCanceledException)
{
    throw new UpstreamUnavailableException(UpstreamNames.RegionDirectory, ex); // 502
}
return Project(response); // a mapping bug stays a 500
```

## Example for row 5: refusal result

```csharp
// BAD
if (request.StatusId != ServiceRequestStatusIds.Draft)
    throw new InvalidOperationException("Only a draft can be submitted."); // 500, no code

// GOOD: the use case returns the refusal; the endpoint maps it once, with its code
if (request.StatusId is not ServiceRequestStatusIds.Draft)
    return CodedRefusal.Conflict(ServiceRequestErrorCodes.NotADraft);
```

## Example for row 7: job catch

```csharp
// BAD
catch (Exception ex) { run.State = JobRunState.Failed; run.Error = ex.Message; } // internals shown on the admin Jobs screen

// GOOD
catch (Exception ex) // load-bearing: records the job run row
{
    run.State = JobRunState.Failed;
    run.Error = failureClassifier.CodeFor(ex);
    logger.LogError(ex, "Job {JobKey} run {RunId} failed.", jobKey, runId);
}
```

## Red flags

- `InvalidOperationException` or `ArgumentException` thrown for a caller or business condition (rows 1, 5)
- `catch (Exception)` that returns a default, or an outbound call caught without the `OperationCanceledException` filter (row 2)
- `try`/`catch` in a controller, `StatusCode(500`, or `ex.Message` in a response (row 3)
- A second exception handler or error envelope; a failure type with no classifier, no `ErrorCodes` constant or a missing i18n key (row 4)
- `Conflict("...")` or `BadRequest(new { message })` without a `code`; a role check written inside an action (row 5)
- `if (!ModelState.IsValid)` in an action; a validator that throws (row 6)
- `ex.Message` stored in a persisted field; an empty `catch` in a job (row 7)
- Personal data in a log template, such as a person's name or a national identity number; log a user hash or an internal ID instead (rows 3, 8)
- A stage from the registration list missing from the host (all rows)
