using Microsoft.Extensions.Hosting;

namespace Codelab.Observability;

/// <summary>
/// The <see cref="LogPropertyNames.DeploymentEnvironment"/> values. A tier is its own property because a
/// preproduction node runs with the ASP.NET Core environment <c>Production</c>; signals and retention key on the tier.
/// </summary>
public static class DeploymentEnvironments
{
    public const string Development = "development";
    public const string Test = "test";
    public const string Staging = "staging";
    public const string Preproduction = "preproduction";
    public const string Production = "production";

    /// <summary>Every allowed value; a configured value outside this list refuses to start.</summary>
    public static IReadOnlyList<string> All { get; } = [Development, Test, Staging, Preproduction, Production];

    /// <summary>
    /// The configured tier (<see cref="ObservabilitySettings.DeploymentEnvironment"/>), or the environment name in
    /// lowercase when unset. Throws <see cref="InvalidOperationException"/> naming the key for a value outside
    /// <see cref="All"/>: a typo would put a tier's events where no alert looks.
    /// </summary>
    public static string Resolve(string? configured, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var tier = string.IsNullOrWhiteSpace(configured)
            ? environment.EnvironmentName.ToLowerInvariant()
            : configured.Trim();

        if (!All.Contains(tier, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{ObservabilitySettings.DeploymentEnvironmentKey} must be one of {string.Join(", ", All)} (got \"{tier}\").");
        }

        return tier;
    }
}
