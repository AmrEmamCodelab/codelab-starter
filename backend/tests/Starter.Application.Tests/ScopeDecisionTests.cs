using Starter.Application.Authorization;

namespace Starter.Application.Tests;

public sealed class ScopeDecisionTests
{
    private const long Own = 5;
    private const long Other = 6;
    private static readonly ScopeDecision Global = new(Allowed: true, Scope: PermissionScope.Global, OrganizationId: null);
    private static readonly ScopeDecision OwnOrganization = new(Allowed: true, Scope: PermissionScope.OwnOrganization, OrganizationId: Own);
    private static readonly ScopeDecision NoOrganization = new(Allowed: true, Scope: PermissionScope.OwnOrganization, OrganizationId: null);

    [Fact]
    public void Global_reaches_every_record()
    {
        Assert.True(Global.Reaches(Other));
        Assert.True(Global.Reaches(organizationId: null));
    }

    [Fact]
    public void Own_organization_reaches_only_its_organization_and_never_a_record_without_one()
    {
        Assert.True(OwnOrganization.Reaches(Own));
        Assert.False(OwnOrganization.Reaches(Other));
        Assert.False(OwnOrganization.Reaches(organizationId: null));
        Assert.False(NoOrganization.Reaches(organizationId: null));
        Assert.False(ScopeDecision.Denied.Reaches(Own));
    }

    [Fact]
    public void Global_keeps_the_requested_filter()
    {
        Assert.Equal(new OrganizationFilter(MatchesNothing: false, OrganizationId: Other), Global.Narrow(Other));
        Assert.Equal(new OrganizationFilter(MatchesNothing: false, OrganizationId: null), Global.Narrow(requestedOrganizationId: null));
    }

    [Fact]
    public void Own_organization_narrows_to_its_organization_or_matches_nothing()
    {
        Assert.Equal(new OrganizationFilter(MatchesNothing: false, OrganizationId: Own), OwnOrganization.Narrow(requestedOrganizationId: null));
        Assert.Equal(new OrganizationFilter(MatchesNothing: false, OrganizationId: Own), OwnOrganization.Narrow(Own));
        Assert.Same(OrganizationFilter.Nothing, OwnOrganization.Narrow(Other));
        Assert.Same(OrganizationFilter.Nothing, NoOrganization.Narrow(requestedOrganizationId: null));
        Assert.Same(OrganizationFilter.Nothing, ScopeDecision.Denied.Narrow(requestedOrganizationId: null));
    }
}
