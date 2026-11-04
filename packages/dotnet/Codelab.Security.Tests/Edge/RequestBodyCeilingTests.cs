using System.Net;
using System.Text.Json;
using Codelab.Security.Problems;
using Codelab.Security.Requests;
using Codelab.Security.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace Codelab.Security.Tests.Edge;

[Collection(SharedCodecGroup.Name)]
public sealed class RequestBodyCeilingTests
{
    private const long Limit = 100;

    [Theory]
    [InlineData(101L, Limit, true)]
    [InlineData(100L, Limit, false)]
    [InlineData(null, Limit, false)]
    [InlineData(5000L, null, false)]
    public void Only_a_declared_length_above_the_limit_in_force_is_over(long? declared, long? limit, bool expected)
    {
        Assert.Equal(expected, RequestBodyCeiling.IsDeclaredOverLimit(Context(declared, limit)));
    }

    [Fact]
    public void The_mvc_filter_short_circuits_with_the_413_problem()
    {
        var http = Context(declared: 500, limit: Limit);
        var context = new ResourceExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [], []);

        new RefuseOversizedBodyAttribute().OnResourceExecuting(context);

        var result = Assert.IsType<SecurityProblemResult>(context.Result);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.Problem.Status);
        Assert.Equal(SecurityProblemCodes.PayloadTooLarge, result.Problem.Code);
    }

    [Fact]
    public void The_mvc_filter_lets_a_request_within_the_limit_through()
    {
        var context = new ResourceExecutingContext(new ActionContext(Context(declared: 10, limit: Limit), new RouteData(), new ActionDescriptor()), [], []);

        new RefuseOversizedBodyAttribute().OnResourceExecuting(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public async Task A_minimal_endpoint_answers_413_with_the_code_before_reading_the_body()
    {
        var options = SecurityTestHost.BaseOptions();
        options.MapEndpoints = endpoints => endpoints.MapPost("/upload", () => "read").RefuseOversizedBody();
        await using var host = await SecurityTestHost.StartAsync(options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/upload") { Content = new ByteArrayContent(new byte[200]) };
        request.Headers.Add(LimitHeader.Name, Limit.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(SecurityProblemCodes.PayloadTooLarge, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(SecurityProblemWriter.ProblemContentType, response.Content.Headers.ContentType?.MediaType);
    }

    private static DefaultHttpContext Context(long? declared, long? limit)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentLength = declared;
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(new FixedLimit(limit));
        return context;
    }

    internal sealed class FixedLimit(long? limit) : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;

        public long? MaxRequestBodySize { get; set; } = limit;
    }
}

/// <summary>TestServer has no body-size feature; this test header installs one so the ceiling has a limit to enforce.</summary>
internal static class LimitHeader
{
    public const string Name = "Test-Body-Limit";

    public static async Task Apply(HttpContext context, RequestDelegate next)
    {
        if (long.TryParse(context.Request.Headers[Name].ToString(), System.Globalization.CultureInfo.InvariantCulture, out var limit))
        {
            context.Features.Set<IHttpMaxRequestBodySizeFeature>(new RequestBodyCeilingTests.FixedLimit(limit));
        }

        await next(context);
    }
}
