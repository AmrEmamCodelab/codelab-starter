namespace Starter.DocsGen.Collection;

/// <summary>Everything the generated docs describe, read from the running host and its assemblies.</summary>
/// <param name="OpenApiJson">The raw <c>/openapi/v1.json</c> document as served.</param>
/// <param name="Permissions">The permission catalog (the union of every module's source).</param>
/// <param name="Endpoints">Every routed endpoint, one row per HTTP method.</param>
/// <param name="ErrorCodes">Every constant of every <c>*ErrorCodes</c> class.</param>
/// <param name="ConfigKeys">Every configuration key of every options class with a <c>SectionName</c>.</param>
/// <param name="Statuses">Every public <c>*Status</c> enum of the host's own assemblies (kernel and modules).</param>
/// <param name="Workflows">Every record type's workflow catalog the host registers, sorted by record type (none is valid).</param>
internal sealed record ApiSurface(
    string OpenApiJson,
    IReadOnlyList<PermissionDoc> Permissions,
    IReadOnlyList<EndpointDoc> Endpoints,
    IReadOnlyList<ErrorCodeDoc> ErrorCodes,
    IReadOnlyList<ConfigKeyDoc> ConfigKeys,
    IReadOnlyList<StatusDoc> Statuses,
    IReadOnlyList<WorkflowDoc> Workflows);

/// <summary>One permission of the catalog.</summary>
/// <param name="Key">The permission key.</param>
/// <param name="Module">The module (or other owner) whose catalog source declares it.</param>
/// <param name="Description">What holding it allows.</param>
internal sealed record PermissionDoc(string Key, string Module, string Description);

/// <summary>Who may call an endpoint.</summary>
internal enum EndpointAccessKind
{
    /// <summary>Anyone (allowlisted as anonymous).</summary>
    Anonymous = 1,

    /// <summary>Any signed-in user, no permission (allowlisted as authenticated).</summary>
    Authenticated = 2,

    /// <summary>A signed-in user holding every listed permission.</summary>
    Permission = 3,
}

/// <summary>One routed endpoint for one HTTP method.</summary>
/// <param name="Method">The HTTP method, or <see cref="EndpointCollector.AnyMethod"/>.</param>
/// <param name="Route">The route template with a leading slash.</param>
/// <param name="Access">Who may call it.</param>
/// <param name="Permissions">The required permission keys (all of them), sorted; empty unless <see cref="EndpointAccessKind.Permission"/>.</param>
/// <param name="Owner">The module, the kernel or the package that maps it.</param>
/// <param name="Summary">One line from the endpoint summary metadata or the action's XML doc.</param>
internal sealed record EndpointDoc(
    string Method,
    string Route,
    EndpointAccessKind Access,
    IReadOnlyList<string> Permissions,
    string Owner,
    string Summary);

/// <summary>One error code constant.</summary>
/// <param name="Code">The wire value.</param>
/// <param name="Constant">The declaring class and field, for example <c>AccessErrorCodes.SubjectRequired</c>.</param>
/// <param name="Owner">The module, the kernel or the package that declares it.</param>
/// <param name="Description">The field's XML doc summary.</param>
internal sealed record ErrorCodeDoc(string Code, string Constant, string Owner, string Description);

/// <summary>One configuration key.</summary>
/// <param name="Key">The full key, <c>:</c>-separated; <c>&lt;n&gt;</c> marks an array index and <c>&lt;name&gt;</c> a dictionary key.</param>
/// <param name="Type">A readable type name.</param>
/// <param name="Owner">The module, the kernel or the package whose options class binds it.</param>
/// <param name="Section">The options class's <c>SectionName</c>.</param>
/// <param name="DefaultValue">The value a fresh options instance carries, or empty when there is none.</param>
/// <param name="Description">The property's XML doc summary.</param>
internal sealed record ConfigKeyDoc(string Key, string Type, string Owner, string Section, string DefaultValue, string Description);

/// <summary>One status enum (an int enum whose name ends with <c>Status</c>) with its members.</summary>
/// <param name="Name">The enum's simple name, for example <c>JobRunStatus</c>.</param>
/// <param name="FullName">The enum's full name (namespace and nesting), which tells two same-named enums apart.</param>
/// <param name="Owner">The module or the kernel that declares it.</param>
/// <param name="Description">The enum's XML doc summary.</param>
/// <param name="Members">Its members, ordered by value, then name.</param>
internal sealed record StatusDoc(string Name, string FullName, string Owner, string Description, IReadOnlyList<StatusMemberDoc> Members);

/// <summary>One member of a status enum.</summary>
/// <param name="Value">The integer stored and sent on the wire.</param>
/// <param name="Name">The member name (never a label: labels come from i18n).</param>
/// <param name="Description">The member's XML doc summary.</param>
internal sealed record StatusMemberDoc(int Value, string Name, string Description);

/// <summary>Where the surface comes from: the real host in the tool, a fixture in tests.</summary>
internal interface IApiSurfaceSource
{
    /// <summary>Reads the surface.</summary>
    /// <exception cref="InvalidOperationException">The source cannot be read (for example the host does not start).</exception>
    Task<ApiSurface> CollectAsync(CancellationToken cancellationToken);
}
