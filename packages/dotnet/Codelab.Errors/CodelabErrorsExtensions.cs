using Codelab.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Codelab.Errors;

/// <summary>The registration of the one error contract: every stage of docs/standards/ERROR-HANDLING.md in two calls.</summary>
public static class CodelabErrorsExtensions
{
    /// <summary>
    /// Registers the error contract: problem details with <see cref="ProblemDetailsNormalizer"/> as the customization,
    /// <see cref="GlobalExceptionHandler"/> (the only exception handler), the ordered <see cref="FailureClassifierTable"/>
    /// (also the <see cref="IFailureCodeResolver"/> of the job failure filter), and the MVC model-binding mapping
    /// (<see cref="ModelBindingErrors"/>, applied when the host adds controllers). Call after
    /// <c>AddCodelabObservability</c>, which registers the <see cref="UserIdHash"/>.
    /// </summary>
    public static IServiceCollection AddCodelabErrors(this IServiceCollection services, Action<CodelabErrorsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<CodelabErrorsOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.AddProblemDetails(problem => problem.CustomizeProblemDetails = ProblemDetailsNormalizer.Normalize);
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.TryAddSingleton<FailureClassifierTable>();
        services.Replace(ServiceDescriptor.Singleton<IFailureCodeResolver>(sp => sp.GetRequiredService<FailureClassifierTable>()));

        services.PostConfigure<MvcOptions>(mvc => ModelBindingErrors.Configure(mvc.ModelBindingMessageProvider));
        services.PostConfigure<ApiBehaviorOptions>(api => api.InvalidModelStateResponseFactory = ModelBindingErrors.Handle);
        return services;
    }

    /// <summary>
    /// The error middleware: the exception handler, then status-code pages for empty 4xx/5xx bodies. Place it right
    /// AFTER <c>UseCodelabObservability</c> and before everything else, so it catches what the rest of the pipeline throws
    /// while the request line (outside it) records the final status without logging the exception a second time.
    /// </summary>
    public static IApplicationBuilder UseCodelabErrors(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler();
        app.UseStatusCodePages(StatusCodePagesWriter.WriteAsync);
        return app;
    }
}
