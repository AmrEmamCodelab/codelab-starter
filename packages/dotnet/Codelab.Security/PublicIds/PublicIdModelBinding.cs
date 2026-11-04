using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Codelab.Security.PublicIds;

/// <summary>
/// The MVC model binder for <c>PublicId&lt;T&gt;</c> route and query parameters (parity with minimal-API
/// <c>TryParse</c> binding). The route value wins over the query; a repeated query key, a decimal, a malformed or
/// inauthentic token and a token of another family are all refused with one message that never echoes the value.
/// An absent value binds null on a nullable parameter and is refused on a non-nullable one.
/// </summary>
public sealed class PublicIdModelBinder<TDomain>(bool nullable) : IModelBinder
    where TDomain : IIdDomain
{
    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);
        var name = string.IsNullOrEmpty(bindingContext.ModelName) ? bindingContext.FieldName : bindingContext.ModelName;
        if (!TryReadSingleValue(bindingContext, name, out var supplied))
        {
            return Reject(bindingContext, name);
        }

        if (supplied is null)
        {
            return nullable ? Bind(bindingContext, model: null) : Reject(bindingContext, name);
        }

        return PublicId<TDomain>.TryParse(supplied, out var id) ? Bind(bindingContext, id) : Reject(bindingContext, name);
    }

    private static bool TryReadSingleValue(ModelBindingContext bindingContext, string name, out string? supplied)
    {
        supplied = null;
        if (bindingContext.ActionContext.RouteData.Values.TryGetValue(name, out var fromRoute) && fromRoute is string { Length: > 0 } segment)
        {
            supplied = segment;
            return true;
        }

        var fromQuery = bindingContext.HttpContext.Request.Query[name];
        if (fromQuery.Count > 1)
        {
            return false;
        }

        supplied = fromQuery.Count == 1 && !string.IsNullOrEmpty(fromQuery[0]) ? fromQuery[0] : null;
        return true;
    }

    private static Task Bind(ModelBindingContext bindingContext, object? model)
    {
        bindingContext.Result = ModelBindingResult.Success(model);
        return Task.CompletedTask;
    }

    private static Task Reject(ModelBindingContext bindingContext, string name)
    {
        bindingContext.ModelState.TryAddModelError(name, PublicIdFormat.UnreadableMessage);
        bindingContext.Result = ModelBindingResult.Failed();
        return Task.CompletedTask;
    }
}

/// <summary>Claims <c>PublicId&lt;T&gt;</c> parameters bound from the route or query; leaves body-bound ones to the body binder.</summary>
public sealed class PublicIdModelBinderProvider : IModelBinderProvider
{
    /// <inheritdoc />
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!AcceptsUrlData(context.BindingInfo.BindingSource) || !PublicIdTypes.TryGetDomain(context.Metadata.ModelType, out var domain, out var nullable))
        {
            return null;
        }

        return (IModelBinder)Activator.CreateInstance(typeof(PublicIdModelBinder<>).MakeGenericType(domain), [nullable])!;
    }

    private static bool AcceptsUrlData(BindingSource? source) =>
        source is null || source == BindingSource.Path || source == BindingSource.Query || source == BindingSource.ModelBinding;
}

/// <summary>
/// The <c>{id:publicid}</c> route constraint: matches the token shape only (authentication happens at binding), so a
/// decimal segment never reaches an action and a shaped-but-forged token is refused 400 by the binder, not 404.
/// </summary>
public sealed class PublicIdRouteConstraint : Microsoft.AspNetCore.Routing.IRouteConstraint
{
    /// <summary>The inline constraint name.</summary>
    public const string ConstraintName = "publicid";

    /// <inheritdoc />
    public bool Match(
        HttpContext? httpContext,
        Microsoft.AspNetCore.Routing.IRouter? route,
        string routeKey,
        Microsoft.AspNetCore.Routing.RouteValueDictionary values,
        Microsoft.AspNetCore.Routing.RouteDirection routeDirection)
    {
        ArgumentNullException.ThrowIfNull(routeKey);
        ArgumentNullException.ThrowIfNull(values);
        return values.TryGetValue(routeKey, out var value) && value is string text && PublicIdFormat.IsWellFormedToken(text);
    }
}
