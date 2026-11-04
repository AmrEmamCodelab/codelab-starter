using System.Reflection;
using Codelab.Testing.Architecture;
using Codelab.Testing.Checks;

namespace Codelab.Testing.Tests.Architecture;

/// <summary>
/// The module boundaries rules.compiled.json declares for the Codelab packages (mayDependOn / mustNotDependOn), checked
/// over the real package assemblies. Without this, a ProjectReference plus a using between two packages would pass CI
/// even though every generated CLAUDE.md says base.module-boundaries is enforced by an architecture test.
/// </summary>
public sealed class PackageBoundaryTests
{
    private static readonly Assembly[] PackageAssemblies =
    [
        typeof(Codelab.Security.SecuritySections).Assembly,
        typeof(Codelab.Errors.ErrorCodes).Assembly,
        typeof(Codelab.Observability.UserIdHash).Assembly,
        typeof(Codelab.Integration.Outbound.OutboundClientOptions).Assembly,
        typeof(CheckResult).Assembly,
    ];

    public static TheoryData<string> PackageNames => new(PackageAssemblies.Select(assembly => assembly.GetName().Name!));

    [Fact]
    public void Every_package_respects_its_declared_module_boundaries()
    {
        var result = ModuleBoundaryRules.CheckRepository(PackageAssemblies);

        result.ThrowIfFailed();
    }

    [Theory]
    [MemberData(nameof(PackageNames))]
    public void Each_package_is_actually_examined(string packageName)
    {
        var assembly = PackageAssemblies.Single(candidate => candidate.GetName().Name == packageName);

        var result = ModuleBoundaryRules.CheckRepository([assembly]);

        Assert.False(result.IsVacuous, $"{packageName}: no module of rules.compiled.json maps to this assembly's namespace, so its boundaries are not checked.");
    }
}
