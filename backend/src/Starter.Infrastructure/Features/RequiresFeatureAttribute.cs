using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;

namespace Starter.Infrastructure.Features;

/// <summary>
/// The controller or action exists only while the feature flag is on. With the flag off the route is never
/// registered, so a caller gets a plain 404 and the API cannot be probed for a capability the deployment does not
/// have. Write the flag as a constant of the module's feature flag class. Not an authorization control: the endpoint
/// still carries its permission attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequiresFeatureAttribute(string flag) : Attribute
{
    /// <summary>The flag name.</summary>
    public string Flag { get; } = flag;
}

/// <summary>
/// Removes the controllers and actions whose <see cref="RequiresFeatureAttribute"/> flag is off from the MVC
/// application model, once, when the model is built. An undeclared flag throws (see <see cref="IFeatureGate.IsEnabled"/>).
/// </summary>
public sealed class FeatureGateConvention(IFeatureGate gate) : IApplicationModelConvention
{
    /// <inheritdoc />
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);
        foreach (var controller in application.Controllers.ToList())
        {
            if (!IsOn(controller.Attributes))
            {
                application.Controllers.Remove(controller);
                continue;
            }

            foreach (var action in controller.Actions.Where(action => !IsOn(action.Attributes)).ToList())
            {
                controller.Actions.Remove(action);
            }
        }
    }

    private bool IsOn(IReadOnlyList<object> attributes) =>
        attributes.OfType<RequiresFeatureAttribute>().FirstOrDefault() is not { } required || gate.IsEnabled(required.Flag);
}

/// <summary>Adds <see cref="FeatureGateConvention"/> to MVC.</summary>
public sealed class FeatureGateMvcSetup(IFeatureGate gate) : IConfigureOptions<MvcOptions>
{
    /// <inheritdoc />
    public void Configure(MvcOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Conventions.Add(new FeatureGateConvention(gate));
    }
}
