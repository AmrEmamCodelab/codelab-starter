namespace Starter.Modules.Ops.Domain;

/// <summary>
/// The rule for the operator name the Ops Console forwards with every call (its Windows account, for example
/// <c>CORP\jdoe</c>): 1 to <see cref="OpsLimits.OperatorMaxLength"/> characters after trimming, no control characters.
/// The name is recorded as given; it never authorizes anything (the service assertion is the credential).
/// </summary>
public static class OperatorName
{
    /// <summary>The trimmed name when <paramref name="raw"/> is acceptable, otherwise null.</summary>
    public static string? Normalize(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > OpsLimits.OperatorMaxLength)
        {
            return null;
        }

        return trimmed.Any(char.IsControl) ? null : trimmed;
    }
}
