using Codelab.Testing.Architecture;
using Starter.Architecture.Tests.Fixtures;
using Starter.Architecture.Tests.Fixtures.Api;
using Starter.Architecture.Tests.Fixtures.Application;
using Starter.Architecture.Tests.Support;

namespace Starter.Architecture.Tests;

/// <summary>Rule <c>base.thin-controllers</c> over every discovered module, plus proofs that the check catches a fat controller.</summary>
public sealed class ControllerTests
{
    public static TheoryData<string> Modules => StarterSolution.ModuleNames;

    [Theory]
    [MemberData(nameof(Modules))]
    public void ModuleControllersAreThin(string module)
    {
        var assembly = StarterSolution.ModuleAssembly(module);
        var root = StarterSolution.RootNamespaceOf(assembly);
        var controllers = ControllerShape.ControllersIn(assembly);
        if (controllers.Count == 0)
        {
            return; // a module without endpoints is allowed: nothing to check
        }

        ControllerShape.Check(controllers, apiNamespace: $"{root}.{LayerNames.Api}", applicationNamespace: $"{root}.{LayerNames.Application}").ThrowIfFailed();
    }

    [Fact]
    public void AThinControllerPasses()
    {
        var result = ControllerShape.Check([typeof(ThinController)], apiNamespace: FixtureNamespaces.Api, applicationNamespace: FixtureNamespaces.Application);

        Assert.True(result.IsSuccessful, result.Describe());
    }

    [Fact]
    public void AnUnsealedControllerReceivingAPortWithAnUntaggedActionFails()
    {
        var result = ControllerShape.Check([typeof(FatController)], apiNamespace: FixtureNamespaces.Api, applicationNamespace: FixtureNamespaces.Application);

        Assert.Contains(result.Violations, violation => violation.Contains("is not sealed", StringComparison.Ordinal));
        Assert.Contains(result.Violations, violation => violation.Contains(nameof(IFixtureRepository), StringComparison.Ordinal));
        Assert.Contains(result.Violations, violation => violation.Contains(nameof(FatController.Untagged), StringComparison.Ordinal));
    }

    [Fact]
    public void AControllerOutsideTheApiLayerFails()
    {
        var result = ControllerShape.Check([typeof(MisplacedController)], apiNamespace: FixtureNamespaces.Api, applicationNamespace: FixtureNamespaces.Application);

        Assert.Contains(result.Violations, violation => violation.Contains("is outside", StringComparison.Ordinal));
    }

    [Fact]
    public void ControllersInFindsOnlyConcreteControllers()
    {
        var controllers = ControllerShape.ControllersIn(typeof(ThinController).Assembly);

        Assert.Contains(typeof(ThinController), controllers);
        Assert.DoesNotContain(typeof(FixtureUseCase), controllers);
    }
}
