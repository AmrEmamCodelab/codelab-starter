using Codelab.Observability;
using Codelab.Security;
using Codelab.Security.Signing;
using Microsoft.AspNetCore.Builder;
using Starter.Infrastructure.Modularity;
using Starter.Infrastructure.Security;

namespace Starter.Infrastructure.Hosting;

/// <summary>The request pipeline and the endpoint map every host uses, in the order Codelab.Security requires.</summary>
public static class StarterPipeline
{
    /// <summary>
    /// Checks the CORS origins first (<see cref="CorsOriginBootCheck"/>: a bad origin throws, so the host never
    /// starts), then adds, in order: CORS (before authentication, so preflights answer), routing, authentication, the
    /// user log context (UserIdHash), Codelab.Security (rate limit, sid revocation and session-version checks on every
    /// authenticated request, request and response signing) and authorization. Call it after
    /// <c>UseCodelabObservability</c> and <c>UseCodelabErrors</c>.
    /// </summary>
    public static WebApplication UseStarterPipeline(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        CorsOriginBootCheck.EnsureValid(app);
        app.UseCors();
        app.UseRouting();
        app.UseAuthentication();
        app.UseCodelabUserLogContext();
        app.UseCodelabSecurity();
        app.UseAuthorization();
        return app;
    }

    /// <summary>
    /// Maps the module controllers and extras, the Codelab.Security signing-key endpoint, the anonymous CSP report
    /// sink (<see cref="SecurityEdgeRoutes.CspReports"/>) and the health endpoints.
    /// </summary>
    public static WebApplication MapStarterEndpoints(this WebApplication app, ModuleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(catalog);
        app.MapStarterModules(catalog);
        app.MapCodelabSigningKeyEndpoint();
        app.MapCspReportSink();
        app.MapCodelabHealth();
        return app;
    }
}
