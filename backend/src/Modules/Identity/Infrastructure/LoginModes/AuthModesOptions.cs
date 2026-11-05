using Codelab.Security.Tickets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Starter.Modules.Identity.Application.LoginModes;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Infrastructure.LoginModes;

/// <summary>
/// The switchable login modes (section <c>Auth</c>). <c>Auth:Modes</c> lists one or more of <c>basic</c>, <c>ad</c> and
/// <c>sso</c>; without the key the deployment offers basic login only. A listed mode switches its provider on
/// (<c>Codelab:Identity:ActiveDirectory</c>, <c>Codelab:Identity:Sso</c>), whose own guard then fails the start when
/// its section is incomplete.
/// </summary>
public sealed class AuthModesOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Auth";

    /// <summary>The configured mode names, or null when <c>Auth:Modes</c> is absent (then <see cref="AuthModeNames.Default"/>).</summary>
    public IList<string>? Modes { get; set; }

    /// <summary>
    /// The SPA's absolute base URL (for example <c>https://portal.example.org</c>), required with <c>sso</c>: the SSO
    /// callback redirects the browser to <c>{WebBaseUrl}/auth/sso/callback</c>.
    /// </summary>
    public string? WebBaseUrl { get; set; }

    /// <summary>
    /// Reads the section by hand rather than with the binder: the binder appends configured items to a default list,
    /// and cannot tell an absent <c>Modes</c> from an empty one. A scalar <c>Auth:Modes</c> (an environment variable such
    /// as <c>Auth__Modes=basic,sso</c>) is read as a comma-separated list.
    /// </summary>
    public static void Bind(IConfigurationSection section, AuthModesOptions options)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(options);
        options.Modes = ReadModes(section.GetSection(nameof(Modes)));
        options.WebBaseUrl = section[nameof(WebBaseUrl)];
    }

    private static List<string>? ReadModes(IConfigurationSection modes)
    {
        if (!modes.Exists())
        {
            return null;
        }

        var items = modes.GetChildren().Select(child => child.Value ?? string.Empty).ToList();
        return items.Count > 0 || modes.Value is null
            ? items
            : [.. modes.Value.Split(AuthModeNames.ListSeparator, StringSplitOptions.TrimEntries)];
    }
}

/// <summary>The mode names of <c>Auth:Modes</c> and their parsing.</summary>
public static class AuthModeNames
{
    /// <summary>Basic login (user name and password checked by this module).</summary>
    public const string Basic = "basic";

    /// <summary>Active Directory login.</summary>
    public const string ActiveDirectory = "ad";

    /// <summary>OpenID Connect single sign-on.</summary>
    public const string Sso = "sso";

    /// <summary>The separator of a scalar list (<c>basic,sso</c>).</summary>
    public const char ListSeparator = ',';

    /// <summary>The modes of a deployment that does not configure <c>Auth:Modes</c>.</summary>
    public static IReadOnlyList<SignInMode> Default { get; } = [SignInMode.Basic];

    private static readonly Dictionary<string, SignInMode> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        [Basic] = SignInMode.Basic,
        [ActiveDirectory] = SignInMode.ActiveDirectory,
        [Sso] = SignInMode.Sso,
    };

    /// <summary>
    /// The modes <paramref name="configured"/> names (null: <see cref="Default"/>), in <see cref="SignInMode"/> order, and
    /// every problem: an empty list, a blank or unknown name, a name listed twice. Names are trimmed and case-insensitive.
    /// </summary>
    public static AuthModeList Parse(IEnumerable<string>? configured)
    {
        if (configured is null)
        {
            return new AuthModeList(Default, Problems: []);
        }

        var modes = new SortedSet<SignInMode>();
        var problems = new List<string>();
        foreach (var name in configured.Select(item => item.Trim()))
        {
            if (!ByName.TryGetValue(name, out var mode))
            {
                problems.Add($"{AuthModesOptions.SectionName}:{nameof(AuthModesOptions.Modes)} has an unknown mode; use {Basic}, {ActiveDirectory} or {Sso}.");
            }
            else if (!modes.Add(mode))
            {
                problems.Add($"{AuthModesOptions.SectionName}:{nameof(AuthModesOptions.Modes)} lists a mode more than once.");
            }
        }

        if (modes.Count == 0 && problems.Count == 0)
        {
            problems.Add($"{AuthModesOptions.SectionName}:{nameof(AuthModesOptions.Modes)} is empty; list at least one of {Basic}, {ActiveDirectory}, {Sso}.");
        }

        return new AuthModeList([.. modes], problems);
    }
}

