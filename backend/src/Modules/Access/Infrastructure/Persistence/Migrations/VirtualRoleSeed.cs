using System.Globalization;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Infrastructure.Persistence.Migrations;

/// <summary>One virtual role the migrations insert.</summary>
/// <param name="Kind">The users it reaches.</param>
/// <param name="Code">The stable code.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="NameAr">The Arabic name.</param>
public sealed record VirtualRoleSeedRow(VirtualRoleKind Kind, string Code, string NameEn, string NameAr);

/// <summary>
/// The virtual roles seeded by <see cref="InitialCreate"/>: one per <see cref="VirtualRoleKind"/>, active, with no
/// permission (administrators add permissions in the role editor). Frozen with the migration: a new kind gets a NEW
/// migration that inserts its row, never an edit here.
/// </summary>
public static class VirtualRoleSeed
{
    /// <summary>The creation time written on the seeded rows (a fixed, deterministic value).</summary>
    public const string SeededAtText = "2026-11-05T00:00:00+00:00";

    /// <summary>The code of the internal-users role.</summary>
    public const string AllInternalUsersCode = "all-internal-users";

    /// <summary>The code of the external-users role.</summary>
    public const string AllExternalUsersCode = "all-external-users";

    /// <summary>The code of the every-signed-in-user role.</summary>
    public const string AllAuthenticatedUsersCode = "all-authenticated-users";

    /// <summary>The seeded creation time.</summary>
    public static DateTimeOffset SeededAt { get; } = DateTimeOffset.Parse(SeededAtText, CultureInfo.InvariantCulture);

    /// <summary>The rows, in kind order.</summary>
    public static IReadOnlyList<VirtualRoleSeedRow> Rows { get; } =
    [
        new(VirtualRoleKind.AllInternalUsers, AllInternalUsersCode, NameEn: "All internal users", NameAr: "جميع المستخدمين الداخليين"),
        new(VirtualRoleKind.AllExternalUsers, AllExternalUsersCode, NameEn: "All external users", NameAr: "جميع المستخدمين الخارجيين"),
        new(VirtualRoleKind.AllAuthenticatedUsers, AllAuthenticatedUsersCode, NameEn: "All signed-in users", NameAr: "جميع المستخدمين المسجلين"),
    ];
}
