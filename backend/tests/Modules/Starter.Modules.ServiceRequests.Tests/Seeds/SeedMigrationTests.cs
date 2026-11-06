using System.Reflection;
using System.Text.RegularExpressions;
using Starter.Application.Notifications;
using Starter.Domain.Attachments;
using Starter.Domain.Localization;
using Starter.Domain.Lookups;
using Starter.Modules.Attachments.Infrastructure.Persistence.Migrations;
using Starter.Modules.Lookups.Domain;
using Starter.Modules.Lookups.Infrastructure.Persistence.Migrations;
using Starter.Modules.Notifications.Infrastructure.Persistence.Migrations;
using Starter.Modules.ServiceRequests.Application;
using Starter.Modules.ServiceRequests.Application.Workflow;

namespace Starter.Modules.ServiceRequests.Tests.Seeds;

/// <summary>
/// The rows the sample seeds through other modules' migrations (Lookups, Attachments, Notifications) match the
/// sample's constants: those modules never reference the sample, so they freeze the same values as data.
/// </summary>
public sealed partial class SeedMigrationTests
{
    private const string ItemsField = "Items";

    [Fact]
    public void The_category_lookup_seeds_exactly_the_category_ids()
    {
        var constants = typeof(ServiceRequestCategoryIds).GetFields().Where(field => field.IsLiteral).Select(field => (int)field.GetRawConstantValue()!).Order();
        var items = (LookupItemSeed[])typeof(AddServiceRequestCategoriesLookup).GetField(ItemsField, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        Assert.Equal(constants, items.Select(item => item.Id).Order());
        Assert.True(items.Length >= 3);
        Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.Names.Ar) || string.IsNullOrWhiteSpace(item.Names.En), item.Code));
        Assert.Equal("service-request-categories", LookupTypes.ServiceRequestCategories);
    }

    [Fact]
    public void The_attachment_types_are_the_required_document_and_the_optional_supporting_files()
    {
        var rows = AddServiceRequestAttachmentTypes.Rows;

        Assert.Equal([AttachmentTypeIds.ServiceRequestDocument, AttachmentTypeIds.ServiceRequestSupporting], rows.Select(row => row.Id));
        Assert.Equal([AttachmentTypeKeys.ServiceRequestDocument, AttachmentTypeKeys.ServiceRequestSupporting], rows.Select(row => row.Key));
        Assert.All(rows, row => Assert.Equal(AttachableRecordTypes.ServiceRequest, row.RecordType));
        Assert.Equal([(true, 3), (false, 5)], rows.Select(row => (row.IsRequired, row.MaxFiles)));
        Assert.All(rows, row => Assert.Equal([".pdf", ".jpg", ".png"], row.AllowedExtensions));
        Assert.All(rows, row => Assert.True(row.IsActive));
    }

    [Fact]
    public void The_attachment_type_ids_do_not_collide_with_the_demo_type()
    {
        Assert.DoesNotContain(AttachmentTypeIds.DemoDocument, AddServiceRequestAttachmentTypes.Rows.Select(row => row.Id));
    }

    [Fact]
    public void The_seeded_template_keys_are_the_sample_s_keys()
    {
        var frozen = typeof(ServiceRequestTemplateKeys).GetFields().Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!);

        Assert.Equal(ServiceRequestNotificationKeys.All.Order(StringComparer.Ordinal), frozen.Order(StringComparer.Ordinal));
        Assert.Equal(ServiceRequestNotificationKeys.All.Order(StringComparer.Ordinal), ServiceRequestTemplateSeed.Rows.Select(row => row.Key).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(ServiceRequestNotificationKeys.RecordNumber, ServiceRequestTemplateSeed.RecordNumber);
        Assert.Equal(ServiceRequestNotificationKeys.DueAt, ServiceRequestTemplateSeed.DueAt);
    }

    [Fact]
    public void Every_template_has_arabic_and_english_in_app_and_by_email()
    {
        foreach (var key in ServiceRequestNotificationKeys.All)
        {
            var texts = ServiceRequestTemplateSeed.Rows.Where(row => row.Key == key).Select(row => (row.Channel, row.Language)).ToHashSet();

            Assert.Equal(
                new HashSet<(NotificationChannels, AppLanguage)>
                {
                    (NotificationChannels.InApp, AppLanguage.Arabic), (NotificationChannels.InApp, AppLanguage.English),
                    (NotificationChannels.Email, AppLanguage.Arabic), (NotificationChannels.Email, AppLanguage.English),
                },
                texts);
        }
    }

    [Fact]
    public void Every_text_uses_exactly_the_documented_placeholders()
    {
        foreach (var row in ServiceRequestTemplateSeed.Rows)
        {
            var expected = row.Key == ServiceRequestNotificationKeys.SlaEscalation
                ? new[] { ServiceRequestNotificationKeys.DueAt, ServiceRequestNotificationKeys.RecordNumber }
                : [ServiceRequestNotificationKeys.RecordNumber];
            var used = Placeholder().Matches(row.Body).Concat(Placeholder().Matches(row.Subject ?? string.Empty))
                .Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal);

            Assert.Equal(expected.Order(StringComparer.Ordinal), used);
            Assert.DoesNotContain('\n', row.Subject ?? string.Empty);
        }
    }

    [Fact]
    public void Every_template_a_transition_names_is_seeded()
    {
        var seeded = ServiceRequestTemplateSeed.Rows.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        var named = ServiceRequestWorkflow.Rules.Select(rule => rule.Notification?.TemplateKey).OfType<string>()
            .Append(ServiceRequestSlaPolicySource.UnderReview.EscalationTemplateKey);

        Assert.All(named, key => Assert.Contains(key, seeded));
    }

    [Fact]
    public void Arabic_email_bodies_read_right_to_left()
    {
        Assert.All(
            ServiceRequestTemplateSeed.Rows.Where(row => row is { Channel: NotificationChannels.Email, Language: AppLanguage.Arabic }),
            row => Assert.StartsWith("<div dir=\"rtl\">", row.Body, StringComparison.Ordinal));
    }

    [Fact]
    public void The_seed_migrations_come_after_each_module_s_existing_ones()
    {
        Assert.True(string.CompareOrdinal(AddServiceRequestCategoriesLookup.MigrationId, AddContactTopicsLookup.MigrationId) > 0);
        Assert.True(string.CompareOrdinal(AddServiceRequestAttachmentTypes.MigrationId, AttachmentsMigrationIds.InitialCreate) > 0);
        Assert.True(string.CompareOrdinal(AddServiceRequestTemplates.MigrationId, NotificationsMigrationIds.InitialCreate) > 0);
    }

    [GeneratedRegex(@"\{\{([A-Za-z0-9]+)\}\}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Placeholder();
}
