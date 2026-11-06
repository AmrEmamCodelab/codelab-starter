using Codelab.Security.PublicIds;
using Microsoft.Extensions.Logging;
using Starter.Application.Messaging;
using Starter.Application.Notifications;
using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Domain.Exceptions;
using Starter.Domain.Users;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Accounts;
using Starter.Modules.Users.Infrastructure;
using Starter.Modules.Users.Tests.Fakes;

namespace Starter.Modules.Users.Tests.Application;

public sealed class AccountActivationEmailHandlerTests
{
    private const string Email = "amal@example.test";
    private const long MessageId = 42;

    [Fact]
    public void It_handles_the_account_activation_type() =>
        Assert.Equal(OutboxMessageTypes.AccountActivation, new UsersHarness().ActivationEmailHandler().MessageType);

    [Fact]
    public async Task Issues_a_fresh_ticket_and_emails_its_link_with_the_activation_template()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.AddWithEmail("amal", Email);
        var delivery = await EnqueuedDeliveryAsync(harness, account);

        await harness.ActivationEmailHandler().HandleAsync(delivery, CancellationToken.None);

        Assert.Equal([$"activation:{account.UserId}", $"activation:{account.UserId}"], harness.Accounts.Calls);
        var sent = Assert.Single(harness.Notifications.ImmediateEmails);
        Assert.Equal(account.UserId, sent.UserId);
        Assert.Equal(NotificationTemplateKeys.AccountActivation, sent.TemplateKey);
        Assert.Equal(NotificationChannels.Email, sent.Channels);
        Assert.Null(sent.Link);
        Assert.Equal($"{UsersHarness.LinkBase}/activate#ticket=sealed-ticket-2", sent.Placeholders[NotificationPlaceholders.ActivationLink]);
        Assert.Equal("2026-11-08 09:00 UTC", sent.Placeholders[NotificationPlaceholders.ExpiresAt]);
        Assert.Equal(2, sent.Placeholders.Count);
    }

    [Fact]
    public async Task The_ticket_reaches_neither_the_payload_nor_a_log_line()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.AddWithEmail("amal", Email);
        var delivery = await EnqueuedDeliveryAsync(harness, account);

        await harness.ActivationEmailHandler().HandleAsync(delivery, CancellationToken.None);

        Assert.DoesNotContain("sealed-ticket", delivery.PayloadJson, StringComparison.Ordinal);
        var line = Assert.Single(harness.HandlerLog.Lines);
        Assert.DoesNotContain("sealed-ticket", line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("#ticket=", line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Email, line.Message, StringComparison.Ordinal);
        Assert.Contains("ticket-id-2", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_redelivery_after_the_user_activated_sends_nothing_and_never_clears_the_password()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.AddWithEmail("amal", Email);
        var delivery = await EnqueuedDeliveryAsync(harness, account);
        harness.Accounts.Accounts[0] = account with { HasPassword = true };
        harness.Accounts.Calls.Clear();

        await harness.ActivationEmailHandler().HandleAsync(delivery, CancellationToken.None);

        Assert.Empty(harness.Notifications.ImmediateEmails);
        Assert.Empty(harness.Accounts.Calls);
        Assert.Contains(((int)ActivationEmailSkip.AlreadyActivated).ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(harness.HandlerLog.Lines).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_redelivery_before_activation_sends_a_newer_link_that_supersedes_the_first()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.AddWithEmail("amal", Email);
        var delivery = await EnqueuedDeliveryAsync(harness, account);
        var handler = harness.ActivationEmailHandler();

        await handler.HandleAsync(delivery, CancellationToken.None);
        await handler.HandleAsync(delivery with { Attempt = 2 }, CancellationToken.None);

        Assert.Equal(
            [$"{UsersHarness.LinkBase}/activate#ticket=sealed-ticket-2", $"{UsersHarness.LinkBase}/activate#ticket=sealed-ticket-3"],
            harness.Notifications.ImmediateEmails.Select(email => email.Placeholders[NotificationPlaceholders.ActivationLink]));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{}")]
    [InlineData("{\"userPublicId\":\"someone-else\",\"ticketId\":\"t\",\"expiresAt\":\"2026-11-08T09:00:00Z\"}")]
    public async Task An_unreadable_payload_is_logged_and_completed_without_an_email(string payloadJson)
    {
        var harness = new UsersHarness();

        await harness.ActivationEmailHandler().HandleAsync(new OutboxDelivery(MessageId, OutboxMessageTypes.AccountActivation, payloadJson, Attempt: 1), CancellationToken.None);

        Assert.Empty(harness.Notifications.ImmediateEmails);
        Assert.Empty(harness.Accounts.Calls);
        Assert.Equal(LogLevel.Warning, Assert.Single(harness.HandlerLog.Lines).Level);
    }

    [Fact]
    public async Task A_message_whose_account_is_no_longer_due_is_completed_without_issuing()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.AddWithEmail("amal", Email);
        var delivery = await EnqueuedDeliveryAsync(harness, account);
        harness.Accounts.Accounts[0] = account with { IsActive = false };
        harness.Accounts.Calls.Clear();

        await harness.ActivationEmailHandler().HandleAsync(delivery, CancellationToken.None);

        Assert.Empty(harness.Accounts.Calls);
        Assert.Empty(harness.Notifications.ImmediateEmails);
    }

    [Fact]
    public async Task An_identity_refusal_throws_its_code_so_the_outbox_retries()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.AddWithEmail("amal", Email);
        var delivery = await EnqueuedDeliveryAsync(harness, account);
        harness.Accounts.IssueRefusal = Refusal.Unprocessable("IDENTITY_ACTIVATION_UNAVAILABLE");

        var thrown = await Assert.ThrowsAsync<BusinessRuleException>(() => harness.ActivationEmailHandler().HandleAsync(delivery, CancellationToken.None));

        Assert.Equal("IDENTITY_ACTIVATION_UNAVAILABLE", thrown.Code);
        Assert.Empty(harness.Notifications.ImmediateEmails);
    }

    [Fact]
    public async Task A_send_failure_propagates_and_the_retry_sends_a_fresh_link()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.AddWithEmail("amal", Email);
        var delivery = await EnqueuedDeliveryAsync(harness, account);
        harness.Notifications.FailNextSend = true;
        var handler = harness.ActivationEmailHandler();

        await Assert.ThrowsAsync<BusinessRuleException>(() => handler.HandleAsync(delivery, CancellationToken.None));
        await handler.HandleAsync(delivery with { Attempt = 2 }, CancellationToken.None);

        var sent = Assert.Single(harness.Notifications.ImmediateEmails);
        Assert.Equal($"{UsersHarness.LinkBase}/activate#ticket=sealed-ticket-3", sent.Placeholders[NotificationPlaceholders.ActivationLink]);
    }

    [Fact]
    public async Task A_link_base_removed_since_the_enqueue_throws_before_anything_is_issued()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.AddWithEmail("amal", Email);
        var delivery = await EnqueuedDeliveryAsync(harness, account);
        harness.Links = new ActivationLinkSettings(LinkBase: null);
        harness.Accounts.Calls.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.ActivationEmailHandler().HandleAsync(delivery, CancellationToken.None));

        Assert.Empty(harness.Accounts.Calls);
    }

    [Fact]
    public void The_expiry_is_written_in_utc_whatever_the_offset() =>
        Assert.Equal(
            "2026-11-08 06:00 UTC",
            AccountActivationEmailHandler.PlaceholdersOf("link", new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.FromHours(3)))[NotificationPlaceholders.ExpiresAt]);

    private static async Task<OutboxDelivery> EnqueuedDeliveryAsync(UsersHarness harness, AccountSummary account)
    {
        var issued = await harness.Activations.IssueAsync(account, CancellationToken.None);
        Assert.Equal(ActivationDelivery.Email, issued.Value.Delivery);
        var message = Assert.Single(harness.Outbox.Messages);
        return new OutboxDelivery(MessageId, message.Type, message.PayloadJson, Attempt: 1);
    }
}

