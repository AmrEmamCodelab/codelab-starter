using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;

namespace Codelab.Security.PublicIds;

/// <summary>
/// Built-in OpenAPI (Microsoft.AspNetCore.OpenApi) schema transformer: every <c>PublicId&lt;T&gt;</c> is documented as
/// an opaque string with the token pattern, never as an object or a number. Replaces the Swashbuckle schema filter.
/// </summary>
public sealed class PublicIdSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <summary>The description written on every PublicId schema.</summary>
    public const string Description =
        "An opaque identifier: 'm1', the key id and 48 base64url characters, separated by underscores. Server-minted and bound to one identifier family; a token of another family is refused like a malformed one. No identifier has a numeric spelling on the wire.";

    /// <summary>A shape-only example (it authenticates under no key).</summary>
    public static readonly string Example = PublicIdFormat.TokenPrefix + "k1" + PublicIdFormat.Separator + new string('A', PublicIdFormat.PayloadCharacters);

    /// <inheritdoc />
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);
        if (!PublicIdTypes.TryGetDomain(context.JsonTypeInfo.Type, out _, out var nullable))
        {
            return Task.CompletedTask;
        }

        schema.Type = nullable ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
        schema.Format = null;
        schema.Properties?.Clear();
        schema.Required?.Clear();
        schema.Pattern = PublicIdFormat.OpenApiPattern;
        schema.Examples = [System.Text.Json.Nodes.JsonValue.Create(Example)];
        schema.Description = Description;
        return Task.CompletedTask;
    }
}

/// <summary>Registers PublicId binding for MVC, the route constraint and the OpenAPI transformer.</summary>
public static class PublicIdBindingExtensions
{
    /// <summary>
    /// Adds the <c>publicid</c> route constraint and the MVC model binder provider (first, once). Minimal APIs need
    /// nothing: they bind through <c>PublicId&lt;T&gt;.TryParse</c>.
    /// </summary>
    public static IServiceCollection AddPublicIdBinding(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure<RouteOptions>(options => options.ConstraintMap[PublicIdRouteConstraint.ConstraintName] = typeof(PublicIdRouteConstraint));
        services.Configure<MvcOptions>(options =>
        {
            if (!options.ModelBinderProviders.Any(provider => provider is PublicIdModelBinderProvider))
            {
                options.ModelBinderProviders.Insert(0, new PublicIdModelBinderProvider());
            }
        });
        return services;
    }

    /// <summary>Adds <see cref="PublicIdSchemaTransformer"/> to an OpenAPI document.</summary>
    public static OpenApiOptions AddCodelabPublicIds(this OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.AddSchemaTransformer<PublicIdSchemaTransformer>();
    }
}
