using Codelab.Testing.Architecture;
using Starter.Architecture.Tests.Support;
using Starter.Infrastructure.Modularity;

namespace Starter.Architecture.Tests;

/// <summary>Rule <c>base.module-boundaries</c>: every module's mayDependOn and mustNotDependOn, from rules/rules.compiled.json.</summary>
public sealed class ModuleBoundaryTests
{
    [Fact]
    public void TheHostReferencesAtLeastOneModule() => Assert.NotEmpty(StarterSolution.ModuleAssemblies);

    [Fact]
    public void EveryModuleHasCompiledRules()
    {
        var rules = CompiledRules.LoadFromRepository();
        var missing = StarterSolution.ModuleAssemblies
            .Select(ModuleNaming.NameOf)
            .Where(name => !rules.DotnetModules.Any(module => string.Equals(module.Module, name, StringComparison.Ordinal)))
            .ToList();

        Assert.True(missing.Count == 0, $"Modules without a module.rules.yaml entry in rules.compiled.json: {string.Join(", ", missing)}");
    }

    [Fact]
    public void ModulesAndKernelRespectMayDependOnAndMustNotDependOn() =>
        ModuleBoundaryRules.CheckRepository(StarterSolution.All).ThrowIfFailed();
}
