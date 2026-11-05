using Starter.Application.Validation;

namespace Starter.Modules.Users.Application;

/// <summary>The enum field rule of every Users body: present (<see cref="ValidationMessages.Required"/>) and a defined member (<see cref="ValidationMessages.InvalidFormat"/>).</summary>
public static class EnumFields
{
    /// <summary>Adds the field error of <paramref name="value"/> to <paramref name="errors"/>, if any.</summary>
    public static ValidationErrors Require<TEnum>(ValidationErrors errors, string field, TEnum? value)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (value is null)
        {
            return errors.Add(field, ValidationMessages.Required);
        }

        return Enum.IsDefined(value.Value) ? errors : errors.Add(field, ValidationMessages.InvalidFormat);
    }
}
