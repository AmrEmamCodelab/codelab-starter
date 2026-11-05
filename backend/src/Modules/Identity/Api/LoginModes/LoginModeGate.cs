using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Results;
using Starter.Infrastructure.Results;
using Starter.Modules.Identity.Application.LoginModes;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Api.LoginModes;

/// <summary>
/// The controller or action belongs to one login mode: while <c>Auth:Modes</c> does not list it, every request answers
/// 404 <see cref="LoginModeErrorCodes.LoginModeDisabled"/> (problem+json) before the body is read or the controller is
/// created. Not an authorization control: the endpoint keeps its own access attribute. Basic login
/// (<c>POST /api/auth/login</c>) has no gate: it stays open for the break-glass administrator and answers the same 404
/// for everyone else (<c>BasicLoginAccounts</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequiresLoginModeAttribute(SignInMode mode) : Attribute, IFilterFactory
{
    /// <summary>The mode the endpoint belongs to.</summary>
    public SignInMode Mode { get; } = mode;

    /// <inheritdoc />
    public bool IsReusable => true;

    /// <inheritdoc />
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        new LoginModeGateFilter(Mode, serviceProvider.GetRequiredService<ILoginModes>());
}

/// <summary>The resource filter behind <see cref="RequiresLoginModeAttribute"/>.</summary>
public sealed class LoginModeGateFilter(SignInMode mode, ILoginModes modes) : IResourceFilter
{
    /// <inheritdoc />
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!modes.IsEnabled(mode))
        {
            context.Result = Refusal.NotFound(LoginModeErrorCodes.LoginModeDisabled).ToActionResult();
        }
    }

    /// <inheritdoc />
    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
