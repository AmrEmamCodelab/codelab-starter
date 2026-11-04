using NetArchTest.Rules;
using Starter.ArchitectureSpike.Fixture.Api;
using Starter.ArchitectureSpike.Fixture.Domain;

namespace Starter.ArchitectureSpike.Tests;

/// <summary>
/// ADR-011 spike: one NetArchTest rule over a two-project fixture, run on net10.0. The second test proves the rule
/// really inspects the assemblies (a rule that can never fail would also be "green").
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly string DomainNamespace = typeof(SampleRecord).Namespace!;
    private static readonly string ApiNamespace = typeof(SampleRecordEndpoint).Namespace!;

    [Fact]
    public void Domain_does_not_depend_on_Api()
    {
        var result = Types.InAssembly(typeof(SampleRecord).Assembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_rule_detects_a_real_dependency()
    {
        var result = Types.InAssembly(typeof(SampleRecordEndpoint).Assembly)
            .ShouldNot()
            .HaveDependencyOn(DomainNamespace)
            .GetResult();

        Assert.False(result.IsSuccessful);
        Assert.Contains(typeof(SampleRecordEndpoint).FullName, result.FailingTypeNames);
    }
}
