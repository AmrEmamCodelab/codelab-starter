using Starter.Application.Authorization;
using Starter.Application.Messaging;
using Starter.Application.Users;
using Starter.Domain.Users;
using static Starter.Domain.Users.UserKind;

namespace Starter.Application.Tests.Messaging;

/// <summary>
/// The phase 3 kernel ports: the stored integer of every enum member (a change would rewrite stored data), the
/// defaults that keep existing <see cref="DirectoryUser"/> callers compiling, and what the outbox payload may carry.
/// </summary>
public sealed class KernelPortContractTests
{
    public static TheoryData<Enum, int> StoredValues => new()
    {
        { Internal, 1 },
        { UserKind.External, 2 },
        { AccountBinding.Password, 1 },
        { AccountBinding.ActiveDirectory, 2 },
        { AccountBinding.Sso, 3 },
        { PermissionScope.Global, 1 },
        { PermissionScope.OwnOrganization, 2 },
        { OutboxMessageStatus.Pending, 1 },
        { OutboxMessageStatus.Sent, 2 },
        { OutboxMessageStatus.Failed, 3 },
    };

    [Theory]
    [MemberData(nameof(StoredValues))]
    public void Every_enum_member_keeps_its_stored_integer(Enum member, int stored) =>
        Assert.Equal(stored, Convert.ToInt32(member, System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(typeof(UserKind), 2)]
    [InlineData(typeof(AccountBinding), 3)]
    [InlineData(typeof(PermissionScope), 2)]
    [InlineData(typeof(OutboxMessageStatus), 3)]
    public void No_enum_gained_an_untested_member(Type enumType, int count) =>
        Assert.Equal(count, Enum.GetValues(enumType).Length);

    [Fact]
    public void A_directory_user_built_the_old_way_is_internal_without_an_organization()
    {
        var user = new DirectoryUser(UserId: 7, IsActive: true, HoldsAllPermissions: false);

        Assert.Equal(Internal, user.Kind);
        Assert.Null(user.OrganizationId);
    }

    [Fact]
    public void A_directory_user_carries_its_kind_and_organization_when_given()
    {
        var user = new DirectoryUser(UserId: 7, IsActive: true, HoldsAllPermissions: false, Kind: UserKind.External, OrganizationId: 42);

        Assert.Equal(UserKind.External, user.Kind);
        Assert.Equal(42, user.OrganizationId);
    }

    [Fact]
    public void A_denied_scope_decision_has_no_scope_and_no_organization()
    {
        Assert.False(ScopeDecision.Denied.Allowed);
        Assert.Null(ScopeDecision.Denied.Scope);
        Assert.Null(ScopeDecision.Denied.OrganizationId);
    }

    [Fact]
    public void The_activation_message_type_is_the_wire_name_the_drain_routes_on() =>
        Assert.Equal("account-activation", OutboxMessageTypes.AccountActivation);

    [Fact]
    public void The_activation_payload_names_the_ticket_but_cannot_carry_it()
    {
        var members = typeof(AccountActivationPayload).GetProperties().Select(property => property.Name).ToList();

        Assert.Equal([nameof(AccountActivationPayload.UserPublicId), nameof(AccountActivationPayload.TicketId), nameof(AccountActivationPayload.ExpiresAt)], members);
        Assert.DoesNotContain(nameof(ActivationIssue.Ticket), members);
    }

    [Fact]
    public void An_account_summary_has_no_password_or_ticket_member()
    {
        var members = typeof(AccountSummary).GetProperties().Select(property => property.Name).ToList();

        Assert.DoesNotContain(members, name => name.Contains("Password", StringComparison.Ordinal) && name != nameof(AccountSummary.HasPassword));
        Assert.DoesNotContain(members, name => name.Contains("Ticket", StringComparison.Ordinal));
    }
}
