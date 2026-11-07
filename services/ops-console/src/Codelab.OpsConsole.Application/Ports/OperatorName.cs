namespace Codelab.OpsConsole.Application.Ports;

/// <summary>
/// The signed-in operator's account name (for Windows sign-in <c>DOMAIN\user</c>), forwarded to the API on every
/// call so its audit trail names the person behind the console. A distinct type, so it cannot be swapped with a
/// reference code or any other string argument.
/// </summary>
public readonly record struct OperatorName
{
    /// <summary>Creates the name; refuses an empty one.</summary>
    /// <exception cref="ArgumentException">The value is null, empty or white space.</exception>
    public OperatorName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    /// <summary>The account name, trimmed.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}
