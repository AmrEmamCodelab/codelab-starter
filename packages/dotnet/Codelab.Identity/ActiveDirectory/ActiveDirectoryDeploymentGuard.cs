using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Codelab.Identity.ActiveDirectory;

/// <summary>
/// The deployment rules for the Active Directory provider, as one pure function: the list of problems that keep a
/// host from signing anyone in (empty when it may start). Run at start through <c>ValidateOnStart</c>, so a
/// half-configured directory fails the deploy, not the first sign-in.
/// <para>
/// Rules while <see cref="ActiveDirectoryOptions.Enabled"/>: <c>Host</c>, <c>Domain</c> and <c>BaseDn</c> are set;
/// <c>Port</c> is a TCP port; <c>TimeoutSeconds</c> is 1 to <see cref="MaxTimeoutSeconds"/>; <c>SearchFilter</c> is a
/// parenthesised filter containing <c>{0}</c> and formats; <c>NetBiosName</c> and every <c>UpnSuffixes</c> entry carry no
/// <c>\</c> or <c>@</c>; <c>MatchBy</c> is defined; and outside Development
/// <c>UseLdaps</c> is true (a simple bind sends the password, and there is no fallback to cleartext). Certificate
/// validation has no off switch, so there is nothing to refuse there.
/// </para>
/// <para>A message names keys, never values.</para>
/// </summary>
public static class ActiveDirectoryDeploymentGuard
{
    /// <summary>The longest allowed sign-in timeout, in seconds.</summary>
    public const int MaxTimeoutSeconds = 120;

    /// <summary>The highest TCP port.</summary>
    public const int MaxPort = 65535;

    private const string FilterOpen = "(";
    private const string FilterClose = ")";
    private const string SampleValue = "x";
    private static readonly char[] QualifierSeparators = ['\\', '@'];

    /// <summary>The problems that keep this deployment from starting; empty when it may.</summary>
    /// <param name="options">The bound section (an absent section binds to defaults, which are disabled).</param>
    /// <param name="isDevelopment">Whether the host runs in the Development environment (cleartext LDAP allowed there only).</param>
    public static IReadOnlyList<string> Check(ActiveDirectoryOptions options, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return [];
        }

        var problems = new List<string>();
        RequireSet(problems, nameof(ActiveDirectoryOptions.Host), options.Host);
        RequireSet(problems, nameof(ActiveDirectoryOptions.Domain), options.Domain);
        RequireSet(problems, nameof(ActiveDirectoryOptions.BaseDn), options.BaseDn);
        CheckNumbers(problems, options);
        CheckQualifiers(problems, options);
        CheckFilter(problems, options.SearchFilter);
        if (!Enum.IsDefined(options.MatchBy))
        {
            problems.Add(Key(nameof(ActiveDirectoryOptions.MatchBy), $"must be one of {string.Join(", ", Enum.GetNames<LoginMatchField>())}"));
        }

        if (!options.UseLdaps && !isDevelopment)
        {
            problems.Add(Key(nameof(ActiveDirectoryOptions.UseLdaps), "must be true outside Development (a simple bind over cleartext LDAP sends the password in the clear)"));
        }

        return problems;
    }

    private static void RequireSet(List<string> problems, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add(Key(key, "must be set"));
        }
    }

    private static void CheckNumbers(List<string> problems, ActiveDirectoryOptions options)
    {
        if (options.Port is < 1 or > MaxPort)
        {
            problems.Add(Key(nameof(ActiveDirectoryOptions.Port), $"must be a TCP port (1 to {MaxPort})"));
        }

        if (options.TimeoutSeconds is < 1 or > MaxTimeoutSeconds)
        {
            problems.Add(Key(nameof(ActiveDirectoryOptions.TimeoutSeconds), $"must be 1 to {MaxTimeoutSeconds}"));
        }
    }

    /// <summary>A qualifier carrying a separator could never equal what a user types before or after it.</summary>
    private static void CheckQualifiers(List<string> problems, ActiveDirectoryOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.NetBiosName) && options.NetBiosName.IndexOfAny(QualifierSeparators) >= 0)
        {
            problems.Add(Key(nameof(ActiveDirectoryOptions.NetBiosName), "must be the bare NetBIOS domain name (no \\ or @)"));
        }

        if (options.UpnSuffixes.Any(suffix => string.IsNullOrWhiteSpace(suffix) || suffix.IndexOfAny(QualifierSeparators) >= 0))
        {
            problems.Add(Key(nameof(ActiveDirectoryOptions.UpnSuffixes), "must list bare DNS suffixes (not blank, no \\ or @)"));
        }
    }

    private static void CheckFilter(List<string> problems, string? filter)
    {
        var key = nameof(ActiveDirectoryOptions.SearchFilter);
        if (string.IsNullOrWhiteSpace(filter)
            || !filter.Contains(ActiveDirectoryOptions.LoginNamePlaceholder, StringComparison.Ordinal))
        {
            problems.Add(Key(key, $"must contain the logon-name placeholder {ActiveDirectoryOptions.LoginNamePlaceholder}"));
            return;
        }

        if (!filter.StartsWith(FilterOpen, StringComparison.Ordinal) || !filter.EndsWith(FilterClose, StringComparison.Ordinal) || !Formats(filter))
        {
            problems.Add(Key(key, $"must be a parenthesised LDAP filter whose only format items are {ActiveDirectoryOptions.LoginNamePlaceholder} and {ActiveDirectoryOptions.BindNamePlaceholder}"));
        }
    }

    private static bool Formats(string filter)
    {
        try
        {
            _ = string.Format(CultureInfo.InvariantCulture, filter, SampleValue, SampleValue);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Key(string key, string rule) => $"{ActiveDirectoryOptions.SectionName}:{key} {rule}";
}

/// <summary>Runs <see cref="ActiveDirectoryDeploymentGuard"/> when the options are first read (at start, through ValidateOnStart).</summary>
internal sealed class ActiveDirectoryOptionsValidator(IHostEnvironment environment) : IValidateOptions<ActiveDirectoryOptions>
{
    public ValidateOptionsResult Validate(string? name, ActiveDirectoryOptions options)
    {
        var problems = ActiveDirectoryDeploymentGuard.Check(options, environment.IsDevelopment());
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
