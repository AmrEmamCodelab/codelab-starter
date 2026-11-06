using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Lookups;
using Starter.Domain.Localization;
using Starter.Domain.Lookups;
using Starter.Modules.Portal.Application;
using Starter.Modules.Portal.Application.Content;
using Starter.Modules.Portal.Domain;
using Starter.Modules.Portal.Infrastructure.Caching;
using Starter.Modules.Portal.Infrastructure.Lookups;
using Starter.Modules.Portal.Infrastructure.Options;
using Starter.Modules.Portal.Infrastructure.Persistence;
using Starter.Modules.Portal.Infrastructure.Persistence.Migrations;
using Starter.Modules.Portal.Tests.Fakes;

namespace Starter.Modules.Portal.Tests.Infrastructure;

public sealed class MemoryPortalContentCacheTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_entry_is_served_until_the_ttl_then_dropped()
    {
        var clock = new FakeTimeProvider(Start);
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryPortalContentCache(memory, clock);
        var content = new PortalContentView(PortalPage.Faq, UpdatedAt: null, []);

        cache.Store(content);
        clock.Advance(PortalLimits.ContentCacheTtl - TimeSpan.FromSeconds(1));
        Assert.Same(content, cache.Find(PortalPage.Faq));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(cache.Find(PortalPage.Faq));
    }

    [Fact]
    public void Invalidate_drops_only_that_page()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryPortalContentCache(memory, new FakeTimeProvider(Start));
        cache.Store(new PortalContentView(PortalPage.Faq, UpdatedAt: null, []));
        cache.Store(new PortalContentView(PortalPage.Terms, UpdatedAt: null, []));

        cache.Invalidate(PortalPage.Faq);

        Assert.Null(cache.Find(PortalPage.Faq));
        Assert.NotNull(cache.Find(PortalPage.Terms));
    }

    [Fact]
    public void Keys_are_per_page()
    {
        Assert.Equal("portal:content:1", MemoryPortalContentCache.KeyOf(PortalPage.Faq));
        Assert.NotEqual(MemoryPortalContentCache.KeyOf(PortalPage.Privacy), MemoryPortalContentCache.KeyOf(PortalPage.Terms));
    }
}

public sealed class PortalContactOptionsValidatorTests
{
    private static readonly PortalContactOptionsValidator Validator = new();

