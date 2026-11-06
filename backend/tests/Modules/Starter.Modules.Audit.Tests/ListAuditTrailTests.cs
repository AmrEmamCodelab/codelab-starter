using Codelab.Security.PublicIds;
using Starter.Application.Auditing;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Domain.Auditing;
using Starter.Modules.Audit.Api;
using Starter.Modules.Audit.Application;

namespace Starter.Modules.Audit.Tests;

public sealed class AuditTrailListQueryValidatorTests
{
    private readonly AuditTrailListQueryValidator _validator = new();

    [Fact]
    public void An_empty_filter_is_valid() =>
        Assert.True(_validator.Validate(new AuditTrailListQuery(EntityType: null, EntityId: null, Before: null, Take: null)).IsValid);

    [Theory]
    [InlineData(1)]
    [InlineData(AuditPaging.MaxTake)]
    public void A_page_size_inside_the_bounds_is_valid(int take) =>
        Assert.True(_validator.Validate(new AuditTrailListQuery("Organization", EntityId: null, Before: null, take)).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(AuditPaging.MaxTake + 1)]
    public void A_page_size_outside_the_bounds_is_refused(int take)
    {
        var errors = _validator.Validate(new AuditTrailListQuery(EntityType: null, EntityId: null, Before: null, take)).ToDictionary();

        Assert.Equal([ValidationMessages.InvalidFormat], errors["take"]);
    }

    [Fact]
    public void An_entity_id_without_a_type_is_refused_on_the_type()
    {
        var errors = _validator.Validate(new AuditTrailListQuery(EntityType: null, PublicId<AuditedEntityIds>.From(5), Before: null, Take: null)).ToDictionary();

        Assert.Equal([ValidationMessages.Required], errors["entityType"]);
    }

    [Theory]
    [InlineData("   ", ValidationMessages.Required)]
    [InlineData(null, null)]
    public void A_blank_type_is_refused(string? entityType, string? expected)
    {
        var errors = _validator.Validate(new AuditTrailListQuery(entityType, EntityId: null, Before: null, Take: null)).ToDictionary();

        if (expected is null)
        {
            Assert.Empty(errors);
        }
        else
        {
            Assert.Equal([expected], errors["entityType"]);
        }
    }

    [Fact]
    public void A_type_longer_than_the_column_is_refused()
    {
        var errors = _validator.Validate(new AuditTrailListQuery(new string('t', AuditPaging.EntityTypeMaxLength + 1), EntityId: null, Before: null, Take: null)).ToDictionary();

        Assert.Equal([ValidationMessages.TooLong], errors["entityType"]);
    }
}

