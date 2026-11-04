using System.Reflection;
using Codelab.Testing.Architecture;

namespace Codelab.Testing.Tests.Architecture;

public sealed class LayerRulesTests
{
    private const string Clean = "Codelab.Testing.Tests.Fixtures.Layers.Clean";
    private const string Dirty = "Codelab.Testing.Tests.Fixtures.Layers.Dirty";
    private static readonly Assembly[] Fixtures = [typeof(LayerRulesTests).Assembly];

    [Fact]
    public void A_clean_module_passes_every_layer_rule()
    {
        var result = LayerRules.Check(Fixtures, Clean);

        Assert.True(result.IsSuccessful, result.Describe());
        Assert.Equal(4, result.Examined);
    }

    [Fact]
    public void Domain_reaching_into_Application_is_reported()
    {
        var result = LayerRules.DomainIsInnermost(Fixtures, Dirty);

        Assert.Equal($"{Dirty}.Domain.DirtyThing uses a forbidden layer or type", Assert.Single(result.Violations));
    }

    [Fact]
    public void Application_holding_a_DbContext_is_reported()
    {
        var result = LayerRules.ApplicationUsesPortsOnly(Fixtures, Dirty);

        Assert.Equal($"{Dirty}.Application.DirtyService uses a forbidden layer or type", Assert.Single(result.Violations));
    }

    [Fact]
    public void Api_using_a_DbContext_is_reported_but_the_composition_root_is_exempt()
    {
        var dirty = LayerRules.ApiHasNoDbContext(Fixtures, Dirty);
        var clean = LayerRules.ApiHasNoDbContext(Fixtures, Clean);

        Assert.Equal($"{Dirty}.Api.DirtyEndpoint uses a forbidden layer or type", Assert.Single(dirty.Violations));
        Assert.True(clean.IsSuccessful, clean.Describe());
        Assert.Equal(1, clean.Examined);
    }

    [Fact]
    public void DbContext_types_are_found_by_their_base_type()
    {
        var contexts = LayerRules.DbContextTypes(Fixtures);

        Assert.Equal([$"{Clean}.Infrastructure.CleanDbContext", $"{Dirty}.Infrastructure.DirtyDbContext"], contexts);
    }

    [Fact]
    public void A_module_without_the_layer_is_vacuous()
    {
        var result = LayerRules.DomainIsInnermost(Fixtures, "Codelab.Testing.Tests.Fixtures.Layers.Missing");

        Assert.True(result.IsVacuous);
        Assert.False(result.IsSuccessful);
    }
}
