namespace Starter.Modules.Users.Application.Import;

/// <summary>A column of the user import sheet.</summary>
public enum ImportColumn
{
    /// <summary>The sign-in name (required).</summary>
    UserName = 1,

    /// <summary>The display name (required).</summary>
    DisplayName = 2,

    /// <summary>The e-mail address.</summary>
    Email = 3,

    /// <summary>The directory login name.</summary>
    LoginName = 4,

    /// <summary>The user principal name.</summary>
    Upn = 5,

    /// <summary><c>internal</c> or <c>external</c> (required).</summary>
    Kind = 6,

    /// <summary><c>password</c>, <c>ad</c> or <c>sso</c> (required).</summary>
    Binding = 7,

    /// <summary>The organization code.</summary>
    OrganizationCode = 8,
}

/// <summary>One column of the template: its header text and whether a sheet must have it.</summary>
/// <param name="Column">The column.</param>
/// <param name="Header">The header text of the template (also the field name of a missing-column error).</param>
/// <param name="IsRequired">True when a sheet without this column is refused.</param>
public sealed record ImportColumnDefinition(ImportColumn Column, string Header, bool IsRequired);

/// <summary>Where each known column is in a sheet, and which required ones are missing.</summary>
/// <param name="Positions">The 0-based position of every known column found.</param>
/// <param name="Missing">The required columns that are absent, in template order.</param>
public sealed record ImportColumnMap(IReadOnlyDictionary<ImportColumn, int> Positions, IReadOnlyList<ImportColumnDefinition> Missing)
{
    /// <summary>The cell of <paramref name="column"/> in <paramref name="row"/>, or null when the column is absent or the cell blank.</summary>
    public string? CellOf(SheetRow row, ImportColumn column)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Positions.TryGetValue(column, out var index) ? row.CellAt(index) : null;
    }
}

/// <summary>
/// The import template's columns and the header matching. Headers are matched case-insensitively and ignoring spaces,
/// underscores and hyphens, so <c>UserName</c>, <c>user name</c>, <c>USER_NAME</c> and <c>user-name</c> are the same
/// column; column order does not matter, unknown columns are ignored and the first of two equal headers wins.
/// </summary>
public static class ImportColumns
{
    /// <summary>The template, in its column order.</summary>
    public static IReadOnlyList<ImportColumnDefinition> Template { get; } =
    [
        new(ImportColumn.UserName, "UserName", IsRequired: true),
        new(ImportColumn.DisplayName, "DisplayName", IsRequired: true),
        new(ImportColumn.Email, "Email", IsRequired: false),
        new(ImportColumn.LoginName, "LoginName", IsRequired: false),
        new(ImportColumn.Upn, "Upn", IsRequired: false),
        new(ImportColumn.Kind, "Kind", IsRequired: true),
        new(ImportColumn.Binding, "Binding", IsRequired: true),
        new(ImportColumn.OrganizationCode, "OrganizationCode", IsRequired: false),
    ];

    /// <summary>The matching key of a header or value: no whitespace, <c>_</c> or <c>-</c>, upper-case (invariant).</summary>
    public static string Normalize(string? text) =>
        text is null
            ? string.Empty
            : new string([.. text.Where(character => !char.IsWhiteSpace(character) && character is not ('_' or '-'))]).ToUpperInvariant();

    /// <summary>The column map of a header row.</summary>
    public static ImportColumnMap Map(SheetRow header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var byKey = Template.ToDictionary(definition => Normalize(definition.Header), definition => definition.Column, StringComparer.Ordinal);
        var positions = new Dictionary<ImportColumn, int>();
        for (var index = 0; index < header.Cells.Count; index++)
        {
            if (byKey.TryGetValue(Normalize(header.Cells[index]), out var column))
            {
                positions.TryAdd(column, index);
            }
        }

        var missing = Template.Where(definition => definition.IsRequired && !positions.ContainsKey(definition.Column)).ToList();
        return new ImportColumnMap(positions, missing);
    }

    /// <summary>The raw import row of a data row.</summary>
    public static ImportRow RowOf(ImportColumnMap map, SheetRow row)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(row);
        return new ImportRow(
            RowNumber: row.RowNumber,
            UserName: map.CellOf(row, ImportColumn.UserName),
            DisplayName: map.CellOf(row, ImportColumn.DisplayName),
            Email: map.CellOf(row, ImportColumn.Email),
            LoginName: map.CellOf(row, ImportColumn.LoginName),
            Upn: map.CellOf(row, ImportColumn.Upn),
            Kind: map.CellOf(row, ImportColumn.Kind),
            Binding: map.CellOf(row, ImportColumn.Binding),
            OrganizationCode: map.CellOf(row, ImportColumn.OrganizationCode));
    }
}

/// <summary>One data row of the sheet, as text (trimmed, blank as null).</summary>
/// <param name="RowNumber">The row number shown in Excel.</param>
/// <param name="UserName">The sign-in name.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Email">The e-mail address.</param>
/// <param name="LoginName">The directory login name.</param>
/// <param name="Upn">The user principal name.</param>
/// <param name="Kind">The kind text (<c>internal</c> or <c>external</c>).</param>
/// <param name="Binding">The binding text (<c>password</c>, <c>ad</c> or <c>sso</c>).</param>
/// <param name="OrganizationCode">The organization code.</param>
public sealed record ImportRow(
    int RowNumber,
    string? UserName,
    string? DisplayName,
    string? Email,
    string? LoginName,
    string? Upn,
    string? Kind,
    string? Binding,
    string? OrganizationCode);
