using Starter.Application.Records;

namespace Starter.Application.Tests.Records;

/// <summary>Paging bounds of RecordQuery and RecordPageRequest, SlaDefinition validation and the SLA state rule.</summary>
public sealed class RecordQueryAndSlaTests
{
    private const string RecordType = "test-record";
    private const int UnderReview = 3;
    private const string EscalationTemplate = "test-records.sla-escalation";
    private const int TooLargePageSize = 500;
    private const int PageSize = 10;
    private const int SecondPage = 2;

    private static readonly TimeSpan ThreeDays = TimeSpan.FromDays(3);
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private static readonly DateTimeOffset EnteredAt = new(year: 2026, month: 11, day: 6, hour: 9, minute: 0, second: 0, offset: TimeSpan.Zero);

    [Theory]
    [InlineData(null, null, 1, RecordsLimits.DefaultPageSize)]
    [InlineData(0, 0, 1, RecordsLimits.DefaultPageSize)]
    [InlineData(-1, -5, 1, RecordsLimits.DefaultPageSize)]
    [InlineData(SecondPage, PageSize, SecondPage, PageSize)]
    [InlineData(SecondPage, TooLargePageSize, SecondPage, RecordsLimits.MaxPageSize)]
    public void A_raw_page_request_is_normalized(int? page, int? pageSize, int expectedPage, int expectedSize) =>
        Assert.Equal(new RecordPageRequest(expectedPage, expectedSize), RecordPageRequest.From(page, pageSize));

    [Fact]
    public void A_query_keeps_its_filters()
    {
        var query = Query(page: SecondPage, pageSize: PageSize) with { Statuses = [UnderReview] };

        Assert.Equal((RecordType, SecondPage, PageSize), (query.RecordType, query.Page, query.PageSize));
        Assert.Equal([UnderReview], query.Statuses!);
    }

    [Theory]
    [InlineData(0, PageSize)]
    [InlineData(1, 0)]
    [InlineData(1, RecordsLimits.MaxPageSize + 1)]
    public void A_query_out_of_paging_bounds_is_refused(int page, int pageSize) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Query(page, pageSize));

    [Fact]
    public void A_query_with_a_malformed_record_type_is_refused() =>
        Assert.Throws<ArgumentException>(() => Query(page: 1, pageSize: PageSize, recordType: "Test Record"));

    [Fact]
    public void An_sla_keeps_its_values()
    {
        var sla = Sla();

        Assert.Equal((RecordType, UnderReview, ThreeDays, OneDay, EscalationTemplate), (sla.RecordType, sla.Status, sla.Duration, sla.DueSoonBefore, sla.EscalationTemplateKey));
    }

    [Fact]
    public void An_sla_without_a_positive_duration_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Sla(duration: TimeSpan.Zero, dueSoonBefore: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sla(duration: -OneDay, dueSoonBefore: TimeSpan.Zero));
    }

    [Fact]
    public void A_due_soon_window_that_is_negative_or_not_shorter_than_the_duration_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Sla(dueSoonBefore: -OneMinute));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sla(dueSoonBefore: ThreeDays));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void An_sla_without_an_escalation_template_is_refused(string template) =>
        Assert.Throws<ArgumentException>(() => Sla(template: template));

    [Fact]
    public void An_sla_with_a_malformed_record_type_is_refused() =>
        Assert.Throws<ArgumentException>(() => Sla(recordType: "TestRecord"));

    [Fact]
    public void A_record_is_on_track_before_the_due_soon_window()
    {
        var badge = Sla().BadgeAt(enteredAt: EnteredAt, now: EnteredAt + ThreeDays - OneDay - OneMinute);

        Assert.Equal(new SlaBadge(SlaState.OnTrack, EnteredAt + ThreeDays), badge);
    }

    [Fact]
    public void A_record_is_due_soon_from_the_start_of_the_window_until_the_deadline()
    {
        Assert.Equal(SlaState.DueSoon, Sla().BadgeAt(enteredAt: EnteredAt, now: EnteredAt + ThreeDays - OneDay).State);
        Assert.Equal(SlaState.DueSoon, Sla().BadgeAt(enteredAt: EnteredAt, now: EnteredAt + ThreeDays - OneMinute).State);
    }

    [Fact]
    public void A_record_is_breached_from_the_deadline_on()
    {
        Assert.Equal(SlaState.Breached, Sla().BadgeAt(enteredAt: EnteredAt, now: EnteredAt + ThreeDays).State);
        Assert.Equal(SlaState.Breached, Sla().BadgeAt(enteredAt: EnteredAt, now: EnteredAt + ThreeDays + OneDay).State);
    }

    [Fact]
    public void A_zero_due_soon_window_goes_straight_from_on_track_to_breached() =>
        Assert.Equal(
            SlaState.OnTrack,
            Sla(dueSoonBefore: TimeSpan.Zero).BadgeAt(enteredAt: EnteredAt, now: EnteredAt + ThreeDays - OneMinute).State);

    [Fact]
    public void The_sla_state_values_are_a_wire_contract() =>
        Assert.Equal([1, 2, 3], Enum.GetValues<SlaState>().Select(value => (int)value));

    private static RecordQuery Query(int page, int pageSize, string recordType = RecordType) =>
        new(
            RecordType: recordType,
            Statuses: null,
            NumberContains: null,
            OrganizationId: null,
            SubmittedFrom: null,
            SubmittedTo: null,
            Ids: null,
            Page: page,
            PageSize: pageSize);

    private static SlaDefinition Sla(
        string recordType = RecordType,
        TimeSpan? duration = null,
        TimeSpan? dueSoonBefore = null,
        string template = EscalationTemplate) =>
        new(
            RecordType: recordType,
            Status: UnderReview,
            Duration: duration ?? ThreeDays,
            DueSoonBefore: dueSoonBefore ?? OneDay,
            EscalationTemplateKey: template);
}
