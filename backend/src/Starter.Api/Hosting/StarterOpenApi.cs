using Codelab.Security.PublicIds;

namespace Starter.Api.Hosting;

/// <summary>The built-in OpenAPI document of the API (the source of docs/generated/openapi.v1.json and the TS client).</summary>
public static class StarterOpenApi
{
    /// <summary>The document name; served at <c>/openapi/v1.json</c>.</summary>
    public const string DocumentName = "v1";

    /// <summary>Registers document <see cref="DocumentName"/> with the Codelab.Security PublicId schema transformer.</summary>
    public static IServiceCollection AddStarterOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options => options.AddCodelabPublicIds());

    /// <summary>Maps the document in Development only; other environments publish the generated file instead.</summary>
    public static WebApplication MapStarterOpenApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();
        }

        return app;
    }
}
