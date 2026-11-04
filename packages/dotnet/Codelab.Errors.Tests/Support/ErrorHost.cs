using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Codelab.Observability;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace Codelab.Errors.Tests.Support;

/// <summary>A broken business rule the test host maps to 422 (stands in for a domain exception of Starter.Domain).</summary>
public sealed class RuleBrokenException : Exception;

/// <summary>An in-process host wired exactly as a Codelab host is: observability, errors, auth, MVC and minimal APIs.</summary>
public static class ErrorHost
{
    public const string ServiceName = "starter-errors-test";
    public const string UserId = "user-4711";
    private const int HashKeyBytes = 32;

    public static async Task<(WebApplication App, CaptureSink Sink)> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Observability:LogFile:Enabled"] = "false",
            ["Observability:UserIdHash:Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(HashKeyBytes)),
        });
        var sink = new CaptureSink();
        builder.Services.AddSingleton<ILogEventSink>(sink);
        ConfigureServices(builder);

        var app = builder.Build();
        ConfigurePipeline(app);
        await app.StartAsync();
        return (app, sink);
    }

    private static void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.AddCodelabObservability(new ObservabilityOptions { ServiceName = ServiceName, HostAssembly = typeof(ErrorHost).Assembly });
        builder.Services.AddCodelabErrors(errors => errors.Map<RuleBrokenException>(
            Microsoft.AspNetCore.Http.StatusCodes.Status422UnprocessableEntity, ErrorCodes.BusinessRule, LogLevel.Information));
        builder.Services.AddAuthentication(NeverAuthenticatedHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, NeverAuthenticatedHandler>(NeverAuthenticatedHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy(
            NeverAuthenticatedHandler.PolicyName,
            policy => policy.AddAuthenticationSchemes(NeverAuthenticatedHandler.SchemeName).RequireAuthenticatedUser()));
        builder.Services.AddControllers().AddApplicationPart(typeof(ErrorHost).Assembly);
    }

    private static void ConfigurePipeline(WebApplication app)
    {
        app.UseCodelabObservability();
        app.UseCodelabErrors();
        app.UseAuthentication();
        app.Use((context, next) =>
        {
            context.User = TestUsers.Authenticated(UserId);
            return next(context);
        });
        app.UseCodelabUserLogContext();
        app.UseAuthorization();
        TestEndpoints.Map(app);
        app.MapControllers();
    }
}

/// <summary>Collects every retained event.</summary>
public sealed class CaptureSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Events => [.. _events];

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    public string AllText() => string.Join(
        Environment.NewLine,
        Events.Select(e => string.Join(
            " | ",
            e.RenderMessage(CultureInfo.InvariantCulture),
            e.Exception?.ToString() ?? string.Empty,
            string.Join(";", e.Properties.Select(p => $"{p.Key}={p.Value}")))));
}
