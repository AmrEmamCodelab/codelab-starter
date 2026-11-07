# Coding standards examples (C#/.NET and TypeScript)

Rows follow the standards in [SKILL.md](SKILL.md). A **rule** answers a question (which actions a status offers) and has one **owner**; a **sequence** performs steps (check, change, record) and belongs to one operation. Examples use the sample `ServiceRequests` module (Draft → Submitted → Under Review → Approved/Rejected). They show the target shapes; verify names in code before copying.

## Steps

**Implementing:** for each signature, rule, class or interface, state change, name, lookup comparison, enum and literal you write, find its row and match its reference shape or GOOD example. Done when every one maps to a row.

**Reviewing:** list each of those items the diff adds or changes, then match it to its row and the red flags. Done when every item matches its row or is reported with `file:line`, the row and the fix.

**Lookup by what you are writing:** method signature → 1, 8 · status check or business rule → 2, 12 · new class, interface or layer → 3, 4, 5 · status change → 6 · naming → 7 · throw, catch, refusal or log call → 9 · bug fix → 10 · cache or optimization → 11 · enum → 13 · literal key, ID or limit → 14 · method or file size → 15 · controller or component → 16 · time → 17 · tests → 18.

## Table, in standard order

| # | Standard | Reference shape |
| --- | --- | --- |
| 1 | Name same-type arguments | Typed route IDs (`PublicId<IdDomains.ServiceRequest>`, `PublicId<IdDomains.User>`); named arguments on calls with two same-type parameters, such as `RepositoryPaths.ToFullPath(root: root, relativePath: path)` in the docs gate. |
| 2 | Give each rule one owner | The workflow transition table owns which actions a status offers; the permission map owns which permission each action needs. |
| 3 | Hide implementation details | `IRecordNumberAllocator.AllocateAsync(year)` hides the per-year sequence row and its atomic increment. |
| 4 | Make modules earn their interfaces | Domain ports in `Starter.Domain`, which Application tests replace with hand-written fakes. |
| 5 | Keep reusable mechanisms independent | Email and SMS senders only transport; the module's notification dispatcher decides who receives what. |
| 6 | Make common usage simple | `IJobRuns.AbandonOtherRunningAsync` checks and changes the status of every other running run in one conditional UPDATE. |
| 7 | Use precise, consistent names | `SlaWindow.Duration` is a `TimeSpan`, not an `int` of unknown unit; domain terms follow the glossary in the root `CONTEXT.md`. |
| 8 | Make necessary contracts explicit | The XML doc on `TryTransitionStatusAsync`: it returns false when the record already left the FROM status, and the caller aborts. |
| 9 | Simplify errors without hiding failures | [ERROR-HANDLING.md](ERROR-HANDLING.md). |
| 10 | Keep the change coherent and bounded | A boundary bug fix corrects the rule and tests below, at and above the boundary. |
| 11 | Justify performance complexity | A replay guard on `IMemoryCache` with a deliberate `SizeLimit`; a static dictionary only for data fixed after startup. |
| 12 | Use named lookup values | `ContactTopicIds`, matching the IDs the reference-data migration writes. |
| 13 | Enums stored as integers | `JobRunStatus` (`Queued = 1`, `Running = 2`, ...), mapped to an `int` column. |
| 14 | No magic strings or numbers | `ServiceRequestsPermissionKeys.Decide`, `LookupLimits.CodeMaxLength`, `DeadlinesJobKeys`. |
| 15 | Small, single-purpose code | A use case `Handle` method of guard clauses plus calls to named steps. |
| 16 | No business logic in controllers or components | `ServiceRequestsController` binds, authorizes, calls `SubmitServiceRequest` and maps the result. |
| 17 | Time from `TimeProvider` | `timeProvider.GetUtcNow()`; tests pass a `FakeTimeProvider`. |
| 18 | Unit tests mandatory | `DraftUseCaseTests` covers `SubmitServiceRequest`: success, the invalid draft and each refusal. |

BAD/GOOD examples follow for rows 1, 2, 6, 12, 13 and 14; the other rows rely on their reference shape and red flag.

## Example for row 1: same-type arguments

```csharp
// BAD: three long IDs; swapped arguments still compile
await roles.AssignUserAsync(userId, roleId, actingUserId, ct);

// GOOD: named arguments on the existing port
await roles.AssignUserAsync(roleId: id, userId: body.UserId.Value, actingUserId: currentUser.Id, ct);
```

## Example for row 2: one owner per rule

