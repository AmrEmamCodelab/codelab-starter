using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Domain.Workflow;
using Starter.Modules.Records.Tests.Support;
using Starter.Workflow.Testing;

namespace Starter.Modules.Records.Tests.Application;

public sealed class RecordStandingTests
{
    private readonly RecordsHarness _harness = new();

    [Fact]
    public async Task The_owner_with_view_own_sees_the_record_as_requester()
    {
        var reference = await _harness.DraftAsync();

        var standing = (await _harness.Loader.LoadAsync(reference, CancellationToken.None)).Value.Standing;

        Assert.Equal(WorkflowAudience.Requester, standing.Audience);
        Assert.Equal(TransitionSide.Requester, standing.Side);
        Assert.True(standing.IsOwner);
        Assert.True(standing.Holds(TestRecordPermissionKeys.Create));
    }

    [Fact]
    public async Task A_draft_is_hidden_from_staff_and_oversight()
    {
        var reference = await _harness.DraftAsync();

        foreach (var user in new[] { _harness.Staff, _harness.Admin })
        {
            _harness.SignIn(user);
            Assert.Equal(RecordsErrorCodes.NotFound, (await _harness.Loader.LoadAsync(reference, CancellationToken.None)).Refusal!.Code);
        }
    }

    [Fact]
    public async Task Staff_see_a_submitted_record_of_their_organization_as_staff()
    {
        var reference = await _harness.SubmittedAsync();
        _harness.SignIn(_harness.Staff);

        var standing = (await _harness.Loader.LoadAsync(reference, CancellationToken.None)).Value.Standing;

        Assert.Equal(WorkflowAudience.Staff, standing.Audience);
        Assert.Equal(TransitionSide.Staff, standing.Side);
        Assert.False(standing.IsOwner);
        Assert.True(standing.Holds(TestRecordPermissionKeys.Review));
        Assert.False(standing.Holds(TestRecordPermissionKeys.Create));
    }

    [Fact]
    public async Task An_own_organization_grant_does_not_reach_another_organizations_record()
    {
        var reference = await _harness.SubmittedAsync();
        _harness.SignIn(_harness.OtherStaff);

        Assert.False((await _harness.Loader.LoadAsync(reference, CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task Oversight_sees_a_submitted_record_as_admin()
    {
        var reference = await _harness.SubmittedAsync();
        _harness.SignIn(_harness.Admin);

        Assert.Equal(WorkflowAudience.Admin, (await _harness.Loader.LoadAsync(reference, CancellationToken.None)).Value.Standing.Audience);
    }

    [Fact]
    public async Task An_owner_without_view_own_has_no_requester_view()
    {
        var reference = await _harness.DraftAsync();
        _harness.Requester.Grants.Remove(LifecycleTestRecord.ViewOwn);

        Assert.False((await _harness.Loader.LoadAsync(reference, CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task The_owner_counts_an_own_organization_grant_even_without_an_organization()
    {
        var loner = _harness.Users.Add(userId: 50, "Loner", organizationId: null)
            .Holds(TestRecordPermissionKeys.Create, PermissionScope.OwnOrganization)
            .Holds(LifecycleTestRecord.ViewOwn, PermissionScope.OwnOrganization);
        _harness.SignIn(loner);
        var reference = await _harness.Lifecycle.CreateDraftAsync(TestRecordWorkflow.RecordType, CancellationToken.None);

        var standing = (await _harness.Loader.LoadAsync(reference, CancellationToken.None)).Value.Standing;

        Assert.Equal(WorkflowAudience.Requester, standing.Audience);
        Assert.True(standing.Holds(TestRecordPermissionKeys.Create));
    }

    [Fact]
    public async Task The_loader_hides_a_malformed_or_foreign_record_type_and_refuses_an_unknown_caller()
    {
        var reference = await _harness.DraftAsync();

        Assert.Equal(RecordsErrorCodes.NotFound, (await _harness.Loader.LoadAsync("Not Kebab", reference.Id, CancellationToken.None)).Refusal!.Code);
        Assert.Equal(RecordsErrorCodes.NotFound, (await _harness.Loader.LoadAsync(TestRecordWorkflow.RecordType, recordId: 0, CancellationToken.None)).Refusal!.Code);
        _harness.SignIn(null);
        Assert.Equal(
            Starter.Modules.Records.Application.RecordsModuleErrorCodes.ActorUnknown,
            (await _harness.Loader.LoadAsync(reference, CancellationToken.None)).Refusal!.Code);
    }
}