public sealed class ListAuditTrailTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    public ListAuditTrailTests() => ReadableCodec.EnsureInstalled();

    [Fact]
    public async Task The_defaults_ask_for_the_newest_page_of_everything()
    {
        var trail = new FakeTrail();

        await UseCase(trail).ExecuteAsync(new AuditTrailListQuery(EntityType: null, EntityId: null, Before: null, Take: null), CancellationToken.None);

        Assert.Equal(new AuditTrailQuery(EntityType: null, EntityKey: null, BeforeId: null, AuditPaging.DefaultTake), trail.Asked);
    }

    [Fact]
    public async Task The_filters_and_the_cursor_reach_the_port_as_internal_values()
    {
        var trail = new FakeTrail();
        var query = new AuditTrailListQuery(" Organization ", PublicId<AuditedEntityIds>.From(42), PublicId<AuditEntryIds>.From(900), Take: 5);

        await UseCase(trail).ExecuteAsync(query, CancellationToken.None);

        Assert.Equal(new AuditTrailQuery("Organization", "42", 900, 5), trail.Asked);
    }

    [Fact]
    public async Task An_invalid_filter_is_refused_with_the_module_code_and_never_reaches_the_port()
    {
        var trail = new FakeTrail();

        var outcome = await UseCase(trail).ExecuteAsync(new AuditTrailListQuery(EntityType: null, PublicId<AuditedEntityIds>.From(1), Before: null, Take: 0), CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(RefusalCategory.Invalid, outcome.Refusal!.Category);
        Assert.Equal(AuditErrorCodes.InvalidFilter, outcome.Refusal.Code);
        Assert.Equal(["entityType", "take"], outcome.Refusal.FieldErrors.Keys.Order(StringComparer.Ordinal));
        Assert.Null(trail.Asked);
    }

    [Fact]
    public async Task Every_internal_id_leaves_as_a_PublicId_and_other_values_pass_through()
    {
        var trail = new FakeTrail
        {
            Page = new AuditTrailPage(
                [new AuditTrailEntry(77, "Organization", "42", AuditAction.Modified, AuditActors.System, "corr", At,
                [
                    new AuditPropertyChange("ParentId", "3", "4", IsIdentifier: true),
                    new AuditPropertyChange("ParentId2", null, "0", IsIdentifier: true),
                    new AuditPropertyChange("Email", "a***@example.test", "b***@example.test", IsIdentifier: false),
                ])],
                HasMore: true),
        };

        var page = (await UseCase(trail).ExecuteAsync(new AuditTrailListQuery(EntityType: null, EntityId: null, Before: null, Take: null), CancellationToken.None)).Value;

        Assert.True(page.HasMore);
        var entry = Assert.Single(page.Items);
        Assert.Equal(77, entry.Id.Value);
        Assert.Equal(42, entry.EntityId!.Value.Value);
        Assert.Equal(AuditAction.Modified, entry.Action);
        Assert.Equal(AuditActors.System, entry.Actor);
        Assert.Equal(At, entry.At);
        Assert.Equal(new AuditChangeView("ParentId", ReadableCodec.TokenOf(AuditedEntityIds.DomainLabel, 3), ReadableCodec.TokenOf(AuditedEntityIds.DomainLabel, 4)), entry.Changes[0]);
        Assert.Equal(new AuditChangeView("ParentId2", null, null), entry.Changes[1]);
        Assert.Equal(new AuditChangeView("Email", "a***@example.test", "b***@example.test"), entry.Changes[2]);
    }

    [Theory]
    [InlineData("""{"RoleId":"3","PermissionId":"7"}""")]
    [InlineData("code-a")]
    [InlineData("0")]
    [InlineData("-4")]
    [InlineData(null)]
    public void A_key_that_is_not_one_positive_integer_has_no_entity_id(string? key) =>
        Assert.Null(AuditViews.Of(new AuditTrailEntry(1, "RolePermission", key, AuditAction.Added, AuditActors.System, CorrelationId: null, At, [])).EntityId);

    [Fact]
    public void The_response_carries_the_view_unchanged()
    {
        var view = new AuditEntryView(PublicId<AuditEntryIds>.From(9), "Role", PublicId<AuditedEntityIds>.From(2), AuditAction.Deleted, "SYSTEM", At, [new AuditChangeView("Name", "Old", NewValue: null)]);

        var response = AuditPageResponse.From(new AuditEntryViewPage([view], HasMore: false));

        var entry = Assert.Single(response.Items);
        Assert.Equal(view.Id, entry.Id);
        Assert.Equal(view.EntityId, entry.EntityId);
        Assert.Equal(AuditAction.Deleted, entry.Action);
        Assert.Equal(new AuditChangeResponse("Name", "Old", NewValue: null), Assert.Single(entry.Changes));
        Assert.False(response.HasMore);
    }

    private static ListAuditTrail UseCase(FakeTrail trail) => new(new AuditTrailListQueryValidator(), trail);

    private sealed class FakeTrail : IAuditTrailQueries
    {
        public AuditTrailPage Page { get; init; } = new([], HasMore: false);

        public AuditTrailQuery? Asked { get; private set; }

        public Task<AuditTrailPage> FindAsync(AuditTrailQuery query, CancellationToken cancellationToken)
        {
            Asked = query;
            return Task.FromResult(Page);
        }
    }
}
