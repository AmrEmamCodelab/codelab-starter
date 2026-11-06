using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Assignments.Application;
using Starter.Modules.Assignments.Application.Queues;
using Starter.Modules.Assignments.Domain;
using Starter.Modules.Assignments.Tests.Fakes;
using static Starter.Modules.Assignments.Tests.Application.RecordQueuesTests;

namespace Starter.Modules.Assignments.Tests.Application;

public sealed class GetWorkQueuePageTests
{
    private readonly TestWorld _world = new();

    [Fact]
    public async Task The_all_view_lists_open_records_with_status_owner_assignee_and_sla()
    {
        _world.AddRecord(id: 1, status: TestRecordStatuses.Submitted, minutesAgo: 10);
        _world.AddRecord(id: 2, status: TestRecordStatuses.InReview, minutesAgo: 5);
        _world.AddRecord(id: 3, status: TestRecordStatuses.Draft);
        _world.AddRecord(id: 4, status: TestRecordStatuses.Closed);
        _world.SeedOpen(recordId: 2, assigneeUserId: TestWorld.OtherStaffUserId);
        _world.Sla.Badges[2] = new SlaBadge(SlaState.DueSoon, TestWorld.Now.AddHours(2));

        var page = (await _world.WorkQueue.ExecuteAsync(TestWorld.Request(QueueViews.All), CancellationToken.None)).Value;

        Assert.Equal([2L, 1L], page.Items.Select(item => item.Id));
        Assert.Equal(2, page.Total);
        var held = page.Items[0];
        Assert.Equal("TR-2026-002", held.Number);
        Assert.Equal("in-review", held.StatusKey);
        Assert.Equal(TestWorld.OwnerName, held.OwnerDisplayName);
        Assert.Equal(TestWorld.OtherStaffName, held.Assignee!.AssigneeDisplayName);
        Assert.Equal(SlaState.DueSoon, held.Sla!.State);
        Assert.Null(page.Items[1].Assignee);
        Assert.Null(page.Items[1].Sla);
    }

    [Fact]
    public async Task The_unassigned_view_skips_held_records_and_counts_only_the_rest()
    {
        for (var id = 1; id <= 5; id++)
        {
            _world.AddRecord(id: id, minutesAgo: id);
        }

        _world.SeedOpen(recordId: 2, assigneeUserId: TestWorld.OtherStaffUserId);
        _world.SeedOpen(recordId: 4, assigneeUserId: TestWorld.StaffUserId);

        var first = (await _world.WorkQueue.ExecuteAsync(TestWorld.Request(QueueViews.Unassigned, pageSize: 2), CancellationToken.None)).Value;
        var second = (await _world.WorkQueue.ExecuteAsync(TestWorld.Request(QueueViews.Unassigned, page: 2, pageSize: 2), CancellationToken.None)).Value;

        Assert.Equal([1L, 3L], first.Items.Select(item => item.Id));
        Assert.Equal([5L], second.Items.Select(item => item.Id));
        Assert.Equal(3, first.Total);
        Assert.Equal(3, second.Total);
    }

    [Fact]
    public async Task The_unassigned_view_reads_the_directory_page_directly_when_nothing_is_held()
    {
        _world.AddRecord(id: 1);

        var page = (await _world.WorkQueue.ExecuteAsync(TestWorld.Request(QueueViews.Unassigned), CancellationToken.None)).Value;

        Assert.Equal([1L], page.Items.Select(item => item.Id));
        Assert.Single(_world.Directory.Queries);
    }

