using System.Text.Json;
using Codelab.Security.PublicIds;
using Starter.Application.Messaging;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Domain.Localization;
using Starter.Domain.Users;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Accounts;
using Starter.Modules.Users.Domain;
using Starter.Modules.Users.Tests.Fakes;

namespace Starter.Modules.Users.Tests.Application;

public sealed class UserValidatorTests
{
    [Fact]
    public void A_complete_external_body_is_valid() =>
        Assert.True(new CreateUserCommandValidator().Validate(Body(organizationId: PublicId<OrganizationIds>.From(1))).IsValid);

    [Fact]
    public void An_internal_user_needs_no_organization() =>
        Assert.True(new CreateUserCommandValidator().Validate(Body(kind: UserKind.@Internal)).IsValid);

    [Fact]
    public void An_external_user_needs_an_organization()
    {
        var errors = new CreateUserCommandValidator().Validate(Body(kind: UserKind.External)).ToDictionary();

        Assert.Equal([ValidationMessages.Required], errors["organizationId"]);
    }

    [Fact]
    public void Every_field_rule_is_reported_at_once()
    {
        var command = new CreateUserCommand(
            UserName: null, DisplayName: " ", Email: "not-an-address", LoginName: new string('l', UsersLimits.LoginNameMaxLength + 1),
            Upn: new string('u', UsersLimits.UpnMaxLength + 1), Kind: null, Binding: (AccountBinding)7, OrganizationId: null);

        var errors = new CreateUserCommandValidator().Validate(command).ToDictionary();

        Assert.Equal([ValidationMessages.Required], errors["userName"]);
        Assert.Equal([ValidationMessages.Required], errors["displayName"]);
        Assert.Equal([ValidationMessages.InvalidFormat], errors["email"]);
        Assert.Equal([ValidationMessages.TooLong], errors["loginName"]);
        Assert.Equal([ValidationMessages.TooLong], errors["upn"]);
        Assert.Equal([ValidationMessages.Required], errors["kind"]);
        Assert.Equal([ValidationMessages.InvalidFormat], errors["binding"]);
    }

    [Fact]
    public void A_too_long_email_is_too_long()
    {
        var errors = new UpdateUserCommandValidator()
            .Validate(new UpdateUserCommand("Name", new string('e', UsersLimits.EmailMaxLength) + "@example.test", null, null, UserKind.@Internal, AccountBinding.Sso, null))
            .ToDictionary();

        Assert.Equal([ValidationMessages.TooLong], errors["email"]);
    }

    [Fact]
    public void Blank_optional_text_becomes_null_in_the_changes()
    {
        var changes = Body(organizationId: PublicId<OrganizationIds>.From(3)).Fields with { Email = "  ", LoginName = " ad\\amal ", Upn = null };

        var mapped = changes.ToChanges();

        Assert.Null(mapped.Email);
        Assert.Equal("ad\\amal", mapped.LoginName);
        Assert.Equal(3, mapped.OrganizationId);
    }

    internal static CreateUserCommand Body(
        UserKind kind = UserKind.External, AccountBinding binding = AccountBinding.Password, PublicId<OrganizationIds>? organizationId = null, string userName = "amal") =>
        new(userName, "Amal", "amal@example.test", null, null, kind, binding, organizationId);
}

public sealed class OrganizationMembershipTests
{
    [Theory]
    [InlineData(OrganizationKind.External, true, MembershipCheck.Allowed)]
    [InlineData(OrganizationKind.@Internal, true, MembershipCheck.KindMismatch)]
    [InlineData(OrganizationKind.External, false, MembershipCheck.OrganizationInactive)]
    public void An_external_user_needs_an_active_external_organization(OrganizationKind kind, bool isActive, MembershipCheck expected)
    {
        var organization = new FakeOrganizations().Add("A", kind, isActive);

        Assert.Equal(expected, OrganizationMembership.Check(UserKind.External, organizationRequested: true, organization, alreadyMember: false));
    }

