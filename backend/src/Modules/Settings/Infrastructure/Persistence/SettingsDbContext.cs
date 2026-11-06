using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Settings.Application;
using Starter.Modules.Settings.Domain;

namespace Starter.Modules.Settings.Infrastructure.Persistence;

/// <summary>The Settings module's tables, in the <c>settings</c> schema: the settings and their version stamp.</summary>
public sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options) : ModuleDbContext(options), ISettingsUnitOfWork
{
    /// <summary>The database schema of the module.</summary>
    public const string SchemaName = "settings";

    /// <summary>The settings.</summary>
    public DbSet<Setting> Settings => Set<Setting>();

    /// <summary>The single-row version stamp.</summary>
    public DbSet<SettingsStamp> Stamps => Set<SettingsStamp>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;

    /// <inheritdoc />
    public async Task SaveChangesAndAdvanceStampAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        await SaveChangesAsync(cancellationToken);
        var advanced = await Stamps
            .Where(stamp => stamp.Id == SettingsStamp.SingletonId)
            .ExecuteUpdateAsync(update => update.SetProperty(stamp => stamp.Version, stamp => stamp.Version + 1), cancellationToken);
        if (advanced != 1)
        {
            throw new InvalidOperationException("settings.SettingsStamp has no row; the Settings InitialCreate migration seeds it.");
        }

        await transaction.CommitAsync(cancellationToken);
    }
}

/// <summary>The <c>settings.Settings</c> table.</summary>
public sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(SettingsTableNames.Settings);
        builder.HasKey(setting => setting.Key);
        builder.Property(setting => setting.Key).HasMaxLength(SettingFieldLimits.KeyMaxLength).ValueGeneratedNever();
        builder.Property(setting => setting.Group).HasMaxLength(SettingFieldLimits.GroupMaxLength).IsRequired();
        builder.Property(setting => setting.Value).HasMaxLength(SettingFieldLimits.ValueMaxLength).IsRequired();
        builder.Property(setting => setting.DefaultValue).HasMaxLength(SettingFieldLimits.ValueMaxLength).IsRequired();
        builder.Property(setting => setting.DescriptionAr).HasMaxLength(SettingFieldLimits.DescriptionMaxLength).IsRequired();
        builder.Property(setting => setting.DescriptionEn).HasMaxLength(SettingFieldLimits.DescriptionMaxLength).IsRequired();
        builder.Ignore(setting => setting.Range);
    }
}

/// <summary>The <c>settings.SettingsStamp</c> table (one row).</summary>
public sealed class SettingsStampConfiguration : IEntityTypeConfiguration<SettingsStamp>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SettingsStamp> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(SettingsTableNames.SettingsStamp);
        builder.HasKey(stamp => stamp.Id);
        builder.Property(stamp => stamp.Id).ValueGeneratedNever();
    }
}

/// <summary>The Settings table names.</summary>
public static class SettingsTableNames
{
    /// <summary>The settings table.</summary>
    public const string Settings = "Settings";

    /// <summary>The single-row version stamp table.</summary>
    public const string SettingsStamp = "SettingsStamp";
}
