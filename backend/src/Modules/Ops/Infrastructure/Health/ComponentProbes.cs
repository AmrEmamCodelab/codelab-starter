using Starter.Modules.Ops.Application;
using Starter.Modules.Ops.Domain;
using Starter.Modules.Ops.Domain.Health;
using Starter.Modules.Ops.Infrastructure.Persistence;

namespace Starter.Modules.Ops.Infrastructure.Health;

/// <summary>The names of the components the module always probes.</summary>
public static class OpsComponentNames
{
    /// <summary>The application database.</summary>
    public const string Database = "database";
}

/// <summary>Times one check against a timeout and turns the outcome into a <see cref="ComponentCheck"/>.</summary>
/// <param name="Clock">The clock that measures the duration.</param>
/// <param name="Timeout">The probe timeout.</param>
public sealed record ProbeBudget(TimeProvider Clock, TimeSpan Timeout)
{
    /// <summary>
    /// Runs <paramref name="check"/> with a token that also fires after <see cref="Timeout"/>. A ready answer is healthy,
    /// or degraded when slow; a timeout is <see cref="OpsFindingCodes.ProbeTimeout"/>; the caller's own cancellation
    /// propagates.
    /// </summary>
    public async Task<ComponentCheck> RunAsync(string name, Func<CancellationToken, Task<string?>> check, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);
        using var timeout = new CancellationTokenSource(Timeout, Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var started = Clock.GetTimestamp();
        string? finding;
        try
        {
            finding = await check(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            finding = OpsFindingCodes.ProbeTimeout;
        }

        var elapsed = Clock.GetElapsedTime(started);
        return Answer(name, finding, elapsed);
    }

    private ComponentCheck Answer(string name, string? finding, TimeSpan elapsed)
    {
        var durationMs = (long)elapsed.TotalMilliseconds;
        if (finding is not null)
        {
            return new ComponentCheck(name, ComponentStatus.Unhealthy, durationMs, finding);
        }

        var status = HealthRollup.ForReadyAnswer(elapsed, Timeout);
        return new ComponentCheck(name, status, durationMs, status is ComponentStatus.Degraded ? OpsFindingCodes.ProbeSlow : null);
    }
}

/// <summary>The application database: healthy when the Ops context can open a connection and run a query.</summary>
public sealed class DatabaseProbe(OpsDbContext context, ProbeBudget budget) : IComponentProbe
{
    /// <inheritdoc />
    public string Name => OpsComponentNames.Database;

    /// <inheritdoc />
    public Task<ComponentCheck> CheckAsync(CancellationToken cancellationToken) =>
        budget.RunAsync(Name, CanConnectAsync, cancellationToken);

    private async Task<string?> CanConnectAsync(CancellationToken cancellationToken) =>
        await context.Database.CanConnectAsync(cancellationToken) ? null : OpsFindingCodes.DatabaseUnreachable;
}

/// <summary>
/// A service's readiness URL (<c>/health/ready</c> of the attachment service or the gateway): healthy on 2xx. TLS is
/// validated as usual; an untrusted certificate is <see cref="OpsFindingCodes.ProbeUnreachable"/>, never accepted.
/// </summary>
public sealed class HttpReadinessProbe(string name, Uri readyUrl, HttpClient client, ProbeBudget budget) : IComponentProbe
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public Task<ComponentCheck> CheckAsync(CancellationToken cancellationToken) =>
        budget.RunAsync(Name, GetReadyAsync, cancellationToken);

    private async Task<string?> GetReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(readyUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode ? null : OpsFindingCodes.ProbeNotReady;
        }
        catch (HttpRequestException)
        {
            return OpsFindingCodes.ProbeUnreachable;
        }
    }
}
