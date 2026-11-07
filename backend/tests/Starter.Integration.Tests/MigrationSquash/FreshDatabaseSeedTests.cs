using Microsoft.EntityFrameworkCore;
using Starter.Modules.Attachments.Domain; // codelab:module Attachments
using Starter.Modules.Attachments.Infrastructure.Persistence.Migrations; // codelab:module Attachments
using Starter.Modules.Lookups.Domain;
using Starter.Modules.Lookups.Infrastructure.Persistence.Migrations;
using Starter.Modules.Notifications.Domain;
using Starter.Modules.Notifications.Infrastructure.Persistence.Migrations;

namespace Starter.Integration.Tests.MigrationSquash;

/// <summary>
/// ADR-010 on real SQL Server: a fresh database built from the squashed migrations alone applies exactly one
/// migration per seeded context, keeping the InitialCreate id, and holds every seed the folded migrations used to
/// insert: each lookup type with all its values, every notification template and the attachment types.
/// </summary>
[Collection(FreshDatabaseGroup.Name)]
public sealed class FreshDatabaseSeedTests(FreshDatabase database)
{
    [Fact]
    public async Task Each_seeded_context_applied_only_its_InitialCreate()
    {
        await using var lookups = database.Lookups();
        await using var notifications = database.Notifications();
        await using var attachments = database.Attachments(); // codelab:module Attachments

        Assert.Equal([LookupsMigrationIds.InitialCreate], await lookups.Database.GetAppliedMigrationsAsync());
        Assert.Equal([NotificationsMigrationIds.InitialCreate], await notifications.Database.GetAppliedMigrationsAsync());
        Assert.Equal([AttachmentsMigrationIds.InitialCreate], await attachments.Database.GetAppliedMigrationsAsync()); // codelab:module Attachments
    }

    [Fact]
    public async Task Every_lookup_type_is_seeded_with_all_its_values()
    {
        await using var context = database.Lookups();
        var types = await context.Set<LookupType>().AsNoTracking().ToListAsync();
        var items = await context.Set<LookupItem>().AsNoTracking().ToListAsync();

        Assert.Equal(
            InitialLookupTypes.All.Select(seed => (seed.TypeKey, seed.Names.Ar, seed.Names.En)).Order(),
            types.Select(type => (type.Key, type.NameAr, type.NameEn)).Order());
        Assert.Equal(
            InitialLookupTypes.All.SelectMany(seed => seed.Items.Select(item => (seed.TypeKey, item.Id, item.Code, item.Names.Ar, item.Names.En, item.SortOrder, item.IsActive))).Order(),
            items.Select(item => (item.TypeKey, item.Id, item.Code, item.NameAr, item.NameEn, item.SortOrder, item.IsActive)).Order());
    }

    [Fact]
    public async Task Every_notification_template_is_seeded()
    {
        await using var context = database.Notifications();
        var stored = await context.Set<NotificationTemplate>().AsNoTracking().ToListAsync();

        Assert.Equal(
            TemplateSeed.Rows.Concat(ServiceRequestTemplateSeed.Rows).Select(NotificationTemplate.Define).Select(row => (row.Key, row.Channel, row.Language, row.Subject, row.Body)).Order(),
            stored.Select(row => (row.Key, row.Channel, row.Language, row.Subject, row.Body)).Order());
    }

    // codelab:module-begin Attachments
    [Fact]
    public async Task The_attachment_types_are_seeded()
    {
        await using var context = database.Attachments();
        var stored = await context.Set<ServiceAttachment>().AsNoTracking().ToListAsync();

        Assert.Equal(
            AttachmentTypeSeed.Rows.Select(row => (row.Id, row.Key, row.RecordType, row.IsRequired, row.MaxFiles, string.Join(',', row.AllowedExtensions))).Order(),
            stored.Select(row => (row.Id, row.Key, row.RecordType, row.IsRequired, row.MaxFiles, string.Join(',', row.AllowedExtensions))).Order());
    }
    // codelab:module-end Attachments
}
