# Starter.Application

## Purpose
The kernel Application project (ADR-004): **ports only**, plus the use-case result types. Modules code against these
interfaces; adapters live in Starter.Infrastructure or in the module that owns the data.

| Area | Types | Implemented by |
| --- | --- | --- |
| Results | `Outcome<T>`, `Outcome`, `Refusal`, `RefusalCategory`, `Done`, `PlatformErrorCodes` | (values) |
| Validation | `IValidator<T>`, `ValidationErrors`, `ValidationMessages`, `FieldNames` | each module |
| Persistence | `IUnitOfWork` | each module's `ModuleDbContext` |
| Users | `ICurrentUserContext`, `IUserDirectory`, `DirectoryUser` | kernel (HTTP) / Identity |
| Authorization | `IPermissionCatalogSource`, `PermissionDefinition`, `PermissionKeyFormat`, `IUserPermissionGrants` | each module / Access |
| Auditing | `IAuditContextAccessor`, `AuditContext` | kernel (HTTP) |
| Localization | `ICurrentLanguage` | kernel (HTTP) |

## Use
```csharp
public sealed class CloseRecord(IRecordRepository records, IRecordsUnitOfWork unitOfWork, IValidator<CloseRecordCommand> validator)
{
    public async Task<Outcome<Done>> HandleAsync(CloseRecordCommand command, CancellationToken cancellationToken)
    {
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);                                  // 400, every field error at once
        }

        var record = await records.FindAsync(command.Id, cancellationToken);
        if (record is null)
        {
            return Refusal.NotFound(RecordsErrorCodes.RecordNotFound);        // 404
        }

        record.Close();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Done.Value;
    }
}
```
Field names come from `FieldNames.Of(nameof(Command.Member))`; messages from `ValidationMessages`.

## Extend
- **A new cross-module port:** add the interface here (with records for its data), implement it in the owning module
  and register it in that module's `Add<Name>Module`. Never let one module reference another module's project.
- **A new refusal kind:** add a `RefusalCategory` member with the next explicit value, a factory on `Refusal`, the
  mapping in `Starter.Infrastructure/Results/OutcomeResults.cs` and tests on both sides.
- These types are frozen for the modules (contract §3): add members, never rename, remove or change a signature.

## Rules
- Ports only: no EF Core, ASP.NET Core, configuration, DI or Codelab package reference (`starterapplication.no-frameworks`).
- A use case returns `Outcome<T>`; it throws only for faults (ERROR-HANDLING rows 1 and 4).
- Permission keys match `PermissionKeyFormat.Pattern` (`^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$`, at most 128 characters).
- Validators are pure: they never throw and return every field error at once.
- Tests: `backend/tests/Starter.Application.Tests` (Outcome, Refusal, ValidationErrors, PermissionKeyFormat, FieldNames).

## Gotchas
- `Outcome<T>.Value` throws on a refusal; branch on `IsSuccess` first (the compiler knows `Refusal` is not null then).
- `PlatformErrorCodes.Validation` mirrors Codelab.Errors (`VALIDATION_FAILED`); this assembly references no package, so
  a test in `Starter.Api.Tests` fails when the two drift.
- `ValidationErrors.ToDictionary()` returns a copy; later `Add` calls do not change a refusal already built.