    [Fact]
    public void A_well_formed_inbox_passes()
    {
        Assert.True(Validator.Validate(name: null, new PortalContactOptions { InboxEmail = "contact@example.invalid" }).Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("a@b.c; d@e.f")]
    public void A_missing_or_malformed_inbox_fails_the_start(string inbox)
    {
        var result = Validator.Validate(name: null, new PortalContactOptions { InboxEmail = inbox });

        Assert.True(result.Failed);
        Assert.Contains("Portal:Contact:InboxEmail", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void An_undefined_language_fails_the_start()
    {
        var result = Validator.Validate(name: null, new PortalContactOptions { InboxEmail = "contact@example.invalid", InboxLanguage = (AppLanguage)7 });

        Assert.True(result.Failed);
    }
}

public sealed class PortalModelTests
{
    [Fact]
    public void The_model_lives_in_the_portal_schema_and_matches_the_InitialCreate_migration()
    {
        using var context = NewContext();

        Assert.Equal(PortalDbContext.SchemaName, context.Model.GetDefaultSchema());
        Assert.Equal(PortalTableNames.ContactMessages, context.Model.FindEntityType(typeof(ContactMessage))!.GetTableName());
        Assert.Equal(PortalTableNames.ContentSections, context.Model.FindEntityType(typeof(ContentSection))!.GetTableName());
        Assert.Equal([PortalMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Enums_are_int_columns()
    {
        using var context = NewContext();

        Assert.Equal("int", context.Model.FindEntityType(typeof(ContactMessage))!.FindProperty(nameof(ContactMessage.Language))!.GetColumnType());
        Assert.Equal("int", context.Model.FindEntityType(typeof(ContentSection))!.FindProperty(nameof(ContentSection.Page))!.GetColumnType());
        Assert.Equal("int", context.Model.FindEntityType(typeof(ContentPage))!.FindProperty(nameof(ContentPage.Page))!.GetColumnType());
    }

    [Fact]
    public void The_migration_script_creates_the_tables_the_unique_number_and_seeds_every_page_and_section()
    {
        using var context = NewContext();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("CREATE TABLE [portal].[ContactMessages]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [portal].[ContentPages]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [portal].[ContentSections]", script, StringComparison.Ordinal);
        Assert.Contains($"CREATE UNIQUE INDEX [{PortalMigrationIds.ReferenceNumberIndex}]", script, StringComparison.Ordinal);
        Assert.Contains("[portal].[__EFMigrationsHistory]", script, StringComparison.Ordinal);
        Assert.Equal(PortalPages.All.Count, Occurrences(script, "INSERT INTO [portal].[ContentPages]"));
        Assert.Equal(ContentSeed.Sections.Count, Occurrences(script, "INSERT INTO [portal].[ContentSections]"));
        Assert.Contains("N'ماذا يمكنني أن أفعل في هذه البوابة؟'", script, StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string value) => text.Split(value).Length - 1;

    private static PortalDbContext NewContext() => new(new DbContextOptionsBuilder<PortalDbContext>()
        .UseSqlServer("Server=.;Database=NeverOpened", sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, PortalDbContext.SchemaName))
        .Options);
}

public sealed class ContentSeedTests
{
    [Fact]
    public void Every_page_is_seeded_with_ordered_sections()
    {
        foreach (var page in PortalPages.All)
        {
            var orders = ContentSeed.Sections.Where(section => section.Page == page).Select(section => section.SortOrder).ToList();

            Assert.NotEmpty(orders);
            Assert.Equal(orders.Order(), orders);
            Assert.Equal(orders.Count, orders.Distinct().Count());
        }
    }

    [Fact]
    public void Every_seeded_text_is_bilingual_trimmed_and_within_the_limits()
    {
        Assert.All(ContentSeed.Sections, section =>
        {
            foreach (var title in new[] { section.Text.TitleAr, section.Text.TitleEn })
            {
                Assert.False(string.IsNullOrWhiteSpace(title));
                Assert.Equal(title.Trim(), title);
                Assert.True(title.Length <= PortalLimits.SectionTitleMaxLength);
            }

            foreach (var body in new[] { section.Text.BodyAr, section.Text.BodyEn })
            {
                Assert.False(string.IsNullOrWhiteSpace(body));
                Assert.True(body.Length <= PortalLimits.SectionBodyMaxLength);
            }

            Assert.Equal(Paragraphs(section.Text.BodyAr), Paragraphs(section.Text.BodyEn));
            Assert.InRange(section.SortOrder, PortalLimits.SortOrderMin, PortalLimits.SortOrderMax);
        });
    }

    [Fact]
    public void The_faq_has_one_section_per_question()
    {
        Assert.All(ContentSeed.Sections.Where(section => section.Page == PortalPage.Faq), section =>
        {
            Assert.EndsWith("?", section.Text.TitleEn, StringComparison.Ordinal);
            Assert.EndsWith("؟", section.Text.TitleAr, StringComparison.Ordinal);
        });
    }

    private static int Paragraphs(string body) => body.Split(ContentSeed.ParagraphBreak).Length;
}

public sealed class LookupContactTopicsTests
{
    [Fact]
    public async Task Topics_are_the_active_values_of_the_contact_topics_lookup_read_through_the_kernel_port()
    {
        var lookups = new FakeLookupValues();
        lookups.Values[(LookupTypes.ContactTopics, ContactTopicIds.General)] = new ActiveLookupValue(ContactTopicIds.General, "general", "عام", "General");
        var topics = new LookupContactTopics(lookups);

        var found = await topics.FindActiveAsync(ContactTopicIds.General, CancellationToken.None);
        var missing = await topics.FindActiveAsync(ContactTopicIds.Account, CancellationToken.None);

        Assert.Equal(new ContactTopic(ContactTopicIds.General, "عام", "General"), found);
        Assert.Null(missing);
        Assert.All(lookups.Asked, asked => Assert.Equal(LookupTypes.ContactTopics, asked.TypeKey));
    }
}

public sealed class SqlContactTopicsTests
{
    [Fact]
    public void The_query_reads_only_active_values_of_the_contact_topics_type_with_parameters()
    {
        Assert.Contains("[lookups].[LookupItems]", SqlContactTopics.Query, StringComparison.Ordinal);
        Assert.Contains("[TypeKey] = @typeKey", SqlContactTopics.Query, StringComparison.Ordinal);
        Assert.Contains("[Id] = @id", SqlContactTopics.Query, StringComparison.Ordinal);
        Assert.Contains("[IsActive] = CAST(1 AS bit)", SqlContactTopics.Query, StringComparison.Ordinal);
        Assert.Equal("contact-topics", LookupTypes.ContactTopics);
    }
}
