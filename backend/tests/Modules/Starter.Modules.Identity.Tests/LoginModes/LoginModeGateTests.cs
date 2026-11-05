using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Results;
using Starter.Modules.Identity.Api;
using Starter.Modules.Identity.Api.LoginModes;
using Starter.Modules.Identity.Application.LoginModes;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Tests.LoginModes;

public sealed class LoginModeGateTests
{
    [Fact]
    public void A_disabled_mode_short_circuits_before_the_action()
    {
        var context = ResourceContext();
        var filter = Filter(SignInMode.Sso, new FakeLoginModes(SignInMode.Basic));

        filter.OnResourceExecuting(context);

        Assert.NotNull(context.Result);
    }

    [Fact]
    public void An_enabled_mode_lets_the_request_through()
    {
        var context = ResourceContext();
        var filter = Filter(SignInMode.Sso, new FakeLoginModes(SignInMode.Sso));

        filter.OnResourceExecuting(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void Basic_login_has_no_mode_gate_so_break_glass_can_always_reach_it()
    {
        Assert.Null(typeof(AuthController).GetCustomAttribute<RequiresLoginModeAttribute>());
        Assert.Null(typeof(AuthController).GetMethod(nameof(AuthController.LoginAsync))!.GetCustomAttribute<RequiresLoginModeAttribute>());
    }

    [Fact]
    public void The_login_mode_endpoints_carry_their_gate()
    {
        Assert.Equal(SignInMode.ActiveDirectory, typeof(DirectoryLoginController).GetCustomAttribute<RequiresLoginModeAttribute>()!.Mode);
        Assert.Equal(SignInMode.Sso, typeof(SsoController).GetCustomAttribute<RequiresLoginModeAttribute>()!.Mode);
        Assert.Null(typeof(LoginModesController).GetCustomAttribute<RequiresLoginModeAttribute>());
    }

    private static IResourceFilter Filter(SignInMode mode, ILoginModes modes)
    {
        var services = new ServiceCollection().AddSingleton(modes).BuildServiceProvider();
        return (IResourceFilter)new RequiresLoginModeAttribute(mode).CreateInstance(services);
    }

    private static ResourceExecutingContext ResourceContext() =>
        new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [], []);
}

public sealed class LoginModeHttpTests
{
    [Fact]
    public void The_sign_in_cookie_is_named_per_state_and_scoped_to_the_sso_routes()
    {
        var first = SsoPendingCookie.NameFor("state-a");
        var second = SsoPendingCookie.NameFor("state-b");
        var options = SsoPendingCookie.Options(isHttps: true);

        Assert.StartsWith(SsoPendingCookie.NamePrefix, first, StringComparison.Ordinal);
        Assert.Equal(SsoPendingCookie.NamePrefix.Length + SsoPendingCookie.NameSuffixLength, first.Length);
        Assert.NotEqual(first, second);
        Assert.Equal(first, SsoPendingCookie.NameFor("state-a"));
        Assert.DoesNotContain("state-a", first, StringComparison.Ordinal);
        Assert.Equal(("/api/auth/sso", true, true, SameSiteMode.Lax, SsoLimits.PendingLifetime), (options.Path, options.HttpOnly, options.Secure, options.SameSite, options.MaxAge!.Value));
        Assert.False(SsoPendingCookie.Options(isHttps: false).Secure);
    }

    [Theory]
    [InlineData(LoginModeErrorCodes.DirectoryUnavailable)]
    [InlineData(LoginModeErrorCodes.SsoUnavailable)]
    public void An_unreachable_authority_is_a_503(string code)
    {
        var result = LoginModeResults.ToActionResult(Outcome.Refused<int>(Refusal.Unprocessable(code)), value => value);

        Assert.IsAssignableFrom<ActionResult>(result.Result);
        Assert.IsNotType<OkObjectResult>(result.Result);
        Assert.Contains("503", StatusOf(result.Result!), StringComparison.Ordinal);
    }

    [Fact]
    public void Other_refusals_and_successes_map_as_usual()
    {
        Assert.IsType<OkObjectResult>(LoginModeResults.ToActionResult(Outcome.Success(1), value => value).Result);
        Assert.DoesNotContain("503", StatusOf(LoginModeResults.ToActionResult(Outcome.Refused<int>(Refusal.Invalid("X")), value => value).Result!), StringComparison.Ordinal);
    }

    [Fact]
    public void The_modes_response_mirrors_the_query() =>
        Assert.Equal(new LoginModesResponse(Basic: true, ActiveDirectory: false, Sso: true), LoginModesResponse.From(new EnabledLoginModes(Basic: true, ActiveDirectory: false, Sso: true)));

    private static string StatusOf(ActionResult result)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor())).GetAwaiter().GetResult();
        return context.Response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
