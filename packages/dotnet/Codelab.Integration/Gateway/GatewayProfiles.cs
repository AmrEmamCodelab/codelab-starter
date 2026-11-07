using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Codelab.Integration.Gateway;

/// <summary>
/// A gateway relay profile: one upstream the DMZ gateway relays to on behalf of the backend (the OIDC provider, the
/// SMS provider, a later one). Register it with <c>AddGatewayProfile&lt;T&gt;()</c>; it is mapped only when its
/// <see cref="Key"/> is listed in <c>Codelab:Integration:Gateway:Profiles</c>.
/// </summary>
public interface IGatewayProfile
{
    /// <summary>The profile key (<see cref="GatewayProfileKey"/>); its routes live under <c>/relay/{Key}</c>.</summary>
    string Key { get; }

    /// <summary>
    /// Maps the profile's relay routes (under <see cref="GatewayRoutes.ProfilePrefix"/> of <see cref="Key"/>). The
    /// host puts them behind the service-assertion scheme; a profile adds no other authentication.
    /// </summary>
    void Map(IEndpointRouteBuilder endpoints);
}

/// <summary>
/// The registered profiles and which of them are enabled. Built once at start-up: a malformed or duplicate key, or an
/// enabled key that no registered profile has, fails the start with every problem in one message.
/// </summary>
public sealed class GatewayProfileRegistry
{
    private readonly HashSet<string> _enabledKeys;

    /// <summary>Creates the registry.</summary>
    /// <exception cref="InvalidOperationException">A malformed or duplicate profile key, or an enabled key with no profile.</exception>
    public GatewayProfileRegistry(IEnumerable<IGatewayProfile> profiles, IOptions<GatewayOptions> options)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(options);
        Registered = [.. profiles];
        _enabledKeys = new HashSet<string>(options.Value.Profiles, StringComparer.Ordinal);
        var problems = Problems(Registered, _enabledKeys);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("The gateway profiles are misconfigured: " + string.Join(" ", problems));
        }

        Enabled = [.. Registered.Where(profile => _enabledKeys.Contains(profile.Key))];
        Disabled = [.. Registered.Where(profile => !_enabledKeys.Contains(profile.Key))];
    }

    /// <summary>Every registered profile, in registration order.</summary>
    public IReadOnlyList<IGatewayProfile> Registered { get; }

    /// <summary>The registered profiles that are enabled.</summary>
    public IReadOnlyList<IGatewayProfile> Enabled { get; }

    /// <summary>The registered profiles that are not enabled (their routes answer 404 GATEWAY_PROFILE_DISABLED).</summary>
    public IReadOnlyList<IGatewayProfile> Disabled { get; }

    /// <summary>True when a registered profile has <paramref name="key"/> and it is enabled.</summary>
    public bool IsEnabled(string key) => _enabledKeys.Contains(key) && Find(key) is not null;

    /// <summary>The registered profile with <paramref name="key"/>, or null.</summary>
    public IGatewayProfile? Find(string key) => Registered.FirstOrDefault(profile => string.Equals(profile.Key, key, StringComparison.Ordinal));

    private static List<string> Problems(IReadOnlyList<IGatewayProfile> registered, HashSet<string> enabledKeys)
    {
        var problems = new List<string>();
        foreach (var profile in registered.Where(profile => !GatewayProfileKey.IsValid(profile.Key)))
        {
            problems.Add($"{profile.GetType().Name} has the malformed key '{profile.Key}'.");
        }

        foreach (var group in registered.GroupBy(profile => profile.Key, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            problems.Add($"The key '{group.Key}' is registered by more than one profile ({string.Join(", ", group.Select(profile => profile.GetType().Name))}).");
        }

        var registeredKeys = registered.Select(profile => profile.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in enabledKeys.Where(key => !registeredKeys.Contains(key)).Order(StringComparer.Ordinal))
        {
            problems.Add($"{GatewayOptions.SectionName}:Profiles enables '{key}', but no profile with that key is registered.");
        }

        return problems;
    }
}