/// <summary>The parsed <c>Auth:Modes</c>.</summary>
/// <param name="Modes">The known modes listed, in <see cref="SignInMode"/> order.</param>
/// <param name="Problems">Why the list is unusable; empty when it is fine.</param>
public sealed record AuthModeList(IReadOnlyList<SignInMode> Modes, IReadOnlyList<string> Problems)
{
    /// <summary>True when <paramref name="mode"/> is listed.</summary>
    public bool Contains(SignInMode mode) => Modes.Contains(mode);
}

/// <summary>
/// Start-up coherence of <c>Auth</c>: a usable mode list; with <c>sso</c>, an absolute SPA base URL (https outside
/// Development, no query or fragment) and Codelab.Security sealed tickets switched on with a lifetime that fits the
/// pending sign-in. The providers' own sections are checked by their Codelab.Identity deployment guards. Messages
/// name keys, never values.
/// </summary>
public sealed class AuthModesOptionsValidator(IHostEnvironment environment, IOptions<SealedTicketOptions> tickets) : IValidateOptions<AuthModesOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AuthModesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var parsed = AuthModeNames.Parse(options.Modes);
        var problems = parsed.Problems.ToList();
        if (parsed.Contains(SignInMode.Sso))
        {
            problems.AddRange(SsoProblems(options.WebBaseUrl));
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    private static readonly string WebBaseUrlKey = ConfigurationPath.Combine(AuthModesOptions.SectionName, nameof(AuthModesOptions.WebBaseUrl));

    private IEnumerable<string> SsoProblems(string? webBaseUrl)
    {
        if (!Uri.TryCreate(webBaseUrl, UriKind.Absolute, out var spa) || (spa.Scheme != Uri.UriSchemeHttps && spa.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(spa.Query) || !string.IsNullOrEmpty(spa.Fragment))
        {
            yield return $"{WebBaseUrlKey} must be the SPA's absolute http(s) base URL, without query or fragment, when {AuthModeNames.Sso} is enabled.";
        }
        else if (spa.Scheme != Uri.UriSchemeHttps && !environment.IsDevelopment())
        {
            yield return $"{WebBaseUrlKey} must use https outside Development.";
        }

        var sealedTickets = tickets.Value;
        if (!sealedTickets.Enabled)
        {
            yield return $"{SealedTicketOptions.SectionName}:{nameof(SealedTicketOptions.Enabled)} must be true when {AuthModeNames.Sso} is enabled (the sign-in cookie and the exchange ticket are sealed tickets).";
        }
        else if (TimeSpan.FromSeconds(sealedTickets.MaxLifetimeSeconds) < SsoLimits.PendingLifetime)
        {
            yield return $"{SealedTicketOptions.SectionName}:{nameof(SealedTicketOptions.MaxLifetimeSeconds)} is shorter than an SSO sign-in may take ({SsoLimits.PendingLifetime.TotalSeconds} seconds).";
        }
    }
}

/// <summary><see cref="ILoginModes"/> from the validated <see cref="AuthModesOptions"/> (fixed at start).</summary>
public sealed class LoginModeSet(IOptions<AuthModesOptions> options) : ILoginModes
{
    private readonly IReadOnlyList<SignInMode> _enabled = AuthModeNames.Parse(options.Value.Modes).Modes;

    /// <inheritdoc />
    public IReadOnlyList<SignInMode> Enabled => _enabled;

    /// <inheritdoc />
    public bool IsEnabled(SignInMode mode) => _enabled.Contains(mode);
}

/// <summary><see cref="ISsoSpaCallback"/> from <c>Auth:WebBaseUrl</c>.</summary>
public sealed class ConfiguredSsoSpaCallback(IOptions<AuthModesOptions> options) : ISsoSpaCallback
{
    private const char PathSeparator = '/';

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException"><c>Auth:WebBaseUrl</c> is not set (the start-up validation requires it with <c>sso</c>).</exception>
    public Uri CallbackUrl =>
        options.Value.WebBaseUrl is { Length: > 0 } baseUrl
            ? new Uri(baseUrl.TrimEnd(PathSeparator) + SsoSpaFragment.CallbackPath, UriKind.Absolute)
            : throw new InvalidOperationException($"{AuthModesOptions.SectionName}:{nameof(AuthModesOptions.WebBaseUrl)} is not configured.");
}