    [Fact]
    public void A_member_may_stay_in_a_deactivated_organization() =>
        Assert.Equal(
            MembershipCheck.Allowed,
            OrganizationMembership.Check(UserKind.External, organizationRequested: true, new FakeOrganizations().Add("A", OrganizationKind.External, isActive: false), alreadyMember: true));

    [Fact]
    public void Missing_and_unknown_organizations()
    {
        Assert.Equal(MembershipCheck.OrganizationRequired, OrganizationMembership.Check(UserKind.External, organizationRequested: false, organization: null, alreadyMember: false));
        Assert.Equal(MembershipCheck.Allowed, OrganizationMembership.Check(UserKind.@Internal, organizationRequested: false, organization: null, alreadyMember: false));
        Assert.Equal(MembershipCheck.OrganizationNotFound, OrganizationMembership.Check(UserKind.@Internal, organizationRequested: true, organization: null, alreadyMember: false));
    }

    [Fact]
    public void Each_failed_check_maps_to_an_unprocessable_refusal()
    {
        Assert.Null(OrganizationMembership.RefusalOf(MembershipCheck.Allowed));
        Assert.Equal(UsersErrorCodes.OrganizationNotFound, OrganizationMembership.RefusalOf(MembershipCheck.OrganizationNotFound)!.Code);
        Assert.Equal(UsersErrorCodes.OrganizationInactive, OrganizationMembership.RefusalOf(MembershipCheck.OrganizationInactive)!.Code);
        Assert.Equal(UsersErrorCodes.OrganizationKindMismatch, OrganizationMembership.RefusalOf(MembershipCheck.KindMismatch)!.Code);
        Assert.Equal(RefusalCategory.Unprocessable, OrganizationMembership.RefusalOf(MembershipCheck.OrganizationRequired)!.Category);
    }
}

public sealed class AccountActivationsTests
{
    [Fact]
    public async Task Issues_the_link_and_a_pending_outbox_message_that_names_the_ticket_by_id_only()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.Add("amal");

        var outcome = await harness.Activations.IssueAsync(account.UserId, CancellationToken.None);

        Assert.Equal($"{UsersHarness.LinkBase}/activate#ticket=sealed-ticket-1", outcome.Value.ActivationLink);
        Assert.Equal(FakeAccounts.TicketExpiry, outcome.Value.ExpiresAt);
        var message = Assert.Single(harness.Outbox.Messages);
        Assert.Equal(OutboxMessageTypes.AccountActivation, message.Type);
        Assert.Null(message.NotBefore);
        Assert.DoesNotContain("sealed-ticket", message.PayloadJson, StringComparison.Ordinal);
        var payload = JsonDocument.Parse(message.PayloadJson).RootElement;
        Assert.Equal($"m1_users_{account.UserId}", payload.GetProperty("userPublicId").GetString());
        Assert.Equal("ticket-id-1", payload.GetProperty("ticketId").GetString());
        Assert.Equal(FakeAccounts.TicketExpiry, payload.GetProperty("expiresAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Without_a_link_base_nothing_is_issued()
    {
        var harness = new UsersHarness { Links = new ActivationLinkSettings(LinkBase: null) };
        var account = harness.Accounts.Add("amal");

        var outcome = await harness.Activations.IssueAsync(account.UserId, CancellationToken.None);

        Assert.Equal(UsersErrorCodes.ActivationLinkUnconfigured, outcome.Refusal!.Code);
        Assert.DoesNotContain(harness.Accounts.Calls, call => call.StartsWith("activation", StringComparison.Ordinal));
        Assert.Empty(harness.Outbox.Messages);
    }

    [Fact]
    public async Task An_identity_refusal_passes_through_and_enqueues_nothing()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.Add("amal", binding: AccountBinding.Sso);

        var outcome = await harness.Activations.IssueAsync(account.UserId, CancellationToken.None);

        Assert.Equal(FakeAccounts.NotPasswordCode, outcome.Refusal!.Code);
        Assert.Empty(harness.Outbox.Messages);
    }
}

public sealed class ActivationLinkSettingsTests
{
    [Theory]
    [InlineData("http://localhost:4280")]
    [InlineData("http://localhost:4280/")]
    public void The_link_puts_the_escaped_ticket_in_the_fragment(string linkBase) =>
        Assert.Equal("http://localhost:4280/activate#ticket=a%2Bb%2Fc", new ActivationLinkSettings(new Uri(linkBase)).LinkFor("a+b/c"));

