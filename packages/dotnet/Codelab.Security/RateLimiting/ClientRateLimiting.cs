using System.Threading.RateLimiting;
using Codelab.Security.Problems;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Codelab.Security.RateLimiting;

/// <summary>Wires the per-client policy (and the optional global limiter) into ASP.NET Core rate limiting.</summary>
public static class ClientRateLimiting
{
    /// <summary>The refusal written when a client is over its window.</summary>
    public static readonly SecurityProblem Refusal = new(StatusCodes.Status429TooManyRequests, "Too many requests.", SecurityProblemCodes.RateLimited);

    /// <summary>Adds the <see cref="ClientRateLimitOptions.PolicyName"/> policy, the 429 problem response and, when configured, the global limiter.</summary>
    public static void Configure(RateLimiterOptions limiter, ClientAddressResolver resolver, IOptions<ClientRateLimitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(limiter);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(options);
        limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        limiter.OnRejected = static (context, _) => new ValueTask(SecurityProblemWriter.TryWriteAsync(context.HttpContext, Refusal));
        limiter.AddPolicy(ClientRateLimitOptions.PolicyName, resolver.PartitionFor);
        if (options.Value.ApplyGlobally)
        {
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(resolver.PartitionFor);
        }
    }
}
