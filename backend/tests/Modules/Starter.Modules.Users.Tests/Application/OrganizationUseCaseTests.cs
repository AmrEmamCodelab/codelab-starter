using Codelab.Security.PublicIds;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Organizations;
using Starter.Modules.Users.Domain;
using Starter.Modules.Users.Tests.Fakes;

namespace Starter.Modules.Users.Tests.Application;

public sealed class OrganizationValidatorTests
{
    [Fact]
    public void A_complete_body_is_valid() =>
        Assert.True(new CreateOrganizationCommandValidator().Validate(new CreateOrganizationCommand("FIN-1", "Finance", "المالية", OrganizationKind.External)).IsValid);

    [Fact]
    public void Every_missing_field_is_reported_at_once()
    {
        var errors = new CreateOrganizationCommandValidator().Validate(new CreateOrganizationCommand(null, " ", null, null)).ToDictionary();

        Assert.Equal([ValidationMessages.Required], errors["code"]);
        Assert.Equal([ValidationMessages.Required], errors["nameEn"]);
        Assert.Equal([ValidationMessages.Required], errors["nameAr"]);
        Assert.Equal([ValidationMessages.Required], errors["kind"]);
    }

    [Fact]
    public void A_malformed_code_an_undefined_kind_and_long_names_are_refused()
    {
        var longName = new string('n', UsersLimits.OrganizationNameMaxLength + 1);

        var errors = new UpdateOrganizationCommandValidator().Validate(new UpdateOrganizationCommand("FIN 1", longName, "ar", (OrganizationKind)9)).ToDictionary();

        Assert.Equal([ValidationMessages.InvalidFormat], errors["code"]);
        Assert.Equal([ValidationMessages.TooLong], errors["nameEn"]);
        Assert.Equal([ValidationMessages.InvalidFormat], errors["kind"]);
        Assert.False(errors.ContainsKey("nameAr"));
    }

    [Fact]
    public void A_too_long_code_is_too_long_not_malformed()
    {
        var errors = new CreateOrganizationCommandValidator()
            .Validate(new CreateOrganizationCommand(new string('A', UsersLimits.OrganizationCodeMaxLength + 1), "en", "ar", OrganizationKind.External)).ToDictionary();

        Assert.Equal([ValidationMessages.TooLong], errors["code"]);
    }
}

public sealed class ListOrganizationsTests
{
    [Fact]
    public async Task Paging_values_are_normalized()
    {
        var harness = new UsersHarness();
        harness.Organizations.Add("A", OrganizationKind.External);
        harness.Organizations.Add("B", OrganizationKind.External, isActive: false);

        var page = await harness.ListOrganizations()
            .ExecuteAsync(new OrganizationListQuery(Search: "  ", Kind: null, IsActive: true, Page: -3, PageSize: 0), CancellationToken.None);

        Assert.Equal(1, page.Value.Total);
        Assert.Equal("A", Assert.Single(page.Value.Items).Code);
    }
}

public sealed class GetOrganizationTests
{
    [Fact]
    public async Task An_unknown_id_is_404()
    {
        var outcome = await new UsersHarness().GetOrganization().ExecuteAsync(PublicId<OrganizationIds>.From(7), CancellationToken.None);

        Assert.Equal(RefusalCategory.NotFound, outcome.Refusal!.Category);
        Assert.Equal(UsersErrorCodes.OrganizationNotFound, outcome.Refusal.Code);
    }

    [Fact]
    public async Task A_known_id_is_returned()
    {
        var harness = new UsersHarness();
        var organization = harness.Organizations.Add("A", OrganizationKind.External);

        var outcome = await harness.GetOrganization().ExecuteAsync(PublicId<OrganizationIds>.From(organization.Id), CancellationToken.None);

        Assert.Same(organization, outcome.Value);
    }
}

