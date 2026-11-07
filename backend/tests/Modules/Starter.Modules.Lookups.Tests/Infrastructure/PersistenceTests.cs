using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Domain.Lookups;
using Starter.Modules.Lookups.Domain;
using Starter.Modules.Lookups.Infrastructure.Persistence;
using Starter.Modules.Lookups.Infrastructure.Persistence.Migrations;

namespace Starter.Modules.Lookups.Tests.Infrastructure;

public sealed class LookupsModelTests
{
    [Fact]
    public void The_model_lives_in_the_lookups_schema_and_matches_its_migrations()
    {
        using var context = NewContext();

        Assert.Equal(LookupsDbContext.SchemaName, context.Model.GetDefaultSchema());
        Assert.Equal(LookupsTableNames.LookupTypes, context.Model.FindEntityType(typeof(LookupType))!.GetTableName());
        Assert.Equal(LookupsTableNames.LookupItems, context.Model.FindEntityType(typeof(LookupItem))!.GetTableName());
        Assert.Equal([LookupsMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Values_are_keyed_by_type_and_fixed_id_and_their_type_cannot_be_deleted_under_them()
    {
        using var context = NewContext();
        var item = context.Model.FindEntityType(typeof(LookupItem))!;

        Assert.Equal([nameof(LookupItem.TypeKey), nameof(LookupItem.Id)], item.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(ValueGenerated.Never, item.FindProperty(nameof(LookupItem.Id))!.ValueGenerated);
        var foreignKey = Assert.Single(item.GetForeignKeys());
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.Equal(typeof(LookupType), foreignKey.PrincipalEntityType.ClrType);
        Assert.Contains(item.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual([nameof(LookupItem.TypeKey), nameof(LookupItem.Code)]));
    }

    [Fact]
    public void The_model_carries_no_seed_data()
    {
        using var context = NewContext();

        Assert.All(context.GetService<IDesignTimeModel>().Model.GetEntityTypes(), entity => Assert.Empty(entity.GetSeedData()));
    }

    [Fact]
    public void The_migration_script_creates_the_tables_and_seeds_the_contact_topics()
    {
        using var context = NewContext();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("[lookups].[LookupTypes]", script, StringComparison.Ordinal);
        Assert.Contains("[lookups].[LookupItems]", script, StringComparison.Ordinal);
        Assert.Contains($"CONSTRAINT [{LookupsMigrationIds.LookupItemsTypeForeignKey}]", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ON DELETE CASCADE", script, StringComparison.Ordinal);
        Assert.Contains($"CREATE UNIQUE INDEX [{LookupsMigrationIds.LookupItemsTypeCodeIndex}]", script, StringComparison.Ordinal);
        Assert.Contains("N'contact-topics'", script, StringComparison.Ordinal);
        Assert.Contains("N'استفسار عام'", script, StringComparison.Ordinal);
        Assert.Contains("[lookups].[__EFMigrationsHistory]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_squashed_InitialCreate_seeds_every_initial_lookup_type()
    {
        using var context = NewContext();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Equal(InitialLookupTypes.All.Count, script.Split("INSERT INTO [lookups].[LookupTypes]").Length - 1);
        Assert.All(InitialLookupTypes.All, seed => Assert.Contains($"N'{seed.TypeKey}'", script, StringComparison.Ordinal));
    }

    internal static LookupsDbContext NewContext() => new(new DbContextOptionsBuilder<LookupsDbContext>()
        .UseSqlServer("Server=.;Database=NeverOpened", sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, LookupsDbContext.SchemaName))
        .Options);
}

/// <summary>
/// Every lookup seed against the lookup rules: the types folded into InitialCreate (<see cref="InitialLookupTypes"/>)
/// and every later seed migration (a migration with a static <c>Items</c> list, written by new-lookup).
/// </summary>
public sealed class SeedMigrationTests
{
    private const string ItemsField = "Items";

    public static TheoryData<string> SeedMigrations()
    {
        var data = new TheoryData<string>();
        foreach (var name in AllSeeds().Keys.Order(StringComparer.Ordinal))
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SeedMigrations))]
    public void Values_have_unique_ids_and_well_formed_unique_codes(string migration)
    {
        var items = AllSeeds()[migration];

        Assert.NotEmpty(items);
        Assert.Equal(items.Count, items.Select(item => item.Id).Distinct().Count());
        Assert.Equal(items.Count, items.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.All(items, item => Assert.True(LookupLimits.IsKebab(item.Code, LookupLimits.CodeMaxLength), item.Code));
        Assert.All(items, item => Assert.True(item.Id >= 1, item.Code));
    }

    [Theory]
    [MemberData(nameof(SeedMigrations))]
    public void Every_value_has_an_arabic_and_an_english_name(string migration) =>
        Assert.All(AllSeeds()[migration], item =>
        {
            Assert.InRange(item.Names.Ar.Length, 1, LookupLimits.NameMaxLength);
            Assert.InRange(item.Names.En.Length, 1, LookupLimits.NameMaxLength);
            Assert.Contains(item.Names.Ar, IsArabicLetter);
            Assert.DoesNotContain(item.Names.En, IsArabicLetter);
        });

    [Fact]
    public void The_contact_topics_seed_matches_the_ContactTopicIds_constants()
    {
        var constants = typeof(ContactTopicIds).GetFields()
            .Where(field => field.IsLiteral)
            .ToDictionary(field => field.Name, field => (int)field.GetRawConstantValue()!, StringComparer.Ordinal);
        var items = InitialLookupTypes.ContactTopics.Items;

        Assert.Equal(constants.Values.Order(), items.Select(item => item.Id).Order());
        Assert.Equal(
            ["account", "general", "request", "suggestion"],
            items.Select(item => item.Code).Order(StringComparer.Ordinal));
        Assert.Equal(constants["Request"], items.Single(item => item.Code == "request").Id);
    }

    private static Dictionary<string, IReadOnlyList<LookupItemSeed>> AllSeeds()
    {
        var seeds = InitialLookupTypes.All.ToDictionary(seed => $"{nameof(InitialCreate)}:{seed.TypeKey}", seed => seed.Items, StringComparer.Ordinal);
        foreach (var type in typeof(LookupsDbContext).Assembly.GetTypes().Where(IsSeedMigration))
        {
            seeds[type.Name] = ItemsOf(type);
        }

        return seeds;
    }

    private static bool IsSeedMigration(Type type) =>
        typeof(Migration).IsAssignableFrom(type) && type.GetField(ItemsField, BindingFlags.NonPublic | BindingFlags.Static) is not null;

    private static IReadOnlyList<LookupItemSeed> ItemsOf(Type migration) =>
        (IReadOnlyList<LookupItemSeed>)migration.GetField(ItemsField, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static bool IsArabicLetter(char character) => character is >= '؀' and <= 'ۿ' && char.IsLetter(character);
}
