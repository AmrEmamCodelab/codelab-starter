using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Settings.Application.Reading;
using Starter.Modules.Settings.Domain;

namespace Starter.Modules.Settings.Application.Admin;

/// <summary>The body of <c>PUT /api/admin/settings/{key}</c>.</summary>
/// <param name="Value">The new value as text, in the setting's format (see <see cref="SettingValueFormat"/>).</param>
public sealed record UpdateSettingRequest(string? Value);

/// <summary>
/// The shape of <see cref="UpdateSettingRequest"/>: a value is present and not longer than
/// <see cref="SettingFieldLimits.ValueMaxLength"/>. It may be empty (a text setting accepts empty); the type and range
/// are checked by <see cref="UpdateSetting"/> against the setting itself.
/// </summary>
public sealed class UpdateSettingRequestValidator : IValidator<UpdateSettingRequest>
{
    /// <summary>The wire name of the value field.</summary>
    public static readonly string ValueField = FieldNames.Of(nameof(UpdateSettingRequest.Value));

    /// <inheritdoc />
    public ValidationErrors Validate(UpdateSettingRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();
        if (input.Value is null)
        {
            return errors.Add(ValueField, ValidationMessages.Required);
        }

        return input.Value.Length > SettingFieldLimits.ValueMaxLength ? errors.Add(ValueField, ValidationMessages.TooLong) : errors;
    }
}

/// <summary><c>GET /api/admin/settings</c>: every setting, ordered by group, then key.</summary>
public sealed class ListSettings(ISettingsRepository settings)
{
    /// <summary>The settings (never refused).</summary>
    public async Task<Outcome<IReadOnlyList<Setting>>> ExecuteAsync(CancellationToken cancellationToken) =>
        Outcome.Success(await settings.ListAsync(cancellationToken));
}

/// <summary>
/// <c>PUT /api/admin/settings/{key}</c>: changes a setting's value. The value is checked against the setting's type and
/// range and stored in canonical form; the change and a new stamp version are written in one transaction (the audit
/// interceptor records the change), and this node's snapshot is dropped so it reloads at once. An unchanged value
/// writes nothing.
/// </summary>
public sealed class UpdateSetting(
    IValidator<UpdateSettingRequest> validator,
    ISettingsRepository settings,
    ISettingsUnitOfWork unitOfWork,
    SettingsSnapshotCache cache,
    SettingChangeMarks changeMarks)
{
    /// <summary>
    /// The setting after the change, or a refusal: <see cref="PlatformErrorCodes.Validation"/> (400, field <c>value</c>
    /// with <c>REQUIRED</c>, <c>TOO_LONG</c>, <c>INVALID_FORMAT</c> or <see cref="SettingsErrorCodes.ValueOutOfRange"/>),
    /// <see cref="SettingsErrorCodes.SettingNotFound"/> (404).
    /// </summary>
    public async Task<Outcome<Setting>> ExecuteAsync(string key, UpdateSettingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = validator.Validate(request);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        if (string.IsNullOrWhiteSpace(key) || await settings.FindAsync(key, cancellationToken) is not { } setting)
        {
            return Refusal.NotFound(SettingsErrorCodes.SettingNotFound);
        }

        var check = setting.Check(request.Value!);
        if (!check.IsValid)
        {
            return Refusal.Invalid(new ValidationErrors().Add(UpdateSettingRequestValidator.ValueField, MessageOf(check.Problem)));
        }

        var mark = await changeMarks.CurrentAsync(cancellationToken);
        if (setting.Change(request.Value!, mark.At, mark.ByUserId))
        {
            await unitOfWork.SaveChangesAndAdvanceStampAsync(cancellationToken);
            cache.Invalidate();
        }

        return setting;
    }

    /// <summary>The field message code of a refused value.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="problem"/> is <see cref="SettingValueProblem.None"/> or undefined.</exception>
    internal static string MessageOf(SettingValueProblem problem) => problem switch
    {
        SettingValueProblem.InvalidFormat => ValidationMessages.InvalidFormat,
        SettingValueProblem.OutOfRange => SettingsErrorCodes.ValueOutOfRange,
        SettingValueProblem.TooLong => ValidationMessages.TooLong,
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "Not a refusal reason."),
    };
}
