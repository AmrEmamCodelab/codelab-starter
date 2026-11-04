using System.Reflection;
using Codelab.Testing.Architecture;
using Codelab.Testing.Checks;

namespace Codelab.Testing.Tests.Architecture;

public sealed class ModuleBoundaryRulesTests
{
    private const string Root = "Codelab.Testing.Tests.Fixtures.Modules";
    private static readonly Assembly[] Fixtures = [typeof(ModuleBoundaryRulesTests).Assembly];

    private const string RulesJson = """
        {
          "schemaVersion": 1,
          "modules": [
            { "module": "Alpha", "kind": "dotnet", "path": "m/alpha", "mayDependOn": ["Beta"], "mustNotDependOn": ["Gamma"] },
            { "module": "Beta", "kind": "dotnet", "path": "m/beta", "mayDependOn": [], "mustNotDependOn": ["Alpha"] },
            { "module": "AlphaExtra", "kind": "dotnet", "path": "m/alpha-extra", "mayDependOn": [], "mustNotDependOn": [] },
            { "module": "Gamma", "kind": "dotnet", "path": "m/gamma", "mayDependOn": ["Alpha"], "mustNotDependOn": [] },
            { "module": "Web", "kind": "angular", "path": "web", "mayDependOn": [], "mustNotDependOn": [] }
          ]
        }
        """;

    [Fact]
    public void A_dependency_not_in_mayDependOn_is_reported_and_namespace_prefixes_do_not_collide()
    {
        var result = ModuleBoundaryRules.Check(CompiledRules.Parse(RulesJson, "fixture"), Map(), Fixtures);

        var violation = Assert.Single(result.Violations);
        Assert.Contains("Beta:", violation, StringComparison.Ordinal);
        Assert.Contains("BetaThing depends on module AlphaExtra (not in mayDependOn)", violation, StringComparison.Ordinal);
        Assert.Equal(4, result.Examined);
    }

    [Fact]
    public void A_dependency_listed_in_mustNotDependOn_names_that_list()
    {
        var json = RulesJson.Replace("\"mayDependOn\": [\"Alpha\"], \"mustNotDependOn\": []", "\"mayDependOn\": [], \"mustNotDependOn\": [\"Alpha\"]", StringComparison.Ordinal);

        var result = ModuleBoundaryRules.Check(CompiledRules.Parse(json, "fixture"), Map(), Fixtures);

        Assert.Contains(result.Violations, violation => violation.Contains("GammaThing depends on module Alpha (listed in mustNotDependOn)", StringComparison.Ordinal));
    }

    [Fact]
    public void Allowed_dependencies_pass()
    {
        var json = RulesJson.Replace("\"module\": \"Beta\", \"kind\": \"dotnet\", \"path\": \"m/beta\", \"mayDependOn\": []", "\"module\": \"Beta\", \"kind\": \"dotnet\", \"path\": \"m/beta\", \"mayDependOn\": [\"AlphaExtra\"]", StringComparison.Ordinal);

        var result = ModuleBoundaryRules.Check(CompiledRules.Parse(json, "fixture"), Map(), Fixtures);

        Assert.True(result.IsSuccessful, result.Describe());
    }

    [Fact]
    public void Modules_without_types_in_the_assemblies_make_the_check_vacuous()
    {
        var result = ModuleBoundaryRules.Check(CompiledRules.Parse(RulesJson, "fixture"), Map(), [typeof(string).Assembly]);

        Assert.True(result.IsVacuous);
        Assert.Throws<ContractViolationException>(result.ThrowIfFailed);
    }

    [Fact]
    public void Codelab_Testing_respects_its_own_module_rules()
    {
        var result = ModuleBoundaryRules.CheckRepository([typeof(CheckResult).Assembly]);

        result.ThrowIfFailed();
    }

    private static ModuleNamespaceMap Map() => ModuleNamespaceMap.FromPairs(new Dictionary<string, string>
    {
        ["Alpha"] = $"{Root}.Alpha",
        ["Beta"] = $"{Root}.Beta",
        ["AlphaExtra"] = $"{Root}.AlphaExtra",
        ["Gamma"] = $"{Root}.Gamma",
    });
}
