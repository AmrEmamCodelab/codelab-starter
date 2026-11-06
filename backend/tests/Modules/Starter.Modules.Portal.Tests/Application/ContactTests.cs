using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Notifications;
using Starter.Application.Results;
using Starter.Application.Settings;
using Starter.Application.Validation;
using Starter.Domain.Localization;
using Starter.Infrastructure.Localization;
using Starter.Modules.Portal.Application;
using Starter.Modules.Portal.Application.Contact;
using Starter.Modules.Portal.Domain;
using Starter.Modules.Portal.Tests.Fakes;

namespace Starter.Modules.Portal.Tests.Application;

public sealed class SubmitContactMessageValidatorTests
{
    private static readonly SubmitContactMessageValidator Validator = new();

    [Fact]
    public void A_complete_message_is_valid()
    {
        Assert.True(Validator.Validate(ContactSamples.Valid()).IsValid);
    }

    [Fact]
    public void Every_missing_field_is_reported_at_once()
    {
        var errors = Validator.Validate(new SubmitContactMessageCommand(null, "  ", null, null, null)).ToDictionary();

        Assert.Equal([ValidationMessages.Required], errors["fullName"]);
        Assert.Equal([ValidationMessages.Required], errors["email"]);
        Assert.Equal([ValidationMessages.Required], errors["topicId"]);
        Assert.Equal([ValidationMessages.Required], errors["message"]);
        Assert.Equal([ValidationMessages.Required], errors["language"]);
    }

    [Theory]
    [InlineData("no-at-sign.example.test")]
    [InlineData("two@@example.test")]
    [InlineData("user@nodot")]
    [InlineData("user name@example.test")]
    public void A_malformed_email_is_refused(string email)
    {
        var errors = Validator.Validate(ContactSamples.Valid() with { Email = email }).ToDictionary();

        Assert.Equal([ValidationMessages.InvalidFormat], errors["email"]);
    }

    [Fact]
    public void Lengths_are_checked_after_trimming()
    {
        var atLimit = ContactSamples.Valid() with
        {
            FullName = "  " + new string('a', PortalLimits.FullNameMaxLength) + "  ",
            Message = " " + new string('m', PortalLimits.MessageMinLength) + " ",
        };
        var tooLong = ContactSamples.Valid() with
        {
            FullName = new string('a', PortalLimits.FullNameMaxLength + 1),
            Email = new string('e', PortalLimits.EmailMaxLength) + "@example.test",
            Message = new string('m', PortalLimits.MessageMaxLength + 1),
        };

        Assert.True(Validator.Validate(atLimit).IsValid);
        var errors = Validator.Validate(tooLong).ToDictionary();
        Assert.Equal([ValidationMessages.TooLong], errors["fullName"]);
        Assert.Equal([ValidationMessages.TooLong], errors["email"]);
        Assert.Equal([ValidationMessages.TooLong], errors["message"]);
    }

