using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Jobs.Domain;

namespace Starter.Modules.Jobs.Infrastructure.Persistence.Migrations;

/// <summary>The migration identifiers and constraint names of the jobs schema.</summary>
public static class JobsMigrationIds
{
    /// <summary>The id of the first migration.</summary>
    public const string InitialCreate = "20261106120000_InitialCreate";

    /// <summary>The primary key of <c>jobs.JobRuns</c>.</summary>
    public const string JobRunsPrimaryKey = "PK_JobRuns";
}

/// <summary>The SQL Server store types and annotations the jobs migrations write.</summary>
public static class JobsSqlTypes
{
    /// <summary>32-bit integer.</summary>
    public const string Int32Column = "int";

    /// <summary>64-bit integer.</summary>
    public const string BigInt = "bigint";

    /// <summary>A GUID.</summary>
    public const string UniqueIdentifier = "uniqueidentifier";

    /// <summary>Date, time and offset.</summary>
    public const string DateTimeOffset = "datetimeoffset";

    /// <summary>The SQL Server identity annotation.</summary>
    public const string IdentityAnnotation = "SqlServer:Identity";

    /// <summary>Identity seed 1, increment 1.</summary>
    public const string IdentitySeedIncrement = "1, 1";

    /// <summary>The model annotation that records the EF Core version.</summary>
    public const string ProductVersionAnnotation = "ProductVersion";

    /// <summary>The EF Core version of the model.</summary>
    public const string ProductVersion = "10.0.14";

    /// <summary>The longest SQL Server identifier.</summary>
    public const int MaxIdentifierLength = 128;

    /// <summary>A Unicode string column of <paramref name="length"/> characters.</summary>
    public static string NVarChar(int length) => $"nvarchar({length})";
}

/// <summary>
/// The jobs model as of the latest migration: the target model of <see cref="InitialCreate"/> and the model snapshot that
/// <c>dotnet ef migrations add</c> diffs against. A later migration freezes its own copy before this one changes.
/// </summary>
public static class JobsMigrationModel
{
    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(JobsDbContext.SchemaName)
            .HasAnnotation(JobsSqlTypes.ProductVersionAnnotation, JobsSqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, JobsSqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(JobRun).FullName!, BuildJobRun);
    }

    private static void BuildJobRun(EntityTypeBuilder entity)
    {
        entity.Property<long>(nameof(JobRun.Id)).ValueGeneratedOnAdd().HasColumnType(JobsSqlTypes.BigInt).UseIdentityColumn();
        StringColumn(entity, nameof(JobRun.Actor), JobsLimits.ActorMaxLength, required: true);
        StringColumn(entity, nameof(JobRun.CorrelationId), JobsLimits.CorrelationIdMaxLength, required: false);
        StringColumn(entity, nameof(JobRun.ErrorCode), JobsLimits.ErrorCodeMaxLength, required: false);
        entity.Property<DateTimeOffset?>(nameof(JobRun.FinishedAt)).HasColumnType(JobsSqlTypes.DateTimeOffset);
        StringColumn(entity, nameof(JobRun.JobKey), JobsLimits.JobKeyMaxLength, required: true);
        entity.Property<DateTimeOffset>(nameof(JobRun.QueuedAt)).HasColumnType(JobsSqlTypes.DateTimeOffset);
        StringColumn(entity, nameof(JobRun.ReferenceCode), JobsLimits.ReferenceCodeMaxLength, required: false);
        entity.Property<DateTimeOffset?>(nameof(JobRun.StartedAt)).HasColumnType(JobsSqlTypes.DateTimeOffset);
        entity.Property<int>(nameof(JobRun.Status)).HasColumnType(JobsSqlTypes.Int32Column);
        entity.Property<int>(nameof(JobRun.Trigger)).HasColumnType(JobsSqlTypes.Int32Column);
        entity.Property<Guid?>(nameof(JobRun.TriggeredBySubjectId)).HasColumnType(JobsSqlTypes.UniqueIdentifier);
        entity.HasKey(nameof(JobRun.Id));
        entity.HasIndex(nameof(JobRun.JobKey), nameof(JobRun.Id)).HasDatabaseName(JobsTableNames.JobKeyIdIndex);
        entity.ToTable(JobsTableNames.JobRuns, JobsDbContext.SchemaName);
    }

    private static void StringColumn(EntityTypeBuilder entity, string name, int maxLength, bool required) =>
        entity.Property<string>(name).IsRequired(required).HasMaxLength(maxLength).HasColumnType(JobsSqlTypes.NVarChar(maxLength));
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(JobsDbContext))]
public sealed class JobsDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => JobsMigrationModel.Build(modelBuilder);
}