    [Fact]
    public void A_base_path_is_kept() =>
        Assert.Equal("https://portal.example.test/app/activate#ticket=t", new ActivationLinkSettings(new Uri("https://portal.example.test/app/")).LinkFor("t"));

    [Fact]
    public void An_unconfigured_base_cannot_build_links()
    {
        var settings = new ActivationLinkSettings(LinkBase: null);

        Assert.False(settings.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => settings.LinkFor("t"));
    }
}

public sealed class ListUsersTests
{
    [Fact]
    public async Task Joins_the_organization_names_in_the_request_language()
    {
        var harness = new UsersHarness { Language = { Language = AppLanguage.Arabic } };
        var organization = harness.Organizations.Add("FIN", OrganizationKind.External);
        harness.Accounts.Add("amal", organizationId: organization.Id);
        harness.Accounts.Add("badr", kind: UserKind.@Internal);

        var page = await harness.ListUsers()
            .ExecuteAsync(new UserListQuery(null, null, null, null, Page: null, PageSize: null), CancellationToken.None);

        Assert.Equal(2, page.Value.Total);
        Assert.Equal("FIN ar", page.Value.Items[0].OrganizationName);
        Assert.Null(page.Value.Items[1].OrganizationName);
    }

    [Fact]
    public async Task Passes_the_filters_and_the_normalized_page()
    {
        var harness = new UsersHarness();

        await harness.ListUsers()
            .ExecuteAsync(new UserListQuery(" am ", PublicId<OrganizationIds>.From(4), UserKind.External, IsActive: false, Page: 2, PageSize: 1000), CancellationToken.None);

        var query = Assert.Single(harness.Accounts.Queries);
        Assert.Equal("am", query.Search);
        Assert.Equal(4, query.OrganizationId);
        Assert.Equal(UserKind.External, query.Kind);
        Assert.False(query.IsActive);
        Assert.Equal(2, query.Page);
        Assert.Equal(UsersLimits.MaxPageSize, query.PageSize);
    }
}

public sealed class GetUserTests
{
    [Fact]
    public async Task An_unknown_user_is_404_and_a_known_one_is_returned()
    {
        var harness = new UsersHarness();
        var account = harness.Accounts.Add("amal");
        var useCase = harness.GetUser();

        var missing = await useCase.ExecuteAsync(PublicId<UsersIds>.From(999), CancellationToken.None);
        var found = await useCase.ExecuteAsync(PublicId<UsersIds>.From(account.UserId), CancellationToken.None);

        Assert.Equal(UsersErrorCodes.UserNotFound, missing.Refusal!.Code);
        Assert.Equal("amal", found.Value.Account.UserName);
    }
}

public sealed class PagingTests
{
    [Theory]
    [InlineData(null, null, 1, UsersLimits.DefaultPageSize)]
    [InlineData(0, -1, 1, UsersLimits.DefaultPageSize)]
    [InlineData(3, 5, 3, 5)]
    [InlineData(1, 500, 1, UsersLimits.MaxPageSize)]
    public void From_normalizes(int? page, int? size, int expectedPage, int expectedSize) =>
        Assert.Equal(new PageRequest(expectedPage, expectedSize), PageRequest.From(page, size));

    [Fact]
    public void Search_is_trimmed_cut_and_blank_is_null()
    {
        Assert.Null(PageRequest.Search("  "));
        Assert.Equal("x", PageRequest.Search(" x "));
        Assert.Equal(UsersLimits.SearchMaxLength, PageRequest.Search(new string('s', UsersLimits.SearchMaxLength + 5))!.Length);
    }
}
