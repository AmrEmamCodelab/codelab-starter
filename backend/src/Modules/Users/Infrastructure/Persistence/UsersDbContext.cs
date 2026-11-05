using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Domain;

namespace Starter.Modules.Users.Infrastructure.Persistence;

/// <summary>The Users module's tables, in the <c>users</c> schema: organizations and import batches.</summary>
public sealed class UsersDbContext(DbContextOptions<UsersDbContext> options) : ModuleDbContext(options), IUsersUnitOfWork
{
    /// <summary>The database schema of the module.</summary>
    public const string SchemaName = "users";

    /// <summary>SQL Server's error number for a duplicate key in a unique index.</summary>
    private const int UniqueIndexViolation = 2601;

    /// <summary>SQL Server's error number for a duplicate key in a unique constraint.</summary>
    private const int UniqueConstraintViolation = 2627;

    /// <summary>The organizations.</summary>
    public DbSet<Organization> Organizations => Set<Organization>();

    /// <summary>The import batches.</summary>
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;

    /// <inheritdoc />
    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            ChangeTracker.Clear();
            return false;
        }
    }

    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation };
}

/// <summary>The <c>users.Organizations</c> table.</summary>
public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(UsersTableNames.Organizations);
        builder.HasKey(organization => organization.Id);
        builder.Property(organization => organization.Id).UseIdentityColumn();
        builder.Property(organization => organization.Code).HasMaxLength(UsersLimits.OrganizationCodeMaxLength).IsRequired();
        builder.HasIndex(organization => organization.Code).IsUnique();
        builder.Property(organization => organization.NameEn).HasMaxLength(UsersLimits.OrganizationNameMaxLength).IsRequired();
        builder.Property(organization => organization.NameAr).HasMaxLength(UsersLimits.OrganizationNameMaxLength).IsRequired();
    }
}

/// <summary>The <c>users.ImportBatches</c> table.</summary>
public sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(UsersTableNames.ImportBatches);
        builder.HasKey(batch => batch.Id);
        builder.Property(batch => batch.Id).UseIdentityColumn();
        builder.Property(batch => batch.Status).IsConcurrencyToken();
        builder.Property(batch => batch.RowsJson).IsRequired();
        builder.HasIndex(batch => batch.ExpiresAt);
    }
}

/// <summary>The Users table names.</summary>
public static class UsersTableNames
{
    /// <summary>The organizations table.</summary>
    public const string Organizations = "Organizations";

    /// <summary>The import batches table.</summary>
    public const string ImportBatches = "ImportBatches";
}