public sealed class CreateOrganizationTests
{
    [Fact]
    public async Task Creates_an_active_organization_with_a_normalized_code()
    {
        var harness = new UsersHarness();

        var outcome = await harness.CreateOrganization().ExecuteAsync(new CreateOrganizationCommand(" fin ", "Finance", "المالية", OrganizationKind.External), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("FIN", outcome.Value.Code);
        Assert.True(outcome.Value.IsActive);
        Assert.Equal(1, harness.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Refuses_an_invalid_body_and_writes_nothing()
    {
        var harness = new UsersHarness();

        var outcome = await harness.CreateOrganization().ExecuteAsync(new CreateOrganizationCommand(null, null, null, null), CancellationToken.None);

        Assert.Equal(PlatformErrorCodes.Validation, outcome.Refusal!.Code);
        Assert.Empty(harness.Organizations.Items);
        Assert.Equal(0, harness.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Refuses_a_taken_code_ignoring_case()
    {
        var harness = new UsersHarness();
        harness.Organizations.Add("FIN", OrganizationKind.External);

        var outcome = await harness.CreateOrganization().ExecuteAsync(new CreateOrganizationCommand("fin", "x", "y", OrganizationKind.External), CancellationToken.None);

        Assert.Equal(RefusalCategory.Conflict, outcome.Refusal!.Category);
        Assert.Equal(UsersErrorCodes.OrganizationCodeTaken, outcome.Refusal.Code);
    }

    [Fact]
    public async Task A_lost_race_on_the_unique_index_is_the_same_conflict()
    {
        var harness = new UsersHarness();
        harness.UnitOfWork.NextTrySaveSucceeds = false;

        var outcome = await harness.CreateOrganization().ExecuteAsync(new CreateOrganizationCommand("FIN", "x", "y", OrganizationKind.External), CancellationToken.None);

        Assert.Equal(UsersErrorCodes.OrganizationCodeTaken, outcome.Refusal!.Code);
    }
}

public sealed class UpdateOrganizationTests
{
    [Fact]
    public async Task Changes_the_values()
    {
        var harness = new UsersHarness();
        var organization = harness.Organizations.Add("FIN", OrganizationKind.External);

        var outcome = await harness.UpdateOrganization().ExecuteAsync(Id(organization), new UpdateOrganizationCommand("FIN2", "New", "جديد", OrganizationKind.External), CancellationToken.None);

        Assert.Equal("FIN2", outcome.Value.Code);
        Assert.Equal("New", outcome.Value.NameEn);
        Assert.Equal(harness.Clock.GetUtcNow(), outcome.Value.UpdatedAt);
    }

    [Fact]
    public async Task Keeping_its_own_code_is_not_a_conflict()
    {
        var harness = new UsersHarness();
        var organization = harness.Organizations.Add("FIN", OrganizationKind.External);

        var outcome = await harness.UpdateOrganization().ExecuteAsync(Id(organization), new UpdateOrganizationCommand("fin", "x", "y", OrganizationKind.External), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public async Task Taking_another_organizations_code_is_a_conflict()
    {
        var harness = new UsersHarness();
        var organization = harness.Organizations.Add("FIN", OrganizationKind.External);
        harness.Organizations.Add("HR", OrganizationKind.External);

        var outcome = await harness.UpdateOrganization().ExecuteAsync(Id(organization), new UpdateOrganizationCommand("HR", "x", "y", OrganizationKind.External), CancellationToken.None);

        Assert.Equal(UsersErrorCodes.OrganizationCodeTaken, outcome.Refusal!.Code);
    }

    [Fact]
    public async Task The_kind_cannot_change_while_accounts_belong_to_it()
    {
        var harness = new UsersHarness();
        var organization = harness.Organizations.Add("FIN", OrganizationKind.External);
        harness.Accounts.Add("member", organizationId: organization.Id);

        var outcome = await harness.UpdateOrganization().ExecuteAsync(Id(organization), new UpdateOrganizationCommand("FIN", "x", "y", OrganizationKind.@Internal), CancellationToken.None);

        Assert.Equal(RefusalCategory.Conflict, outcome.Refusal!.Category);
        Assert.Equal(UsersErrorCodes.OrganizationKindInUse, outcome.Refusal.Code);
        Assert.Equal(OrganizationKind.External, organization.Kind);
    }

    [Fact]
    public async Task The_kind_of_an_empty_organization_may_change()
    {
        var harness = new UsersHarness();
        var organization = harness.Organizations.Add("FIN", OrganizationKind.External);

        var outcome = await harness.UpdateOrganization().ExecuteAsync(Id(organization), new UpdateOrganizationCommand("FIN", "x", "y", OrganizationKind.@Internal), CancellationToken.None);

        Assert.Equal(OrganizationKind.@Internal, outcome.Value.Kind);
    }

    [Fact]
    public async Task An_unknown_organization_is_404_and_an_invalid_body_is_400()
    {
        var harness = new UsersHarness();

        var missing = await harness.UpdateOrganization().ExecuteAsync(PublicId<OrganizationIds>.From(99), new UpdateOrganizationCommand("A", "x", "y", OrganizationKind.External), CancellationToken.None);
        var invalid = await harness.UpdateOrganization().ExecuteAsync(PublicId<OrganizationIds>.From(99), new UpdateOrganizationCommand(null, "x", "y", OrganizationKind.External), CancellationToken.None);

        Assert.Equal(UsersErrorCodes.OrganizationNotFound, missing.Refusal!.Code);
        Assert.Equal(PlatformErrorCodes.Validation, invalid.Refusal!.Code);
    }

    private static PublicId<OrganizationIds> Id(Organization organization) => PublicId<OrganizationIds>.From(organization.Id);
}

public sealed class SetOrganizationActiveTests
{
    [Fact]
    public async Task Deactivates_and_saves_once_then_repeating_is_a_no_op()
    {
        var harness = new UsersHarness();
        var organization = harness.Organizations.Add("FIN", OrganizationKind.External);
        var useCase = harness.SetOrganizationActive();

        var first = await useCase.ExecuteAsync(PublicId<OrganizationIds>.From(organization.Id), isActive: false, CancellationToken.None);
        var second = await useCase.ExecuteAsync(PublicId<OrganizationIds>.From(organization.Id), isActive: false, CancellationToken.None);

        Assert.True(first.IsSuccess && second.IsSuccess);
        Assert.False(organization.IsActive);
        Assert.Equal(1, harness.UnitOfWork.Saves);
    }

    [Fact]
    public async Task An_unknown_organization_is_404()
    {
        var harness = new UsersHarness();

        var outcome = await harness.SetOrganizationActive()
            .ExecuteAsync(PublicId<OrganizationIds>.From(5), isActive: true, CancellationToken.None);

        Assert.Equal(UsersErrorCodes.OrganizationNotFound, outcome.Refusal!.Code);
    }
}
