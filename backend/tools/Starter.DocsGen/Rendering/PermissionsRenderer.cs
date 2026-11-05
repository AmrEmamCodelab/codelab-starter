using System.Text.Json;
using System.Text.Json.Serialization;
using Starter.DocsGen.Collection;

namespace Starter.DocsGen.Rendering;

/// <summary>Renders <c>permissions.json</c> (contract section 8 shape) and <c>permissions.md</c>.</summary>
internal static class PermissionsRenderer
{
    private const string Title = "Permissions";
    private const string Intro =
        "The permission catalog: the union of every module's `IPermissionCatalogSource`. Each key is a constant of its " +
        "module's `<Name>PermissionKeys` class; the frontend's `PermissionKeys` is generated from `permissions.json` " +
        "(`npm run api:gen`). The break-glass account holds every key; nobody else holds a key until it is granted.";

    private const string NoPermissions = "No module declares a permission.";
    private const string NoEndpoint = "none";
    private const string EndpointSeparator = ", ";

    private static readonly string[] Headers = ["Key", "Module", "Description", "Endpoints"];

    /// <summary><c>{"permissions":[{"key","module","description"}]}</c>, sorted by key.</summary>
    public static string RenderJson(IReadOnlyList<PermissionDoc> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var file = new PermissionsFile([.. Sorted(permissions).Select(permission =>
            new PermissionEntry(permission.Key, permission.Module, permission.Description))]);
        return JsonSerializer.Serialize(file, OpenApiNormalizer.WriterOptions) + MarkdownText.NewLine;
    }

    /// <summary>The catalog as a table, with the endpoints each key guards.</summary>
    public static string RenderMarkdown(IReadOnlyList<PermissionDoc> permissions, IReadOnlyList<EndpointDoc> endpoints)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(endpoints);
        var rows = Sorted(permissions).Select(permission => (IReadOnlyList<string>)
        [
            MarkdownText.Code(permission.Key),
            permission.Module,
            permission.Description,
            GuardedEndpoints(permission.Key, endpoints),
        ]);
        return MarkdownText.Document(Title).Line(Intro).Line().Table(Headers, rows, NoPermissions).ToString();
    }

    private static IEnumerable<PermissionDoc> Sorted(IEnumerable<PermissionDoc> permissions) =>
        permissions.OrderBy(permission => permission.Key, StringComparer.Ordinal).ThenBy(permission => permission.Module, StringComparer.Ordinal);

    private static string GuardedEndpoints(string key, IReadOnlyList<EndpointDoc> endpoints)
    {
        var guarded = endpoints.Where(endpoint => endpoint.Permissions.Contains(key, StringComparer.Ordinal))
            .Select(endpoint => MarkdownText.Code(endpoint.Method + " " + endpoint.Route))
            .ToList();
        return guarded.Count == 0 ? NoEndpoint : string.Join(EndpointSeparator, guarded);
    }

    private sealed record PermissionsFile([property: JsonPropertyName("permissions")] IReadOnlyList<PermissionEntry> Permissions);

    private sealed record PermissionEntry(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("module")] string Module,
        [property: JsonPropertyName("description")] string Description);
}
