using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.AspNetCore;
using Serilog.Events;

namespace Codelab.Observability;

/// <summary>The middleware half of Codelab.Observability, in pipeline order.</summary>
public static class CodelabObservabilityExtensions
{
    /// <summary>
    /// The edge half: the one Serilog request line per request (enriched with the correlation id, reference code and
    /// route template; query string never logged; quiet paths at Verbose), then <see cref="CorrelationIdMiddleware"/>.
    /// Place it FIRST in the pipeline, before <c>UseCodelabErrors</c>: the request line then records the final status of a
    /// handled failure without its exception, so a failure is logged once (by the exception handler). An exception that
    /// still escapes (the response had started) is logged here, at Error.
    /// </summary>
    public static IApplicationBuilder UseCodelabObservability(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var quietPaths = QuietPaths(app.ApplicationServices);

        // The host owns its logger (preserveStaticLogger), so the request line must use it, not the static Log.Logger.
        var hostLogger = app.ApplicationServices.GetService<Serilog.ILogger>();
        app.UseSerilogRequestLogging(options =>
        {
            options.Logger = hostLogger;
            ConfigureRequestLine(options, quietPaths);
        });
        app.UseMiddleware<CorrelationIdMiddleware>();
        return app;
    }

    /// <summary>The user half: the hashed user id on every line. Place it after <c>UseAuthentication</c>.</summary>
    public static IApplicationBuilder UseCodelabUserLogContext(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<LogUserContextMiddleware>();
        return app;
    }

    internal static void ConfigureRequestLine(RequestLoggingOptions options, IReadOnlyList<PathString> quietPaths)
    {
        options.IncludeQueryInRequestPath = false;
        options.GetLevel = (http, _, exception) => LevelFor(http, exception, quietPaths);
        options.EnrichDiagnosticContext = static (diagnosticContext, http) =>
        {
            CorrelationLogProperties.EnrichRequestLine(diagnosticContext, http);
            if (FailureFingerprint.RouteTemplateOf(http) is { } routeTemplate)
            {
                diagnosticContext.Set(LogPropertyNames.RouteTemplate, routeTemplate);
            }
        };
    }

    internal static LogEventLevel LevelFor(HttpContext http, Exception? exception, IReadOnlyList<PathString> quietPaths)
    {
        if (exception is not null)
        {
            return LogEventLevel.Error;
        }

        return TracingBootstrap.IsQuiet(http.Request.Path, quietPaths) ? LogEventLevel.Verbose : LogEventLevel.Information;
    }

    private static PathString[] QuietPaths(IServiceProvider services) =>
        (services.GetService<IOptions<QuietRequestPathsOptions>>()?.Value.Paths ?? [HealthEndpoints.BasePath])
            .Select(p => new PathString(p))
            .ToArray();
}

/// <summary>The quiet request paths recorded by <c>AddCodelabObservability</c> for the middleware.</summary>
internal sealed class QuietRequestPathsOptions
{
    public IReadOnlyList<string> Paths { get; set; } = [HealthEndpoints.BasePath];
}
