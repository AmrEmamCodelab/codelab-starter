using Starter.ArchitectureSpike.Fixture.Domain;

namespace Starter.ArchitectureSpike.Fixture.Api;

/// <summary>Outer-layer type of the spike fixture; depends on the Domain fixture on purpose.</summary>
public static class SampleRecordEndpoint
{
    public static SampleRecord Describe(string title) => new() { Title = title };
}
