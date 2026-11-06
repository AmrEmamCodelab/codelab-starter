using Codelab.Security.PublicIds;
using Starter.Application.Results;
using Starter.Application.Validation;

namespace Starter.Modules.Lookups.Application;

/// <summary>The body of <c>PUT /api/admin/lookups/{type}/items/{id}/active</c>.</summary>
/// <param name="IsActive">True to offer the value again, false to retire it.</param>
public sealed record SetLookupItemActiveCommand(bool? IsActive);

/// <summary>Validates <see cref="SetLookupItemActiveCommand"/>: the flag is required.</summary>
public sealed class SetLookupItemActiveCommandValidator : IValidator<SetLookupItemActiveCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(SetLookupItemActiveCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();
        return input.IsActive is null
            ? errors.Add(FieldNames.Of(nameof(SetLookupItemActiveCommand.IsActive)), ValidationMessages.Required)
            : errors;
    }
}

/// <summary>
/// <c>PUT /api/admin/lookups/{type}/items/{id}/active</c>: activates or deactivates one value. The only run-time change
/// a lookup accepts (names, codes and order change by migration). Repeating the current state is a no-op success. A
/// change drops the type's cached list on this node; other nodes see it within <see cref="LookupsLimits.CacheTtl"/>.
/// </summary>
public sealed class SetLookupItemActive(
    IValidator<SetLookupItemActiveCommand> validator,
    ILookupRepository lookups,
    ILookupsUnitOfWork unitOfWork,
    ILookupCache cache)
{
    /// <summary>
    /// Done, or a refusal (<see cref="PlatformErrorCodes.Validation"/>, <see cref="LookupsErrorCodes.TypeNotFound"/>,
    /// <see cref="LookupsErrorCodes.ItemNotFound"/>).
    /// </summary>
    public async Task<Outcome<Done>> ExecuteAsync(
        string? typeKey, PublicId<LookupItemIds> id, SetLookupItemActiveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        if (!LookupTypeKeys.IsWellFormed(typeKey) || !await lookups.TypeExistsAsync(typeKey!, cancellationToken))
        {
            return Refusal.NotFound(LookupsErrorCodes.TypeNotFound);
        }

        if (!TryItemId(id, out var itemId)
            || await lookups.FindItemAsync(new LookupItemKey(typeKey!, itemId), cancellationToken) is not { } item)
        {
            return Refusal.NotFound(LookupsErrorCodes.ItemNotFound);
        }

        if (item.SetActive(command.IsActive!.Value))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            cache.Invalidate(typeKey!);
        }

        return Done.Value;
    }

    /// <summary>Lookup ids are <see cref="int"/>s; a token that carries a larger number names no value.</summary>
    private static bool TryItemId(PublicId<LookupItemIds> id, out int itemId)
    {
        itemId = default;
        if (!id.HasValue || id.Value > int.MaxValue)
        {
            return false;
        }

        itemId = (int)id.Value;
        return true;
    }
}
