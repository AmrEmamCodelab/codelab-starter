using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Jobs;
using Starter.Application.Notifications;
using Starter.Application.Records;
using Starter.Domain.Localization;
using Starter.Infrastructure.Localization;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests.Deadlines;

/// <summary>
/// Phase 7 exit E2E #9 on real SQL: a record of a test-only type enters a status with a 3-day SLA (through the Deadlines
/// transition observer), the host <see cref="TimeProvider"/> is advanced past the deadline, the <c>deadlines-sla-scan</c>
/// job (run as the system actor) marks the breach and writes ONE escalation notification row for the assignee, and a
/// second run writes none. The SLA badge moves from on track to breached, and a record that left the status in time
/// is never escalated.
/// </summary>
[Collection(DeadlinesHostGroup.Name)]
public sealed class SlaBreachEscalationTests(DeadlinesHost host) : IAsyncLifetime
{
    private const string TemplateSubjectAr = "تجاوز المهلة {{recordNumber}}";
    private const string TemplateSubjectEn = "Deadline passed {{recordNumber}}";
    private const string TemplateBody = "{{recordNumber}} {{statusKey}} {{dueAt}}";
    private const string ActionKey = "start-review";
    private const string NewestNotifications = "/api/notifications?take=10";
    private static readonly TimeSpan PastTheDeadline = TestRecordSla.Review + TimeSpan.FromMinutes(1);

    private const string InsertTemplate =
        "IF NOT EXISTS (SELECT 1 FROM [notifications].[NotificationTemplates] WHERE [Key] = @key AND [Channel] = @channel AND [Language] = @language) "
        + "INSERT INTO [notifications].[NotificationTemplates] ([Key], [Channel], [Language], [Subject], [Body]) VALUES (@key, @channel, @language, @subject, @body)";

    private const string EscalationsFor =
        "SELECT COUNT(*) FROM [notifications].[Notifications] WHERE [TemplateKey] = @key AND [UserId] = @user";

    private const string EscalatedDeadlines =
        "SELECT COUNT(*) FROM [deadlines].[RecordDeadlines] WHERE [RecordId] = @record AND [BreachedAt] IS NOT NULL AND [EscalatedAt] IS NOT NULL";

    private const string BreakGlassId = "SELECT [Id] FROM [identity].[UserAccounts] WHERE [UserName] = @name";

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        foreach (var channel in new[] { NotificationChannels.InApp, NotificationChannels.Email })
        {
            await SeedTemplateAsync(channel, AppLanguage.Arabic, TemplateSubjectAr);
            await SeedTemplateAsync(channel, AppLanguage.English, TemplateSubjectEn);
        }
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_breach_past_three_days_raises_one_escalation_notification_and_a_rerun_raises_none()
    {
        var assignee = await BreakGlassUserIdAsync();
        var recordId = NewRecord(assignee);
        await EnterUnderReviewAsync(recordId);
        Assert.Equal(SlaState.OnTrack, (await BadgeAsync(recordId))!.State);
        var before = await EscalationCountAsync(assignee);

        try
        {
            host.Clock.Advance(PastTheDeadline);
            await RunScanAsync();
            await RunScanAsync();

            Assert.Equal(before + 1, await EscalationCountAsync(assignee));
            Assert.Equal(1, await CountAsync(EscalatedDeadlines, new Dictionary<string, object> { ["@record"] = recordId }));
            Assert.Equal(SlaState.Breached, (await BadgeAsync(recordId))!.State);
        }
        finally
        {
            host.Clock.Reset();
        }

        await AssertBellShowsEscalationAsync(NumberOf(recordId));
    }

    [Fact]
    public async Task A_record_that_left_the_status_in_time_is_never_escalated()
    {
        var assignee = await BreakGlassUserIdAsync();
        var recordId = NewRecord(assignee);
        await EnterUnderReviewAsync(recordId);
        await TransitionAsync(recordId, from: TestRecordSla.UnderReview, to: TestRecordSla.Returned);
        host.Records[recordId] = host.Records[recordId] with { Status = TestRecordSla.Returned };
        var before = await EscalationCountAsync(assignee);

        try
        {
            host.Clock.Advance(PastTheDeadline);
            await RunScanAsync();

            Assert.Equal(before, await EscalationCountAsync(assignee));
            Assert.Null(await BadgeAsync(recordId));
        }
        finally
        {
            host.Clock.Reset();
        }
    }