    [Fact]
    public void A_message_shorter_than_the_minimum_is_too_short()
    {
        var errors = Validator.Validate(ContactSamples.Valid() with { Message = "  short  " }).ToDictionary();

        Assert.Equal([PortalValidationMessages.TooShort], errors["message"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_topic_id_below_one_is_invalid(int topicId)
    {
        var errors = Validator.Validate(ContactSamples.Valid() with { TopicId = topicId }).ToDictionary();

        Assert.Equal([ValidationMessages.InvalidFormat], errors["topicId"]);
    }

    [Theory]
    [InlineData("ar", true)]
    [InlineData("EN", true)]
    [InlineData(" en ", true)]
    [InlineData("fr", false)]
    [InlineData("arabic", false)]
    public void Only_ar_and_en_are_languages(string language, bool valid)
    {
        Assert.Equal(valid, Validator.Validate(ContactSamples.Valid() with { Language = language }).IsValid);
    }

    [Fact]
    public void The_language_codes_are_the_hosts_language_tags()
    {
        Assert.Equal(LanguageTags.Arabic, ContactLanguageCodes.Arabic);
        Assert.Equal(LanguageTags.English, ContactLanguageCodes.English);
        Assert.Equal(AppLanguage.Arabic, ContactLanguageCodes.Find("ar"));
        Assert.Equal(AppLanguage.English, ContactLanguageCodes.Find("en"));
        Assert.Null(ContactLanguageCodes.Find(null));
    }
}

public sealed class SubmitContactMessageTests
{
    private readonly ContactRig _rig = new();

    [Fact]
    public async Task A_valid_message_gets_a_CU_number_is_stored_trimmed_and_then_announced()
    {
        var outcome = await _rig.UseCase.ExecuteAsync(
            ContactSamples.Valid() with { FullName = "  Sara Ahmed ", Email = " sara@example.test ", Message = "  " + ContactSamples.Text + "  " },
            CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("CU-2026-001", outcome.Value.ReferenceNumber);
        Assert.Equal([PortalNumberPrefixes.ContactUs], _rig.Numbers.Prefixes);
        Assert.Equal([JournalCalls.Allocate, JournalCalls.Save, JournalCalls.Notify], _rig.Journal.Calls);
        var stored = Assert.Single(_rig.Store.Saved);
        Assert.Equal("Sara Ahmed", stored.FullName);
        Assert.Equal("sara@example.test", stored.Email);
        Assert.Equal(ContactSamples.Text, stored.Message);
        Assert.Equal(1, stored.TopicId);
        Assert.Equal(AppLanguage.English, stored.Language);
        Assert.Equal(ContactRig.Now, stored.ReceivedAt);
        Assert.Equal("CU-2026-001", stored.ReferenceNumber);
    }

    [Fact]
    public async Task The_inbox_gets_the_contact_received_template_with_every_placeholder_in_its_language()
    {
        await _rig.UseCase.ExecuteAsync(ContactSamples.Valid(), CancellationToken.None);

        var sent = Assert.Single(_rig.Notifications.Addressed);
        Assert.Equal(ContactRig.Inbox.EmailAddress, sent.EmailAddress);
        Assert.Equal(NotificationTemplateKeys.ContactReceived, sent.TemplateKey);
        Assert.Equal(AppLanguage.Arabic, sent.Language);
        Assert.Equal("CU-2026-001", sent.Placeholders[NotificationPlaceholders.ReferenceNumber]);
        Assert.Equal("Sara Ahmed", sent.Placeholders[NotificationPlaceholders.FullName]);
        Assert.Equal("sara@example.test", sent.Placeholders[NotificationPlaceholders.Email]);
        Assert.Equal("استفسار عام", sent.Placeholders[NotificationPlaceholders.Topic]);
        Assert.Equal(ContactSamples.Text, sent.Placeholders[NotificationPlaceholders.Message]);
    }

    [Fact]
    public async Task An_invalid_message_is_refused_before_anything_is_numbered_stored_or_sent()
    {
        var outcome = await _rig.UseCase.ExecuteAsync(ContactSamples.Valid() with { Email = "bad" }, CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(PlatformErrorCodes.Validation, outcome.Refusal!.Code);
        Assert.Equal(RefusalCategory.Invalid, outcome.Refusal.Category);
        Assert.Empty(_rig.Journal.Calls);
        Assert.Empty(_rig.Topics.Asked);
    }

    [Fact]
    public async Task An_unknown_or_retired_topic_is_a_field_error_and_nothing_is_numbered()
    {
        var outcome = await _rig.UseCase.ExecuteAsync(ContactSamples.Valid() with { TopicId = 9 }, CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(PortalErrorCodes.TopicNotFound, outcome.Refusal!.Code);
        Assert.Equal(RefusalCategory.Invalid, outcome.Refusal.Category);
        Assert.Equal([PortalErrorCodes.TopicNotFound], outcome.Refusal.FieldErrors["topicId"]);
        Assert.Empty(_rig.Journal.Calls);
    }

    [Fact]
    public async Task A_switched_off_contact_form_refuses_before_anything_is_validated_numbered_stored_or_sent()
    {
        _rig.Settings.ContactEnabled = false;

        var outcome = await _rig.UseCase.ExecuteAsync(ContactSamples.Valid(), CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(PortalErrorCodes.ContactDisabled, outcome.Refusal!.Code);
        Assert.Equal(RefusalCategory.Conflict, outcome.Refusal.Category);
        Assert.Empty(_rig.Journal.Calls);
        Assert.Empty(_rig.Topics.Asked);
        Assert.Empty(_rig.Store.Saved);
        Assert.Empty(_rig.Notifications.Addressed);
    }

    [Fact]
    public async Task The_contact_form_reads_its_switch_from_the_runtime_settings_on_every_submit()
    {
        await _rig.UseCase.ExecuteAsync(ContactSamples.Valid(), CancellationToken.None);
        _rig.Settings.ContactEnabled = false;
        var refused = await _rig.UseCase.ExecuteAsync(ContactSamples.Valid(), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Single(_rig.Store.Saved);
        Assert.Equal([SettingKeys.PortalContactEnabled, SettingKeys.PortalContactEnabled], _rig.Settings.Read);
    }

    [Fact]
    public async Task A_notification_failure_after_the_save_keeps_the_receipt_and_logs_only_the_reference_number()
    {
        _rig.Notifications.FailWith = new InvalidOperationException("queue down");

        var outcome = await _rig.UseCase.ExecuteAsync(ContactSamples.Valid(), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Single(_rig.Store.Saved);
        var entry = Assert.Single(_rig.Logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("CU-2026-001", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sara@example.test", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Sara", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_while_announcing_propagates()
    {
        _rig.Notifications.FailWith = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _rig.UseCase.ExecuteAsync(ContactSamples.Valid(), CancellationToken.None));
    }

    [Fact]
    public async Task A_failed_save_propagates_and_announces_nothing()
    {
        _rig.Store.FailSaveWith = new InvalidOperationException("database down");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _rig.UseCase.ExecuteAsync(ContactSamples.Valid(), CancellationToken.None));

        Assert.Equal([JournalCalls.Allocate, JournalCalls.Save], _rig.Journal.Calls);
        Assert.Empty(_rig.Notifications.Addressed);
    }

    [Fact]
    public async Task Each_message_gets_the_next_number()
    {
        var first = await _rig.UseCase.ExecuteAsync(ContactSamples.Valid(), CancellationToken.None);
        var second = await _rig.UseCase.ExecuteAsync(ContactSamples.Valid() with { TopicId = 2 }, CancellationToken.None);

        Assert.Equal("CU-2026-001", first.Value.ReferenceNumber);
        Assert.Equal("CU-2026-002", second.Value.ReferenceNumber);
    }
}

public sealed class ContactFilingTests
{
    [Fact]
    public async Task An_unvalidated_language_is_a_caller_defect()
    {
        var rig = new ContactRig();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            rig.Filing.FileAsync(ContactSamples.Valid() with { Language = "fr" }, new ContactTopic(1, "أ", "A"), CancellationToken.None));
        Assert.Empty(rig.Journal.Calls);
    }
}

public sealed class ContactAnnouncerTests
{
    [Fact]
    public async Task The_topic_name_follows_the_inbox_language()
    {
        var journal = new CallJournal();
        var notifications = new FakeNotificationService(journal);
        var announcer = new ContactAnnouncer(notifications, ContactRig.Inbox with { Language = AppLanguage.English }, new ListLogger<ContactAnnouncer>());
        var message = ContactMessage.Receive(new NewContactMessage("CU-2026-007", "Sara", "sara@example.test", 1, ContactSamples.Text, AppLanguage.Arabic, ContactRig.Now));

        await announcer.AnnounceAsync(message, new ContactTopic(1, "استفسار عام", "General question"), CancellationToken.None);

        var sent = Assert.Single(notifications.Addressed);
        Assert.Equal(AppLanguage.English, sent.Language);
        Assert.Equal("General question", sent.Placeholders[NotificationPlaceholders.Topic]);
    }
}

internal static class ContactSamples
{
    public const string Text = "I would like to know how to follow my request.";

    public static SubmitContactMessageCommand Valid() => new(
        FullName: "Sara Ahmed",
        Email: "sara@example.test",
        TopicId: 1,
        Message: Text,
        Language: "en");
}

internal sealed class ContactRig
{
    public static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 30, 0, TimeSpan.Zero);

    public static readonly ContactInbox Inbox = new("contact@example.invalid", AppLanguage.Arabic);

    public ContactRig()
    {
        Numbers = new FakeNumberAllocator(Journal);
        Store = new FakeContactMessageStore(Journal);
        Notifications = new FakeNotificationService(Journal);
        Filing = new ContactFiling(Numbers, Store, new FakeTimeProvider(Now));
        UseCase = new SubmitContactMessage(
            Settings,
            new SubmitContactMessageValidator(),
            Topics,
            Filing,
            new ContactAnnouncer(Notifications, Inbox, Logger));
    }

    public CallJournal Journal { get; } = new();

    public FakeRuntimeSettings Settings { get; } = new();

    public FakeContactTopics Topics { get; } = new();

    public FakeNumberAllocator Numbers { get; }

    public FakeContactMessageStore Store { get; }

    public FakeNotificationService Notifications { get; }

    public ListLogger<ContactAnnouncer> Logger { get; } = new();

    public ContactFiling Filing { get; }

    public SubmitContactMessage UseCase { get; }
}
