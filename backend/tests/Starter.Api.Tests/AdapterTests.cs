using System.Net;
using System.Security.Claims;
using Codelab.Errors;
using Codelab.Security.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Application.Results;
using Starter.Domain.Localization;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Localization;
using Starter.Infrastructure.Results;
using Starter.Infrastructure.Users;

namespace Starter.Api.Tests;

public sealed class HttpCurrentLanguageTests
{
    [Theory]
    [InlineData(null, AppLanguage.Arabic)]
    [InlineData("", AppLanguage.Arabic)]
    [InlineData("fr-FR", AppLanguage.Arabic)]
    [InlineData("en", AppLanguage.English)]
    [InlineData("en-US,en;q=0.9", AppLanguage.English)]
    [InlineData("ar-SA", AppLanguage.Arabic)]
    [InlineData("fr;q=1, en;q=0.8, ar;q=0.5", AppLanguage.English)]
    [InlineData("ar;q=0.4, en;q=0.6", AppLanguage.English)]
    [InlineData("en;q=0, ar;q=0.1", AppLanguage.Arabic)]
    [InlineData("EN", AppLanguage.English)]
    public void Resolve_picks_the_best_supported_language(string? acceptLanguage, AppLanguage expected)
    {
        Assert.Equal(expected, HttpCurrentLanguage.Resolve(acceptLanguage));
    }

    [Fact]
    public void Without_a_request_the_language_is_Arabic()
    {
        Assert.Equal(AppLanguage.Arabic, new HttpCurrentLanguage(new HttpContextAccessor()).Language);
    }

    [Fact]
    public void The_request_header_is_read()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = "en";

        Assert.Equal(AppLanguage.English, new HttpCurrentLanguage(new HttpContextAccessor { HttpContext = context }).Language);
    }
}

public sealed class HttpUserAdaptersTests
{
    [Fact]
    public void An_anonymous_request_has_no_subject_or_session()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var user = new HttpCurrentUserContext(accessor);

        Assert.False(user.IsAuthenticated);
        Assert.Null(user.SubjectId);
        Assert.Null(user.SessionId);
    }

    [Fact]
    public void An_authenticated_request_exposes_subject_and_session()
    {
        var subject = Guid.NewGuid();
        var accessor = new HttpContextAccessor { HttpContext = Authenticated(subject.ToString(), "sid-1") };
        var user = new HttpCurrentUserContext(accessor);

        Assert.True(user.IsAuthenticated);
        Assert.Equal(subject, user.SubjectId);
        Assert.Equal("sid-1", user.SessionId);
    }

    [Fact]
    public void A_malformed_subject_reads_as_null()
    {
        var accessor = new HttpContextAccessor { HttpContext = Authenticated("not-a-guid", "sid-1") };

        Assert.Null(new HttpCurrentUserContext(accessor).SubjectId);
    }

    [Fact]
    public void Audit_context_carries_subject_and_address_inside_a_request()
    {
        var subject = Guid.NewGuid();
        var context = Authenticated(subject.ToString(), "sid-1");
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        var accessor = new HttpContextAccessor { HttpContext = context };

        var audit = new HttpAuditContextAccessor(accessor, new HttpCurrentUserContext(accessor)).Current;

        Assert.Equal(subject, audit.SubjectId);
        Assert.Equal(IPAddress.Loopback.ToString(), audit.ClientAddress);
    }

    [Fact]
    public void Audit_context_outside_a_request_is_the_system_actor()
    {
        var accessor = new HttpContextAccessor();

        var audit = new HttpAuditContextAccessor(accessor, new HttpCurrentUserContext(accessor)).Current;

        Assert.True(audit.IsSystem);
        Assert.Null(audit.ClientAddress);
    }

    private static DefaultHttpContext Authenticated(string subject, string sessionId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(SecurityClaimTypes.Subject, subject), new Claim(SecurityClaimTypes.SessionId, sessionId)],
            authenticationType: "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }
}

public sealed class AuthorizationDefaultsTests
{
    [Fact]
    public void HasPermission_is_its_own_requirement()
    {
        var attribute = new HasPermissionAttribute("dashboard.view");

        Assert.Same(attribute, Assert.Single(attribute.GetRequirements()));
        Assert.Equal("dashboard.view", attribute.Permission);
        Assert.Null(attribute.Policy);
        Assert.IsAssignableFrom<IEndpointAccessDeclaration>(attribute);
        Assert.IsAssignableFrom<IEndpointAccessDeclaration>(new AuthenticatedUserAttribute());
    }

    [Fact]
    public void HasPermission_rejects_a_blank_key()
    {
        Assert.ThrowsAny<ArgumentException>(() => new HasPermissionAttribute(" "));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public void Fallback_requires_authentication_on_routed_requests_only(bool authenticated, bool routed, bool satisfied)
    {
        var http = new DefaultHttpContext();
        if (routed)
        {
            http.SetEndpoint(new Endpoint(requestDelegate: null, EndpointMetadataCollection.Empty, displayName: "probe"));
        }

        var identity = authenticated ? new ClaimsIdentity(authenticationType: "test") : new ClaimsIdentity();
        var context = new AuthorizationHandlerContext([new AuthenticatedOrUnroutedRequirement()], new ClaimsPrincipal(identity), http);

        Assert.Equal(satisfied, AuthenticatedOrUnroutedRequirement.IsSatisfied(context));
    }

    [Fact]
    public void The_no_credentials_scheme_is_the_default_only_when_none_is_set()
    {
        var unset = new AuthenticationOptions();
        var set = new AuthenticationOptions { DefaultScheme = "Bearer" };

        AuthorizationRegistration.UseNoCredentialsWhenUnset(unset);
        AuthorizationRegistration.UseNoCredentialsWhenUnset(set);

        Assert.Equal(NoCredentialsAuthenticationHandler.SchemeName, unset.DefaultScheme);
        Assert.Equal("Bearer", set.DefaultScheme);
    }
}

public sealed class OutcomeResultsTests
{
    [Fact]
    public void A_success_is_200_with_the_mapped_body()
    {
        var result = Outcome.Success(value: 3).ToActionResult(value => value * 2);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(6, ok.Value);
    }

    [Fact]
    public void A_success_without_body_is_204()
    {
        Assert.IsType<NoContentResult>(Outcome.Success(Done.Value).ToNoContentResult());
    }

    [Theory]
    [InlineData(RefusalCategory.Invalid, StatusCodes.Status400BadRequest)]
    [InlineData(RefusalCategory.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(RefusalCategory.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(RefusalCategory.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(RefusalCategory.Unprocessable, StatusCodes.Status422UnprocessableEntity)]
    public void Each_category_maps_to_its_status_with_the_same_code(RefusalCategory category, int status)
    {
        var refusal = category switch
        {
            RefusalCategory.Invalid => Refusal.Invalid("SAMPLE"),
            RefusalCategory.Forbidden => Refusal.Forbidden("SAMPLE"),
            RefusalCategory.NotFound => Refusal.NotFound("SAMPLE"),
            RefusalCategory.Conflict => Refusal.Conflict("SAMPLE"),
            _ => Refusal.Unprocessable("SAMPLE"),
        };

        CodedRefusal coded = refusal.ToCodedRefusal();

        Assert.Equal(status, coded.Status);
        Assert.Equal("SAMPLE", coded.Code);
    }
}
