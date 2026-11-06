using Codelab.Testing.Architecture;
using Codelab.Testing.Checks;
using Starter.Architecture.Tests.Support;
using Starter.Infrastructure.Modularity;

namespace Starter.Architecture.Tests;

/// <summary>
/// Layering inside each module (ADR-004) and rules <c>base.application-ports-only</c> and <c>base.no-dbcontext-in-api</c>,
/// one theory row per discovered module.
/// </summary>
public sealed class LayerTests
{
    public static TheoryData<string> Modules => StarterSolution.ModuleNames;

    public static TheoryData<string> ModulesWithDomain => StarterSolution.ModuleNamesWithDomain;

    [Theory]
    [MemberData(nameof(Modules))]
    public void EveryTypeSitsInALayerNamespace(string module)
    {
        var assembly = StarterSolution.ModuleAssembly(module);
        LayerPlacement.EveryTypeIsInALayer(assembly, StarterSolution.RootNamespaceOf(assembly), ModuleCatalog.ModuleTypeOf(assembly)).ThrowIfFailed();
    }

    [Theory]
    [MemberData(nameof(ModulesWithDomain))]
    public void DomainIsInnermost(string module) =>
        LayerRules.DomainIsInnermost(StarterSolution.All, RootOf(module)).ThrowIfFailed();

    [Theory]
    [MemberData(nameof(Modules))]
    public void ApplicationUsesPortsOnly(string module) =>
        LayerRules.ApplicationUsesPortsOnly(StarterSolution.All, RootOf(module)).ThrowIfFailed();

    [Theory]
    [MemberData(nameof(Modules))]
    public void ApplicationSignaturesMentionNoHostingOrPersistenceType(string module)
    {
        var assembly = StarterSolution.ModuleAssembly(module);
        var applicationRoot = $"{RootOf(module)}.{LayerNames.Application}";
        var namespaces = assembly.GetExportedTypes()
            .Select(type => type.Namespace)
            .OfType<string>()
            .Where(name => LayerPlacement.IsSameOrNested(name, applicationRoot))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        CheckResult.Combine(
                $"{applicationRoot} is free of ASP.NET Core and EF Core",
                namespaces.Select(name => AssemblyPurityRules.SignaturesMentionNone(assembly, name, AssemblyPurityRules.HostingAndPersistencePrefixes)))
            .ThrowIfFailed();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void ApiHasNoDbContext(string module) =>
        LayerRules.ApiHasNoDbContext(StarterSolution.All, RootOf(module)).ThrowIfFailed();

    [Fact]
    public void KernelApplicationReferencesNoHostingOrPersistenceAssembly() =>
        AssemblyPurityRules.ReferencesNone(StarterSolution.KernelApplication, AssemblyPurityRules.HostingAndPersistencePrefixes).ThrowIfFailed();

    [Fact]
    public void KernelDomainReferencesNoHostingOrPersistenceAssembly() =>
        AssemblyPurityRules.ReferencesNone(StarterSolution.KernelDomain, AssemblyPurityRules.HostingAndPersistencePrefixes).ThrowIfFailed();

    private static string RootOf(string module) => StarterSolution.RootNamespaceOf(StarterSolution.ModuleAssembly(module));
}