    private long NewRecord(long assignee)
    {
        var recordId = Random.Shared.NextInt64(minValue: 100_000, maxValue: 999_999);
        var now = host.Clock.GetUtcNow();
        host.Records[recordId] = new RecordSummary(
            Id: recordId,
            RecordType: TestRecordSla.RecordType,
            Number: NumberOf(recordId),
            Status: TestRecordSla.UnderReview,
            OwnerUserId: assignee,
            OrganizationId: null,
            CreatedAt: now,
            SubmittedAt: now,
            UpdatedAt: now,
            IsTerminal: false);
        host.Assignees[recordId] = assignee;
        return recordId;
    }

    private static string NumberOf(long recordId) =>
        string.Create(CultureInfo.InvariantCulture, $"{TestRecordSla.Prefix}-2026-{recordId}");

    private Task EnterUnderReviewAsync(long recordId) =>
        TransitionAsync(recordId, from: TestRecordSla.Submitted, to: TestRecordSla.UnderReview);

    private async Task TransitionAsync(long recordId, int from, int to)
    {
        await using var scope = host.Factory.Services.CreateAsyncScope();
        var transition = new RecordTransitioned(
            RecordId: recordId,
            RecordType: TestRecordSla.RecordType,
            FromStatus: from,
            ToStatus: to,
            ActionKey: ActionKey,
            ActorUserId: host.Records[recordId].OwnerUserId,
            ToTerminal: false,
            At: host.Clock.GetUtcNow());
        var observers = scope.ServiceProvider.GetServices<IRecordTransitionObserver>().ToList();
        Assert.NotEmpty(observers);
        foreach (var observer in observers)
        {
            await observer.OnTransitionedAsync(transition, CancellationToken.None);
        }
    }

    private async Task RunScanAsync()
    {
        await using var scope = host.Factory.Services.CreateAsyncScope();
        var job = Assert.Single(scope.ServiceProvider.GetServices<IJob>(), candidate => candidate.Key == TestRecordSla.ScanJobKey);
        using var system = scope.ServiceProvider.GetRequiredService<ISystemActorScope>().BeginSystemScope();
        await job.RunAsync(
            new JobRunContext(job.Key, RunId: 0, JobTrigger.Manual, CorrelationId: Guid.NewGuid().ToString("N")),
            CancellationToken.None);
    }

    private async Task<SlaBadge?> BadgeAsync(long recordId)
    {
        await using var scope = host.Factory.Services.CreateAsyncScope();
        var reader = Assert.Single(scope.ServiceProvider.GetServices<IRecordSlaReader>());
        return (await reader.BadgesAsync([recordId], CancellationToken.None)).GetValueOrDefault(recordId);
    }

    private async Task AssertBellShowsEscalationAsync(string number)
    {
        var token = await host.AdminTokenAsync();
        using var api = host.CreateApi();
        using var response = await api.GetAsync(NewestNotifications, token, LanguageTags.English);
        var page = await ApiClient.ReadAsync(response, HttpStatusCode.OK);

        Assert.Contains(
            page.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("titleText").GetString() == TemplateSubjectEn.Replace("{{recordNumber}}", number, StringComparison.Ordinal));
    }

    private async Task<long> BreakGlassUserIdAsync() =>
        Convert.ToInt64(
            await TestSqlServer.ScalarAsync(host.DatabaseName, BreakGlassId, new Dictionary<string, object> { ["@name"] = host.Accounts.BreakGlassUserName }),
            CultureInfo.InvariantCulture);

    private Task<int> EscalationCountAsync(long userId) =>
        CountAsync(EscalationsFor, new Dictionary<string, object> { ["@key"] = TestRecordSla.EscalationTemplate, ["@user"] = userId });

    private async Task<int> CountAsync(string sql, IReadOnlyDictionary<string, object> parameters) =>
        Convert.ToInt32(await TestSqlServer.ScalarAsync(host.DatabaseName, sql, parameters), CultureInfo.InvariantCulture);

    private Task<int> SeedTemplateAsync(NotificationChannels channel, AppLanguage language, string subject) =>
        TestSqlServer.ExecuteAsync(
            host.DatabaseName,
            InsertTemplate,
            new Dictionary<string, object>
            {
                ["@key"] = TestRecordSla.EscalationTemplate,
                ["@channel"] = (int)channel,
                ["@language"] = (int)language,
                ["@subject"] = subject,
                ["@body"] = TemplateBody,
            });
}
