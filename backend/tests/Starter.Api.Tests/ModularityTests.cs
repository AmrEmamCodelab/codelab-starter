using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Starter.Api.Tests.Support;
using Starter.Infrastructure.Modularity;

namespace Starter.Api.Tests;

public sealed class ModuleCatalogTests
{
    private static readonly Assembly TestAssembly = typeof(ModuleCatalogTests).Assembly;
    private static readonly Assembly KernelAssembly = typeof(ModuleCatalog).Assembly;

    [Fact]
    public void Zero_modules_give_an_empty_catalog()
    {
        Assert.Empty(ModuleCatalog.FromModules([]).Modules);
        Assert.Empty(ModuleCatalog.Empty.Assemblies);
    }

    [Fact]
    public void A_host_without_module_attributes_discovers_nothing()
    {
        Assert.Empty(ModuleCatalog.Discover(TestAssembly).Modules);
    }

    [Fact]
    public void One_module_is_listed_with_its_assembly()
    {
        var module = new ProbeModule();

        var catalog = ModuleCatalog.FromModules([module]);

        Assert.Same(module, Assert.Single(catalog.Modules));
        Assert.Equal([TestAssembly], catalog.Assemblies);
    }

    [Fact]
    public void Modules_are_ordered_by_name()
    {
        var catalog = ModuleCatalog.FromModules([new NamedModule("Zeta"), new NamedModule("Alpha")]);

        Assert.Equal(["Alpha", "Zeta"], catalog.Modules.Select(module => module.Name));
    }

    [Fact]
    public void Duplicate_module_names_are_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ModuleCatalog.FromModules([new ProbeModule(), new DuplicateProbeModule()]));

        Assert.Contains(Probe.ModuleName, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void A_module_without_a_name_is_refused(string name)
    {
        Assert.Throws<InvalidOperationException>(() => ModuleCatalog.FromModules([new NamedModule(name)]));
    }

    [Fact]
    public void An_assembly_with_several_modules_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ModuleCatalog.ModuleTypeOf(TestAssembly));

        Assert.Contains("exactly one", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_assembly_without_a_module_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => ModuleCatalog.ModuleTypeOf(KernelAssembly));
    }

    [Fact]
    public void A_listed_assembly_that_is_not_a_module_assembly_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ModuleCatalog.FromAssemblies([TestAssembly], KernelAssembly));

        Assert.Contains("Starter.Modules.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Application_parts_are_added_once()
    {
        var manager = new ApplicationPartManager();

        ModuleRegistration.AddParts(manager, [KernelAssembly, TestAssembly, KernelAssembly]);
        ModuleRegistration.AddParts(manager, [TestAssembly]);

        Assert.Equal(2, manager.ApplicationParts.Count);
    }

    /// <summary>A module whose name the test chooses (internal, so the assembly scan does not count it).</summary>
    private sealed class NamedModule(string name) : IModule
    {
        public string Name => name;

        public void AddModule(Microsoft.Extensions.DependencyInjection.IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
        }

        public void UseModule(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints)
        {
        }
    }
}

public sealed class ModuleNamingTests
{
    private static readonly Assembly KernelAssembly = typeof(ModuleCatalog).Assembly;

    [Fact]
    public void Names_come_from_the_assembly_name()
    {
        Assert.Equal("Infrastructure", ModuleNaming.NameOf(KernelAssembly));
        Assert.Equal("Starter", ModuleNaming.SolutionTokenOf(KernelAssembly));
        Assert.Equal("Starter.Modules.", ModuleNaming.ModuleAssemblyPrefixOf(KernelAssembly));
        Assert.Equal("starter-infrastructure", ModuleNaming.ServiceNameOf(KernelAssembly));
    }

    [Fact]
    public void Only_assemblies_under_the_modules_prefix_are_module_assemblies()
    {
        Assert.False(ModuleNaming.IsModuleAssembly(typeof(ModuleNamingTests).Assembly, KernelAssembly));
        Assert.False(ModuleNaming.IsModuleAssembly(KernelAssembly, KernelAssembly));
    }
}
