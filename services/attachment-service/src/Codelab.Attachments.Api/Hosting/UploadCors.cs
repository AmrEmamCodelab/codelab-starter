using Codelab.Attachments.Api.Authentication;
using Codelab.Observability;
using Codelab.Security;
using Codelab.Security.Cors;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Codelab.Attachments.Api.Hosting;

/// <summary>The browser origins allowed to upload (<c>Attachments:Cors</c>); every other endpoint is same-origin only.</summary>
public sealed class UploadCorsOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Attachments:Cors";

    /// <summary>The default preflight cache duration in seconds.</summary>
    public const int DefaultPreflightMaxAgeSeconds = 600;

    /// <summary>Exact origins (scheme://host[:port]); https only outside Development, no wildcard outside Development.</summary>
    public IList<string> AllowedOrigins { get; init; } = [];

    /// <summary>Preflight cache duration in seconds.</summary>
    public int PreflightMaxAgeSeconds { get; set; } = DefaultPreflightMaxAgeSeconds;
}

/// <summary>
/// The CORS policy of <c>POST /api/uploads</c>: the configured origins, POST only, the ticket header and
/// <c>Content-Type</c>, no credentials (the ticket is the credential, never a cookie). Origins are validated at start
/// with Codelab.Security's <see cref="CorsOriginValidator"/>.
/// </summary>
public static class UploadCors
{
    /// <summary>The policy name.</summary>
    public const string PolicyName = "attachments-upload";

    /// <summary>The policy for <paramref name="options"/>; with no origins it grants nothing.</summary>
    public static CorsPolicy Build(UploadCorsOptions options, SecurityProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(protocol);
        return new CorsPolicyBuilder()
            .WithOrigins([.. options.AllowedOrigins])
            .WithMethods(HttpMethods.Post)
            .WithHeaders(HeaderNames.ContentType, UploadTickets.HeaderName(protocol))
            .WithExposedHeaders(CorrelationId.Header)
            .DisallowCredentials()
            .SetPreflightMaxAge(TimeSpan.FromSeconds(options.PreflightMaxAgeSeconds))
            .Build();
    }

    internal static IServiceCollection AddUploadCors(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<UploadCorsOptions>().Bind(configuration.GetSection(UploadCorsOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<UploadCorsOptions>, UploadCorsOptionsValidator>());
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<UploadCorsOptions>, SecurityProtocol>(
            (cors, options, protocol) => cors.AddPolicy(PolicyName, Build(options.Value, protocol)));
        return services;
    }
}

/// <summary>The upload origins pass the same validator as the API's CORS origins (credentials off).</summary>
internal sealed class UploadCorsOptionsValidator(IHostEnvironment environment) : IValidateOptions<UploadCorsOptions>
{
    public ValidateOptionsResult Validate(string? name, UploadCorsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.PreflightMaxAgeSeconds < 0)
        {
            return ValidateOptionsResult.Fail($"{UploadCorsOptions.SectionName}:{nameof(UploadCorsOptions.PreflightMaxAgeSeconds)} must not be negative.");
        }

        var asCodelab = new CodelabCorsOptions { AllowedOrigins = options.AllowedOrigins, AllowCredentials = false };
        var problems = CorsOriginValidator.Validate(asCodelab, environment.IsDevelopment())
            .Select(problem => problem.Replace(CodelabCorsOptions.SectionName, UploadCorsOptions.SectionName, StringComparison.Ordinal))
            .ToList();
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
