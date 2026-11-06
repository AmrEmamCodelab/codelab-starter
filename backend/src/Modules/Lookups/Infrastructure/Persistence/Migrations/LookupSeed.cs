using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Modules.Lookups.Domain;

namespace Starter.Modules.Lookups.Infrastructure.Persistence.Migrations;

/// <summary>
/// The only way lookup rows are written: explicit InsertData and DeleteData operations inside a migration (the model
/// carries no seed data). Every seed migration that <c>tools/scaffold/new-lookup.mjs</c> writes calls these, so the
/// column list has one owner.
/// </summary>
public static class LookupSeed
{
    private static readonly string[] TypeColumns = [nameof(LookupType.Key), nameof(LookupType.NameAr), nameof(LookupType.NameEn)];

    private static readonly string[] ItemColumns =
    [
        nameof(LookupItem.TypeKey),
        nameof(LookupItem.Id),
        nameof(LookupItem.Code),
        nameof(LookupItem.NameAr),
        nameof(LookupItem.NameEn),
        nameof(LookupItem.SortOrder),
        nameof(LookupItem.IsActive),
    ];

    private static readonly string[] ItemKeyColumns = [nameof(LookupItem.TypeKey), nameof(LookupItem.Id)];

    /// <summary>Inserts a new lookup type and its values (a whole new lookup).</summary>
    public static void InsertType(MigrationBuilder migrationBuilder, string typeKey, LookupNames names, IReadOnlyList<LookupItemSeed> items)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);
        ArgumentNullException.ThrowIfNull(names);
        migrationBuilder.InsertData(
            table: LookupsTableNames.LookupTypes,
            columns: TypeColumns,
            values: new object[,] { { typeKey, names.Ar, names.En } },
            schema: LookupsDbContext.SchemaName);
        InsertItems(migrationBuilder, typeKey, items);
    }

    /// <summary>Inserts values into an existing lookup type (a later migration that adds values).</summary>
    public static void InsertItems(MigrationBuilder migrationBuilder, string typeKey, IReadOnlyList<LookupItemSeed> items)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        var values = new object[items.Count, ItemColumns.Length];
        for (var row = 0; row < items.Count; row++)
        {
            object[] cells = [typeKey, items[row].Id, items[row].Code, items[row].Names.Ar, items[row].Names.En, items[row].SortOrder, items[row].IsActive];
            for (var column = 0; column < cells.Length; column++)
            {
                values[row, column] = cells[column];
            }
        }

        migrationBuilder.InsertData(table: LookupsTableNames.LookupItems, columns: ItemColumns, values: values, schema: LookupsDbContext.SchemaName);
    }

    /// <summary>Deletes values of a lookup type (the Down of <see cref="InsertItems"/>).</summary>
    public static void DeleteItems(MigrationBuilder migrationBuilder, string typeKey, IReadOnlyList<LookupItemSeed> items)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        var keys = new object[items.Count, ItemKeyColumns.Length];
        for (var row = 0; row < items.Count; row++)
        {
            keys[row, 0] = typeKey;
            keys[row, 1] = items[row].Id;
        }

        migrationBuilder.DeleteData(table: LookupsTableNames.LookupItems, keyColumns: ItemKeyColumns, keyValues: keys, schema: LookupsDbContext.SchemaName);
    }

    /// <summary>Deletes a lookup type and its values (the Down of <see cref="InsertType"/>).</summary>
    public static void DeleteType(MigrationBuilder migrationBuilder, string typeKey, IReadOnlyList<LookupItemSeed> items)
    {
        DeleteItems(migrationBuilder, typeKey, items);
        migrationBuilder.DeleteData(
            table: LookupsTableNames.LookupTypes,
            keyColumn: nameof(LookupType.Key),
            keyValue: typeKey,
            schema: LookupsDbContext.SchemaName);
    }
}
