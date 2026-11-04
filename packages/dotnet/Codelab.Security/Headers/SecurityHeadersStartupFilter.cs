using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Codelab.Security.Headers;

/// <summary>
/// Puts <see cref="SecurityHeadersMiddleware"/> first in the pipeline (before CORS, routing, authentication and the
/// exception handler), so every response carries the baseline: CORS preflights, 404s, 401s and handled errors too.
/// </summary>
internal sealed class SecurityHeadersStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<SecurityHeadersMiddleware>();
        next(app);
    };
}
