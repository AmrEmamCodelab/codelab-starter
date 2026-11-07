namespace Starter.Modules.Ops.Domain.Health;

/// <summary>The health of one component, or of the whole system (sent as the integer value).</summary>
public enum ComponentStatus
{
    /// <summary>The component answered and is ready.</summary>
    Healthy = 1,

    /// <summary>The component answered ready, but slower than half the probe timeout.</summary>
    Degraded = 2,

    /// <summary>The component did not answer, or answered that it is not ready.</summary>
    Unhealthy = 3,
}

/// <summary>The rule that turns the component statuses into the system status.</summary>
public static class HealthRollup
{
    /// <summary>A ready answer slower than the probe timeout divided by this is <see cref="ComponentStatus.Degraded"/>.</summary>
    public const int SlowAnswerDivisor = 2;

    /// <summary>The worst status in <paramref name="statuses"/>; <see cref="ComponentStatus.Healthy"/> when there is none.</summary>
    public static ComponentStatus Overall(IEnumerable<ComponentStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        return statuses.DefaultIfEmpty(ComponentStatus.Healthy).Max();
    }

    /// <summary>
    /// The status of a component that answered ready after <paramref name="elapsed"/>: <see cref="ComponentStatus.Degraded"/>
    /// when that took more than half of <paramref name="timeout"/>, otherwise <see cref="ComponentStatus.Healthy"/>.
    /// </summary>
    public static ComponentStatus ForReadyAnswer(TimeSpan elapsed, TimeSpan timeout) =>
        elapsed > timeout / SlowAnswerDivisor ?ComponentStatus.Degraded : ComponentStatus.Healthy;
}