public sealed class ActivationDeliveryRulesTests
{
    private static readonly AccountSummary Pending = new(
        UserId: 7, SubjectId: Guid.NewGuid(), UserName: "amal", DisplayName: "Amal", Email: "amal@example.test", LoginName: null, Upn: null,
        Kind: UserKind.External, Binding: AccountBinding.Password, OrganizationId: null, IsActive: true, HasPassword: false, IsLockedOut: false, IsBreakGlass: false);

    [Fact]
    public void An_address_means_email_and_none_means_the_administrators_link()
    {
        Assert.Equal(ActivationDelivery.Email, ActivationDeliveryRules.DeliveryFor(Pending));
        Assert.Equal(ActivationDelivery.AdministratorLink, ActivationDeliveryRules.DeliveryFor(Pending with { Email = null }));
        Assert.Equal(ActivationDelivery.AdministratorLink, ActivationDeliveryRules.DeliveryFor(Pending with { Email = " " }));
    }

    [Fact]
    public void A_pending_password_account_with_an_address_is_due() =>
        Assert.Null(ActivationDeliveryRules.SkipReasonOf(Pending));

    [Fact]
    public void Every_stale_state_has_its_reason()
    {
        Assert.Equal(ActivationEmailSkip.AccountNotFound, ActivationDeliveryRules.SkipReasonOf(null));
        Assert.Equal(ActivationEmailSkip.NotPasswordBound, ActivationDeliveryRules.SkipReasonOf(Pending with { Binding = AccountBinding.Sso }));
        Assert.Equal(ActivationEmailSkip.BreakGlass, ActivationDeliveryRules.SkipReasonOf(Pending with { IsBreakGlass = true }));
        Assert.Equal(ActivationEmailSkip.Inactive, ActivationDeliveryRules.SkipReasonOf(Pending with { IsActive = false }));
        Assert.Equal(ActivationEmailSkip.AlreadyActivated, ActivationDeliveryRules.SkipReasonOf(Pending with { HasPassword = true }));
        Assert.Equal(ActivationEmailSkip.NoEmailAddress, ActivationDeliveryRules.SkipReasonOf(Pending with { Email = null }));
    }