```csharp
// BAD: the status rule copied into an endpoint; it already misses Submitted
var actions = request.StatusId == ServiceRequestStatusIds.UnderReview
    ? new[] { "approve", "reject" }
    : Array.Empty<string>();

// GOOD: the status rule and the permission rule each come from their owner
var actions = permissionMap.Filter(transitions.AllowedFrom(request.StatusId), currentUser.Permissions);
```

## Example for row 6: check and change in one operation

```csharp
// BAD: another user can move the request between the check and the save
if (request.StatusId != ServiceRequestStatusIds.UnderReview) return CodedRefusal.Conflict(ServiceRequestErrorCodes.NotUnderReview);
request.StatusId = ServiceRequestStatusIds.Approved;
await unitOfWork.SaveChangesAsync(ct);

// GOOD: one conditional UPDATE inside the transaction that also writes the history entry
if (!await serviceRequests.TryTransitionStatusAsync(request.Id,
        fromStatusId: ServiceRequestStatusIds.UnderReview, toStatusId: ServiceRequestStatusIds.Approved, ct))
{
    return CodedRefusal.Conflict(ServiceRequestErrorCodes.NotUnderReview);
}
```

## Example for row 12: named lookup values

```csharp
// BAD: the reader cannot tell which status 4 is
.Where(r => r.StatusId == 4)

// GOOD
.Where(r => r.StatusId == ServiceRequestStatusIds.Approved)
```

## Example for row 13: enums stored as integers

```csharp
// BAD: implicit values (inserting a member renumbers stored data) and stored as text
public enum JobRunState { Queued, Running, Succeeded, Failed }
builder.Property(r => r.State).HasConversion<string>();

// GOOD: explicit values, default int mapping
public enum JobRunState
{
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
}
```

```typescript
// Angular: generated enum plus i18n label keys; never display the member name
export enum JobRunState { Queued = 1, Running = 2, Succeeded = 3, Failed = 4 }
export const JOB_RUN_STATE_LABEL_KEYS: Record<JobRunState, string> = {
  [JobRunState.Queued]: 'jobs.state.queued',
  [JobRunState.Running]: 'jobs.state.running',
  [JobRunState.Succeeded]: 'jobs.state.succeeded',
  [JobRunState.Failed]: 'jobs.state.failed',
};
```

## Example for row 14: no magic strings or numbers

```csharp
// BAD
[HasPermission("service-requests.approve")]
var days = int.Parse(configuration["Sla:DefaultDays"] ?? "5");
if (attempts > 3) { ... }

// GOOD
[HasPermission(PermissionKeys.ServiceRequests.Approve)]
var days = slaOptions.Value.DefaultDays;          // bound from ConfigKeys.Sla.Section, validated at startup
if (attempts > slaOptions.Value.MaxEscalations) { ... }
```

```typescript
// BAD
this.router.navigate(['/service-requests', id]);
// GOOD
this.router.navigate([AppRoutes.serviceRequests.detail, id]);
```

## Red flags

- A new or changed method with two or more parameters of one type, called positionally (row 1)
- The same status set or condition written in a second place (row 2)
- A caller building storage keys, SQL or serialized formats that belong to a repository or adapter (row 3)
- A new class whose public methods only forward to one dependency and form no layer boundary (row 4)
- Screen-, role- or use-case-specific checks inside shared infrastructure such as a sender or the outbox (row 5)
- A status read, checked, assigned and saved without a conditional transition; a disposable created without `using` (row 6)
- A new `Manager`, `Helper` or `Util` type; a duration or size held in a bare `int` (row 7)
- A public port method whose units, null meaning or failure result callers must guess (row 8)
- Any changed `throw`, `catch`, refusal or log call: run [ERROR-HANDLING.md](ERROR-HANDLING.md) (row 9)
- A bug fix that special-cases the reported input value (row 10)
- A static dictionary or cache entry holding changeable data with no size limit or expiry; a speed claim with no measurement (row 11)
- A bare number compared with or assigned to a `...StatusId` or `...TypeId`; a new enum mirroring seeded IDs; a comparison with a display label (row 12)
- An enum member without an explicit value; `HasConversion<string>()` on an enum; an enum member name shown to a user (row 13)
- A string literal passed as a permission, role, config, claim, cache, route or job key; a bare number other than 0, 1 or -1 used as a limit or ID (row 14)
- A method that needs a comment to separate its steps; nested `if` pyramids; commented-out code (row 15)
- A repository, `DbContext` or rule inside a controller; `HttpClient` or business rules inside a component (row 16)
- `DateTime.Now`/`UtcNow`/`Today` or `DateTimeOffset.Now`/`UtcNow` anywhere outside tests that assert the ban (row 17)
- New logic with no test in the same change; a test that only restates the implementation (row 18)