    [Fact]
    public async Task The_unassigned_view_pages_past_one_directory_batch()
    {
        var count = RecordsLimits.MaxPageSize + 10;
        for (var id = 1; id <= count; id++)
        {
            _world.AddRecord(id: id, minutesAgo: id);
        }

        _world.SeedOpen(recordId: 1, assigneeUserId: TestWorld.StaffUserId);

        var page = (await _world.WorkQueue.ExecuteAsync(TestWorld.Request(QueueViews.Unassigned, page: 6, pageSize: 20), CancellationToken.None)).Value;

        Assert.Equal(count - 1, page.Total);
        Assert.Equal(Enumerable.Range(102, 9).Select(id => (long)id), page.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task The_mine_view_lists_only_the_callers_records()
    {
        _world.AddRecord(id: 1);
        _world.AddRecord(id: 2);
        _world.SeedOpen(recordId: 1, assigneeUserId: TestWorld.OtherStaffUserId);
        _world.SeedOpen(recordId: 2, assigneeUserId: TestWorld.StaffUserId);

        var page = (await _world.WorkQueue.ExecuteAsync(TestWorld.Request(QueueViews.Mine), CancellationToken.None)).Value;

        Assert.Equal([2L], page.Items.Select(item => item.Id));
        Assert.Equal(TestWorld.StaffName, page.Items[0].Assignee!.AssigneeDisplayName);
    }

    [Fact]
    public async Task The_mine_view_of_a_caller_holding_nothing_is_empty_without_a_directory_read()
    {
        _world.AddRecord(id: 1);

        var page = (await _world.WorkQueue.ExecuteAsync(TestWorld.Request(QueueViews.Mine), CancellationToken.None)).Value;

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
        Assert.Empty(_world.Directory.Queries);
    }

    [Fact]
    public async Task An_own_organization_holder_sees_only_their_organizations_records()
    {
        _world.Scope.Decisions[TestRecordType.QueuePermission] = TestWorld.OwnOrganizationOnly;
        _world.AddRecord(id: 1, organizationId: TestWorld.OwnOrganization);
        _world.AddRecord(id: 2, organizationId: TestWorld.OtherOrganization);
        _world.AddRecord(id: 3, organizationId: null);

        var page = (await _world.WorkQueue.ExecuteAsync(TestWorld.Request(QueueViews.All), CancellationToken.None)).Value;

        Assert.Equal([1L], page.Items.Select(item => item.Id));
        Assert.Equal(TestWorld.OwnOrganization, Assert.Single(_world.Directory.Queries).OrganizationId);
    }

    [Fact]
    public async Task An_own_organization_holder_without_an_organization_sees_nothing()
    {
        _world.Scope.Decisions[TestRecordType.QueuePermission] = new ScopeDecision(Allowed: true, Scope: PermissionScope.OwnOrganization, OrganizationId: null);
        _world.AddRecord(id: 1);

        var page = (await _world.WorkQueue.ExecuteAsync(TestWorld.Request(QueueViews.All), CancellationToken.None)).Value;

        Assert.Empty(page.Items);
        Assert.Empty(_world.Directory.Queries);
    }

    [Fact]
    public async Task A_global_holder_sees_every_organization()
    {
        _world.AddRecord(id: 1, organizationId: TestWorld.OwnOrganization);
        _world.AddRecord(id: 2, organizationId: TestWorld.OtherOrganization);

        var page = (await _world.WorkQueue.ExecuteAsync(TestWorld.Request(QueueViews.All), CancellationToken.None)).Value;

        Assert.Equal(2, page.Total);
        Assert.Null(Assert.Single(_world.Directory.Queries).OrganizationId);
    }

    [Fact]
    public async Task The_status_and_number_filters_reach_the_directory()
    {
        _world.AddRecord(id: 1, status: TestRecordStatuses.Submitted);
        _world.AddRecord(id: 12, status: TestRecordStatuses.InReview);
        _world.AddRecord(id: 13, status: TestRecordStatuses.InReview);

        var page = (await _world.WorkQueue.ExecuteAsync(
            TestWorld.Request(QueueViews.All, status: TestRecordStatuses.InReview, number: " 012 "), CancellationToken.None)).Value;

        Assert.Equal([12L], page.Items.Select(item => item.Id));
        Assert.Equal("012", Assert.Single(_world.Directory.Queries).NumberContains);
    }

    [Theory]
    [InlineData(TestRecordStatuses.Draft)]
    [InlineData(TestRecordStatuses.Closed)]
    [InlineData(42)]
    public async Task A_status_that_is_not_an_open_queue_status_is_invalid(int status)
    {
        var listed = await _world.WorkQueue.ExecuteAsync(TestWorld.Request(QueueViews.All, status: status), CancellationToken.None);

        AssertRefused(listed, RefusalCategory.Invalid, PlatformErrorCodes.Validation);
        Assert.Equal([ValidationMessages.InvalidFormat], listed.Refusal!.FieldErrors["status"]);
    }

    [Fact]
    public async Task The_page_size_is_capped_and_defaulted()
    {
        _world.AddRecord(id: 1);

        var capped = (await _world.WorkQueue.ExecuteAsync(TestWorld.Request(QueueViews.All, page: 0, pageSize: 1000), CancellationToken.None)).Value;
        var defaulted = (await _world.WorkQueue.ExecuteAsync(TestWorld.Request(QueueViews.All), CancellationToken.None)).Value;

        Assert.Equal(1, capped.Page);
        Assert.Equal(RecordsLimits.MaxPageSize, capped.PageSize);
        Assert.Equal(RecordsLimits.DefaultPageSize, defaulted.PageSize);
    }

    [Fact]
    public async Task A_caller_without_the_queue_permission_is_forbidden()
    {
        _world.Scope.Decisions.Clear();

        var listed = await _world.WorkQueue.ExecuteAsync(TestWorld.Request(QueueViews.All), CancellationToken.None);

        AssertRefused(listed, RefusalCategory.Forbidden, AssignmentsErrorCodes.QueueForbidden);
    }

    [Fact]
    public async Task An_invalid_request_is_refused_before_anything_is_read()
    {
        var listed = await _world.WorkQueue.ExecuteAsync(TestWorld.Request("everything"), CancellationToken.None);

        AssertRefused(listed, RefusalCategory.Invalid, PlatformErrorCodes.Validation);
        Assert.Empty(_world.Directory.Queries);
    }
}

public sealed class WorkQueueRequestValidatorTests
{
    private static readonly DateTimeOffset Instant = TestWorld.Now;

    [Theory]
    [InlineData("unassigned", QueueView.Unassigned)]
    [InlineData(" Mine ", QueueView.Mine)]
    [InlineData("ALL", QueueView.All)]
    public void The_view_words_parse_ignoring_case_and_spaces(string word, QueueView view)
    {
        Assert.Equal(view, QueueViews.Parse(word));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("other")]
    public void An_unknown_view_word_parses_to_nothing(string? word)
    {
        Assert.Null(QueueViews.Parse(word));
    }

    [Fact]
    public void A_valid_request_has_no_error()
    {
        var errors = Validate(TestWorld.Request(QueueViews.All, number: "TR-2026"));

        Assert.True(errors.IsValid);
    }

    [Fact]
    public void Every_field_error_is_reported_at_once()
    {
        var request = new WorkQueueRequest(
            RecordType: TestRecordType.Key,
            View: " ",
            Status: null,
            Number: new string('1', AssignmentsLimits.NumberSearchMaxLength + 1),
            SubmittedFrom: Instant,
            SubmittedTo: Instant.AddDays(-1),
            Page: null,
            PageSize: null);

        var errors = Validate(request).ToDictionary();

        Assert.Equal([ValidationMessages.Required], errors["view"]);
        Assert.Equal([ValidationMessages.TooLong], errors["number"]);
        Assert.Equal([ValidationMessages.InvalidFormat], errors["submittedTo"]);
    }

    [Fact]
    public void An_unknown_view_is_an_invalid_format()
    {
        var errors = Validate(TestWorld.Request("everything")).ToDictionary();

        Assert.Equal([ValidationMessages.InvalidFormat], errors["view"]);
    }

    [Fact]
    public void A_blank_number_is_no_search()
    {
        Assert.Null(TestWorld.Request(QueueViews.All, number: "  ").NumberSearch);
    }

    private static ValidationErrors Validate(WorkQueueRequest request) => new WorkQueueRequestValidator().Validate(request);
}
