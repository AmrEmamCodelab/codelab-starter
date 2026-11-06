using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Starter.Modules.Attachments.Tests.Api;

/// <summary>What an executed MVC result wrote: status, headers and body.</summary>
internal sealed record WrittenResponse(int Status, IHeaderDictionary Headers, byte[] Body)
{
    private const string CodeMember = "code";

    /// <summary>The <c>code</c> of a problem+json body.</summary>
    public string? ProblemCode()
    {
        using var document = JsonDocument.Parse(Body);
        return document.RootElement.TryGetProperty(CodeMember, out var code) ? code.GetString() : null;
    }
}

/// <summary>Runs an MVC result against an in-memory response (the problem envelope and the file stream executor only).</summary>
internal static class ResultProbe
{
    public static async Task<WrittenResponse> ExecuteAsync(IActionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IActionResultExecutor<FileStreamResult>, FileStreamResultExecutor>()
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var body = new MemoryStream();
        context.Response.Body = body;

        await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));

        return new WrittenResponse(context.Response.StatusCode, context.Response.Headers, body.ToArray());
    }

    /// <summary>The status and code of a refusal result (problem+json).</summary>
    public static async Task<(int Status, string? Code)> RefusalOf(IActionResult? result)
    {
        Assert.NotNull(result);
        var written = await ExecuteAsync(result);
        return (written.Status, written.ProblemCode());
    }

    /// <summary>The value of a 200 or 201 object result with <paramref name="status"/>.</summary>
    public static T ValueOf<T>(ActionResult<T> result, int status)
    {
        ArgumentNullException.ThrowIfNull(result);
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(status, objectResult.StatusCode ?? StatusCodes.Status200OK);
        return Assert.IsAssignableFrom<T>(objectResult.Value);
    }
}
