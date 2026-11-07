using Starter.Application.Results;
using Starter.Modules.Ops.Domain.Health;

namespace Starter.Modules.Ops.Application.Health;

/// <summary>The health summary: every component checked at once, and the worst status as the system status.</summary>
/// <param name="Status">The system status (<see cref="HealthRollup.Overall"/>).</param>
/// <param name="CheckedAt">When the checks started.</param>
/// <param name="Components">Each component's check, ordered by name.</param>
public sealed record SystemHealth(ComponentStatus Status, DateTimeOffset CheckedAt, IReadOnlyList<ComponentCheck> Components);

/// <summary>
/// <c>GET api/ops/health</c>: runs every registered <see cref="IComponentProbe"/> in parallel (each bounds its own time)
/// and rolls their statuses up. An unhealthy component is an answer, not a failure: the endpoint still answers 200.
/// </summary>
public sealed class GetSystemHealth(IEnumerable<IComponentProbe> probes, TimeProvider clock)
{
    /// <summary>Checks every component once.</summary>
    public async Task<Outcome<SystemHealth>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var checkedAt = clock.GetUtcNow();
        var checks = await Task.WhenAll(probes.Select(probe => probe.CheckAsync(cancellationToken)));
        var ordered = checks.OrderBy(check => check.Name, StringComparer.Ordinal).ToList();
        return new SystemHealth(HealthRollup.Overall(ordered.Select(check => check.Status)), checkedAt, ordered);
    }
}
