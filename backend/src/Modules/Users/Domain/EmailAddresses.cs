namespace Starter.Modules.Users.Domain;

/// <summary>
/// The shape check of an e-mail address: one <c>@</c> with text on both sides, a dot inside the domain (not at its
/// edges), no whitespace, at most <see cref="UsersLimits.EmailMaxLength"/> characters. It proves the shape only;
/// whether the mailbox exists is the activation e-mail's job (phase 5).
/// </summary>
public static class EmailAddresses
{
    private const char At = '@';
    private const char Dot = '.';
    private const string EmptyLabel = "..";

    /// <summary>True when <paramref name="value"/> has the shape of an e-mail address.</summary>
    public static bool IsWellFormed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > UsersLimits.EmailMaxLength || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var at = value.IndexOf(At, StringComparison.Ordinal);
        if (at <= 0 || at != value.LastIndexOf(At))
        {
            return false;
        }

        var domain = value[(at + 1)..];
        var dot = domain.IndexOf(Dot, StringComparison.Ordinal);
        return dot > 0 && !domain.EndsWith(Dot) && !domain.Contains(EmptyLabel, StringComparison.Ordinal);
    }
}
