using System.Reflection;
using Codelab.Testing.Checks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Starter.Architecture.Tests.Support;

/// <summary>
/// Rule <c>base.thin-controllers</c> as far as reflection can see it: a controller is sealed, sits in the module's
/// <c>.Api</c> layer, receives only Application use cases (concrete classes of the module's <c>.Application</c> layer,
/// never a port, repository, unit of work or DbContext), and every public action names its HTTP method.
/// What an action does with its use case is left to review.
/// </summary>
public static class ControllerShape
{
    /// <summary>The controllers (concrete <see cref="ControllerBase"/> types) of <paramref name="assembly"/>.</summary>
    public static IReadOnlyList<Type> ControllersIn(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return [.. assembly.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(type))];
    }

    /// <summary>Checks every controller against the thin-controller shape.</summary>
    /// <param name="controllers">The controllers to check.</param>
    /// <param name="apiNamespace">The module's Api layer namespace.</param>
    /// <param name="applicationNamespace">The module's Application layer namespace.</param>
    public static CheckResult Check(IReadOnlyCollection<Type> controllers, string apiNamespace, string applicationNamespace)
    {
        ArgumentNullException.ThrowIfNull(controllers);
        var violations = controllers.SelectMany(controller => Violations(controller, apiNamespace, applicationNamespace)).ToList();
        return new CheckResult("Controllers are thin", violations, controllers.Count);
    }

    private static IEnumerable<string> Violations(Type controller, string apiNamespace, string applicationNamespace)
    {
        if (!controller.IsSealed)
        {
            yield return $"{controller.FullName} is not sealed";
        }

        if (controller.Namespace is null || !LayerPlacement.IsSameOrNested(controller.Namespace, apiNamespace))
        {
            yield return $"{controller.FullName} is outside {apiNamespace}";
        }

        foreach (var dependency in Dependencies(controller).Where(type => !IsUseCase(type, applicationNamespace)))
        {
            yield return $"{controller.FullName} receives {dependency.FullName}; a controller receives Application use cases only";
        }

        foreach (var action in Actions(controller).Where(action => !action.GetCustomAttributes().OfType<IActionHttpMethodProvider>().Any()))
        {
            yield return $"{controller.FullName}.{action.Name} declares no HTTP method attribute";
        }
    }

    private static IEnumerable<Type> Dependencies(Type controller) =>
        controller.GetConstructors().SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType).Distinct();

    private static bool IsUseCase(Type type, string applicationNamespace) =>
        type is { IsClass: true, IsAbstract: false }
        && type.Namespace is not null
        && LayerPlacement.IsSameOrNested(type.Namespace, applicationNamespace);

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null);
}
