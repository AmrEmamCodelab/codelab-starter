using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Codelab.Security.Signing;

/// <summary>
/// Puts <see cref="ResponseSignatureMiddleware"/> first in the pipeline (before the host's exception handler and
/// status-code pages), so the signature is computed over the body that is actually sent. Placed inside
/// <c>UseCodelabSecurity</c> it would hash the empty body of a 401/403/500 and the problem body an outer middleware
/// writes afterwards would arrive with a signature that no client can verify.
/// </summary>
internal sealed class ResponseSigningStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<ResponseSignatureMiddleware>();
        next(app);
    };
}
