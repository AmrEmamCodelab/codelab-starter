namespace Starter.Modules.Users.Application;

/// <summary>
/// How activation links are built: <c>{LinkBase}/activate#ticket={ticket}</c>. The ticket travels in the fragment, so
/// it never reaches a server log or a Referer header; the web app reads it and posts it to <c>/api/auth/activate</c>.
/// </summary>
/// <param name="LinkBase">The web app's origin (and base path), or null when not configured.</param>
public sealed record ActivationLinkSettings(Uri? LinkBase)
{
    /// <summary>The path of the web app's activation page.</summary>
    public const string ActivationPath = "/activate";

    /// <summary>The fragment key that carries the ticket.</summary>
    public const string TicketFragment = "#ticket=";

    private const char PathSeparator = '/';

    /// <summary>True when links can be built.</summary>
    public bool IsConfigured => LinkBase is not null;

    /// <summary>The activation link of <paramref name="ticket"/>.</summary>
    /// <exception cref="InvalidOperationException">No link base is configured (check <see cref="IsConfigured"/> first).</exception>
    public string LinkFor(string ticket)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticket);
        var linkBase = LinkBase ?? throw new InvalidOperationException("Users:Activation:LinkBaseUrl is not configured.");
        return linkBase.AbsoluteUri.TrimEnd(PathSeparator) + ActivationPath + TicketFragment + Uri.EscapeDataString(ticket);
    }
}

/// <summary>The import limits an administrator may tune (each within a hard ceiling in <see cref="Import.ImportLimits"/>).</summary>
/// <param name="MaxRows">The most data rows a sheet may have.</param>
/// <param name="BatchLifetime">How long a previewed batch may be committed.</param>
public sealed record ImportSettings(int MaxRows, TimeSpan BatchLifetime);
