namespace Codelab.Testing.Checks;

/// <summary>
/// The outcome of one Codelab.Testing check: what was checked, how many items were examined and every violation.
/// Test-framework neutral; call <see cref="ThrowIfFailed"/> from any test framework.
/// </summary>
public sealed class CheckResult
{
    /// <summary>Creates a result. A check that examined nothing is a violation in itself (see <see cref="IsVacuous"/>).</summary>
    public CheckResult(string title, IEnumerable<string> violations, int examined)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(violations);
        ArgumentOutOfRangeException.ThrowIfNegative(examined);
        Title = title;
        Violations = [.. violations];
        Examined = examined;
    }

    /// <summary>What was checked, used as the first line of the failure message.</summary>
    public string Title { get; }

    /// <summary>Every violation, one line each.</summary>
    public IReadOnlyList<string> Violations { get; }

    /// <summary>How many items (types, endpoints, files, cases) the check examined.</summary>
    public int Examined { get; }

    /// <summary>
    /// True when the check examined nothing. A guard that sees nothing proves nothing, so it fails: a narrowed scope,
    /// a wrong namespace or an empty endpoint list must not pass silently.
    /// </summary>
    public bool IsVacuous => Examined == 0;

    /// <summary>True when there is no violation and the check examined something.</summary>
    public bool IsSuccessful => Violations.Count == 0 && !IsVacuous;

    /// <summary>The failure message: the title, then every violation on its own line.</summary>
    public string Describe()
    {
        var lines = IsVacuous ? Violations.Prepend(CheckMessages.Vacuous) : Violations;
        return string.Join(Environment.NewLine, lines.Select(line => CheckMessages.Indent + line).Prepend(Title));
    }

    /// <summary>Throws <see cref="ContractViolationException"/> unless the check succeeded.</summary>
    public void ThrowIfFailed()
    {
        if (!IsSuccessful)
        {
            throw new ContractViolationException(this);
        }
    }

    /// <summary>Combines several results into one under a new title.</summary>
    public static CheckResult Combine(string title, IEnumerable<CheckResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var all = results.ToList();
        var violations = all.SelectMany(result => result.Violations.Select(violation => $"{result.Title}: {violation}"));
        return new CheckResult(title, violations, all.Sum(result => result.Examined));
    }
}

/// <summary>Thrown by <see cref="CheckResult.ThrowIfFailed"/>; the message lists every violation.</summary>
public sealed class ContractViolationException : Exception
{
    /// <summary>Creates the exception for a failed result.</summary>
    public ContractViolationException(CheckResult result)
        : base(result?.Describe())
    {
        ArgumentNullException.ThrowIfNull(result);
        Result = result;
    }

    /// <summary>The failed result.</summary>
    public CheckResult Result { get; }
}

internal static class CheckMessages
{
    public const string Indent = "  ";
    public const string Vacuous = "the check examined nothing, so it proves nothing (wrong scope, namespace or input?)";
}
