using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codelab.Errors.Tests.Support;

/// <summary>The paths the end-to-end tests call.</summary>
public static class TestPaths
{
    public const string Unclassified = "/fail/unclassified";
    public const string RuleBroken = "/fail/rule";
    public const string Upstream = "/fail/upstream";
    public const string Busy = "/fail/busy";
    public const string Refused = "/refused";
    public const string Secure = "/secure";
    public const string Json = "/json";
    public const string LeakyProblem = "/leaky-problem";
    public const string MvcMissing = "/mvc/missing";
    public const string MvcLeakyProblem = "/mvc/leaky-problem";
    public const string MvcItems = "/mvc/items";

    /// <summary>A value that must never reach a client or a retained log.</summary>
    public const string PersonalValue = "jane@example.test";

    /// <summary>The code of the test refusal.</summary>
    public const string NotADraftCode = "RECORD_NOT_A_DRAFT";
}

/// <summary>Minimal-API endpoints, one per error path.</summary>
public static class TestEndpoints
{
    public sealed record Item(string Name);

    public static void Map(WebApplication app)
    {
        app.MapGet(TestPaths.Unclassified, IResult () =>
            throw new InvalidOperationException($"The duplicate key value is ({TestPaths.PersonalValue})."));
        app.MapGet(TestPaths.RuleBroken, IResult () => throw new RuleBrokenException());
        app.MapGet(TestPaths.Upstream, IResult () => throw new UpstreamUnavailableException("mail-relay"));
        app.MapGet(TestPaths.Busy, IResult () => throw new TimeoutException());
        app.MapGet(TestPaths.Refused, () => CodedRefusal.Conflict(TestPaths.NotADraftCode).ToResult());
        app.MapGet(TestPaths.Secure, () => Results.Ok()).RequireAuthorization(NeverAuthenticatedHandler.PolicyName);
        app.MapPost(TestPaths.Json, (Item item) => Results.Ok(item));
        app.MapGet(TestPaths.LeakyProblem, () => Results.Problem(
            detail: $"Lookup for {TestPaths.PersonalValue} failed: System.InvalidOperationException",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["exception"] = "System.InvalidOperationException: boom" }));
    }
}

/// <summary>MVC actions for the client-error mapping and model-binding paths.</summary>
[ApiController]
[Route("mvc")]
public sealed class TestController : ControllerBase
{
    public sealed class ItemRequest
    {
        [Required]
        public string? Name { get; set; }

        [Range(1, 10)]
        public int Count { get; set; }
    }

    [HttpGet("missing")]
    public IActionResult Missing() => NotFound();

    [HttpGet("leaky-problem")]
    public IActionResult LeakyProblem() =>
        Problem(detail: $"System.NullReferenceException for {TestPaths.PersonalValue}", statusCode: StatusCodes.Status409Conflict);

    [HttpPost("items")]
    public IActionResult Create(ItemRequest request) => Ok(request);
}

/// <summary>The principal the test host signs every request in with (after authentication ran).</summary>
public static class TestUsers
{
    public static ClaimsPrincipal Authenticated(string userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], NeverAuthenticatedHandler.SchemeName));
}

/// <summary>A scheme that never authenticates, so a protected endpoint answers the framework's empty 401.</summary>
public sealed class NeverAuthenticatedHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "never";
    public const string PolicyName = "authenticated-by-scheme";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}
