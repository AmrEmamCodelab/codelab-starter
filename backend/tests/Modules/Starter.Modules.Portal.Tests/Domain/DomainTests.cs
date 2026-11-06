using System.Reflection;
using Starter.Domain.Auditing;
using Starter.Domain.Localization;
using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Tests.Domain;

public sealed class PortalPagesTests
{
    [Fact]
    public void Every_page_has_a_key_that_finds_it_again()
    {
        Assert.Equal(Enum.GetValues<PortalPage>(), PortalPages.All);
        foreach (var page in PortalPages.All)
        {
            Assert.Equal(page, PortalPages.Find(PortalPages.KeyOf(page)));
        }
    }

    [Fact]
    public void The_keys_are_the_frontends_page_keys()
    {
        Assert.Equal(["faq", "service-guide", "privacy", "terms"], PortalPages.All.Select(PortalPages.KeyOf));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Faq")]
    [InlineData("landing")]
    public void An_unknown_key_finds_nothing(string? key)
    {
        Assert.Null(PortalPages.Find(key));
    }

    [Fact]
    public void An_undefined_page_has_no_key()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PortalPages.KeyOf((PortalPage)99));
    }
}

public sealed class EmailShapeTests
{
    [Theory]
    [InlineData("a@example.test", true)]
    [InlineData("first.last+tag@sub.example.test", true)]
    [InlineData("a@b", false)]
    [InlineData("a b@example.test", false)]
    [InlineData("a@b.c\r\nBcc: x@y.z", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_one_at_sign_a_dotted_domain_and_no_white_space_pass(string? address, bool expected)
    {
        Assert.Equal(expected, EmailShape.IsWellFormed(address));
    }
}

public sealed class ContentSectionTests
{
    private static readonly SectionText Original = new("عنوان", "Title", "نص", "Body", 10);

    [Fact]
    public void Edit_reports_a_change_only_when_a_value_differs()
    {
        var section = ContentSection.Create(PortalPage.Faq, Original);

        Assert.False(section.Edit(Original));
        Assert.True(section.Edit(Original with { SortOrder = 20 }));
        Assert.Equal(20, section.SortOrder);
        Assert.True(section.Edit(Original with { BodyEn = "Other" }));
        Assert.Equal("Other", section.BodyEn);
        Assert.Equal(PortalPage.Faq, section.Page);
    }

    [Fact]
    public void A_page_header_starts_without_an_update_time_and_records_one()
    {
        var page = ContentPage.Seeded(PortalPage.Terms);
        var at = new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);

        Assert.Null(page.UpdatedAt);
        page.Touch(at);
        Assert.Equal(at, page.UpdatedAt);
    }
}

public sealed class ContactMessageTests
{
    [Fact]
    public void A_message_keeps_its_values()
    {
        var at = new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);

        var message = ContactMessage.Receive(new NewContactMessage("CU-2026-001", "Sara", "sara@example.test", 3, "Hello there, team.", AppLanguage.English, at));

        Assert.Equal("CU-2026-001", message.ReferenceNumber);
        Assert.Equal(3, message.TopicId);
        Assert.Equal(AppLanguage.English, message.Language);
        Assert.Equal(at, message.ReceivedAt);
    }

    [Fact]
    public void A_blank_value_is_a_caller_defect()
    {
        Assert.Throws<ArgumentException>(() =>
            ContactMessage.Receive(new NewContactMessage("CU-2026-001", " ", "sara@example.test", 1, "Hello there, team.", AppLanguage.Arabic, DateTimeOffset.MinValue)));
    }

    [Theory]
    [InlineData(nameof(ContactMessage.FullName), PersonalDataKind.Name)]
    [InlineData(nameof(ContactMessage.Email), PersonalDataKind.Email)]
    [InlineData(nameof(ContactMessage.Message), PersonalDataKind.FreeText)]
    public void What_the_sender_typed_is_personal_data(string property, PersonalDataKind kind)
    {
        var attribute = typeof(ContactMessage).GetProperty(property)!.GetCustomAttribute<PersonalDataAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(kind, attribute.Kind);
    }
}
