using Microsoft.AspNetCore.Authorization;

namespace Codelab.Testing.Tests.Fixtures.Endpoints;

/// <summary>The fixture's permission attribute (a host's [HasPermission]).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class FixturePermissionAttribute(string permission) : AuthorizeAttribute
{
    public string Permission { get; } = permission;
}

/// <summary>The fixture's validator contract (a host's IValidator&lt;T&gt;).</summary>
public interface IFixtureValidator<in T>
{
    bool IsValid(T value);
}

/// <summary>The fixture's wire id (a host's PublicId&lt;T&gt;).</summary>
public readonly record struct WireId<T>(string Token);

/// <summary>A request body with a validator.</summary>
public sealed record RecordInput(string Title);

/// <summary>A request body without a validator.</summary>
public sealed record NoteInput(string Text);

/// <summary>A controller request body without a validator.</summary>
public sealed record OrderInput(int Quantity);

/// <summary>The validator of <see cref="RecordInput"/>.</summary>
public sealed class RecordInputValidator : IFixtureValidator<RecordInput>
{
    public bool IsValid(RecordInput value) => !string.IsNullOrWhiteSpace(value?.Title);
}

/// <summary>A response whose ids are mostly wire ids.</summary>
public sealed record RecordView(WireId<RecordView> Id, WireId<RecordView>? ParentId, int StatusId, int CategoryId, IReadOnlyList<LineView> Lines);

/// <summary>A nested response type with a raw numeric id.</summary>
public sealed record LineView(long ProductId, IReadOnlyList<WireId<LineView>> RelatedIds);

/// <summary>A response with only wire ids.</summary>
public sealed record ItemView(WireId<ItemView> Id, string Name);

/// <summary>Constants of the fixture endpoints.</summary>
public static class FixturePermissions
{
    public const string RecordsView = "records.view";
}

/// <summary>
/// Route templates with raw numeric constraints, the shape the PublicId contract refuses. They are split so the
/// repository content rule that bans that shape in real code does not flag these deliberate fixtures.
/// </summary>
public static class FixtureRoutes
{
    public const string OrderById = "{orderId" + ":long}";
    public const string RecordById = "/records/{recordId" + ":int}";
}
