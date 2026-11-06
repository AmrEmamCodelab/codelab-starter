using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Starter.Infrastructure.Numbering.Migrations;

/// <summary>
/// The <c>numbering</c> schema, empty: the sequences are created on demand by <see cref="SqlNumberSequences"/>. Down
/// leaves the schema (it may hold those sequences, which no migration owns).
/// </summary>
[DbContext(typeof(NumberingDbContext))]
[Migration(NumberingMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: NumberingDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => ArgumentNullException.ThrowIfNull(migrationBuilder);

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => NumberingMigrationModel.Build(modelBuilder);
}

/// <summary>The migration identifiers of the numbering schema.</summary>
public static class NumberingMigrationIds
{
    /// <summary>The id of the first migration.</summary>
    public const string InitialCreate = "20261106090100_InitialCreate";
}

/// <summary>The numbering model as of the latest migration (empty: default schema and annotations only).</summary>
public static class NumberingMigrationModel
{
    /// <summary>The model annotation that records the EF Core version.</summary>
    public const string ProductVersionAnnotation = "ProductVersion";

    /// <summary>The EF Core version that scaffolded the model.</summary>
    public const string ProductVersion = "10.0.14";

    /// <summary>The longest SQL Server identifier.</summary>
    public const int MaxIdentifierLength = 128;

    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(NumberingDbContext.SchemaName)
            .HasAnnotation(ProductVersionAnnotation, ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
    }
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(NumberingDbContext))]
public sealed class NumberingDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => NumberingMigrationModel.Build(modelBuilder);
}