    [Fact]
    public void The_enums_keep_their_stored_values()
    {
        Assert.Equal(1, (int)ActivationDelivery.AdministratorLink);
        Assert.Equal(2, (int)ActivationDelivery.Email);
        Assert.Equal([1, 2, 3, 4, 5, 6], Enum.GetValues<ActivationEmailSkip>().Select(value => (int)value));
    }
}

public sealed class ActivationPayloadsTests
{
    [Fact]
    public void A_payload_round_trips_in_web_casing()
    {
        var payload = new AccountActivationPayload("m1_users_7", "ticket-id-1", new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero));

        var json = ActivationPayloads.Write(payload);

        Assert.Contains("\"userPublicId\"", json, StringComparison.Ordinal);
        Assert.Equal(payload, ActivationPayloads.TryRead(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"ticketId\":\"t\"}")]
    public void A_malformed_payload_reads_as_null(string json) =>
        Assert.Null(ActivationPayloads.TryRead(json));
}

public sealed class UserPublicIdsTests
{
    [Fact]
    public void A_token_round_trips_and_a_foreign_token_is_refused()
    {
        using var codec = PublicIdCodecAccessor.Install(new LabelledCodec());
        var publicIds = new UserPublicIds();

        Assert.Equal(7, publicIds.IdOf(publicIds.TokenOf(7)));
        Assert.Null(publicIds.IdOf(PublicId<OrganizationIds>.From(7).ToToken()));
        Assert.Null(publicIds.IdOf("not-a-token"));
        Assert.Null(publicIds.IdOf(null));
    }
}

/// <summary>A readable stand-in for the host codec (<c>domain:id</c>); Codelab.Security tests the real cryptography.</summary>
internal sealed class LabelledCodec : IPublicIdCodec
{
    private const char Separator = ':';

    public string Protect(string domain, long id) => domain + Separator + id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public bool TryUnprotect(string domain, string? token, out long id)
    {
        id = 0;
        var prefix = domain + Separator;
        return token is not null && token.StartsWith(prefix, StringComparison.Ordinal)
            && long.TryParse(token.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out id);
    }
}
