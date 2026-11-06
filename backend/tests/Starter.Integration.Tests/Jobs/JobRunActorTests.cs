using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Starter.Application.Jobs;
using Starter.Application.Messaging;
using Starter.Integration.Tests.Support;
using Starter.Integration.Tests.Users;
using Starter.Modules.Jobs.Application;

namespace Starter.Integration.Tests.Jobs;

/// <summary>
/// Exit criterion: a job run is recorded with the actor SYSTEM, and the audited writes it makes carry SYSTEM too. The
/// host runs the real job server (Hangfire on the test database). The administrator creates a user with an e-mail
/// address (an account-activation outbox message), then presses "run now" on outbox-drain: the run delivers the
/// activation (Identity issues a fresh ticket: audited writes) as SYSTEM.
/// </summary>
[Collection(JobServerHostGroup.Name)]
public sealed class JobRunActorTests(JobServerHost host)
{
    private const string OutboxDrainKey = "outbox-drain";
    private const string Runs = "/api/admin/jobs/" + OutboxDrainKey + "/runs";
    private const string RunsQuery = Runs + "?take=20";
    private const int ManualTrigger = (int)JobTrigger.Manual;
    private const int Succeeded = (int)JobRunStatus.Succeeded;
    private const int Failed = (int)JobRunStatus.Failed;
    private const int MaxTriggerAttempts = 3;
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private const string LastManualErrorCode =
        "SELECT TOP 1 [ErrorCode] FROM [jobs].[JobRuns] WHERE [JobKey] = @key AND [Trigger] = @manual ORDER BY [QueuedAt] DESC";
    private const string SucceededRunCorrelations =
        "SELECT [CorrelationId] FROM [jobs].[JobRuns] WHERE [Status] = @succeeded AND [CorrelationId] IS NOT NULL";
    private const string UndeliveredActivations =
        "SELECT COUNT(*) FROM [messaging].[OutboxMessages] WHERE [Type] = @type AND [Status] <> @sent AND [PayloadJson] LIKE @user";
    private const string AuditRowsOfRun = "SELECT COUNT(*) FROM [audit].[AuditEntries] WHERE [CorrelationId] = @correlation";
    private const string NonSystemAuditRowsOfRun =
        "SELECT COUNT(*) FROM [audit].[AuditEntries] WHERE [CorrelationId] = @correlation AND [Actor] <> @system";

    [Fact]
    public async Task A_manual_outbox_drain_runs_as_SYSTEM_and_every_audited_write_of_a_run_is_SYSTEM()
    {
        var adminToken = await host.AdminTokenAsync();
        using var users = new UsersApi(host.Factory.CreateClient());
        using var api = host.CreateApi();
        var suffix = TestNames.Suffix();
        var organizationId = await users.CreateExternalOrganizationAsync("JOB-" + suffix, adminToken);
        var email = $"job.{suffix}@{PiiCanaries.EmailDomain}";
        var userId = await CreateEmailedUserAsync(users, new NewAccount("job.user." + suffix, email, organizationId), adminToken);

        var run = await RunOutboxDrainAsync(api, adminToken);

        Assert.Equal(Succeeded, run.GetProperty("status").GetInt32());
        Assert.Equal(ManualTrigger, run.GetProperty("trigger").GetInt32());
        Assert.Equal(SystemActor.Name, run.GetProperty("actor").GetString());
        var triggeredBy = run.GetProperty("triggeredBy").GetString();
        Assert.False(string.IsNullOrEmpty(triggeredBy)); // the administrator, kept apart from the actor
        Assert.False(Guid.TryParse(triggeredBy, out _)); // shown by name, never the internal subject id
        var undelivered = new Dictionary<string, object>
        {
            ["@type"] = OutboxMessageTypes.AccountActivation,
            ["@sent"] = (int)OutboxMessageStatus.Sent,
            ["@user"] = $"%{userId}%",
        };
        Assert.Equal(0, await CountAsync(UndeliveredActivations, undelivered));
        Assert.Single(host.Mail.To(email)); // the activation e-mail a run delivered
        await AssertEveryAuditedWriteOfARunIsSystemAsync();
    }

    private static async Task<string> CreateEmailedUserAsync(UsersApi users, NewAccount account, string adminToken)
    {
        using var created = await users.PostJsonAsync(
            UsersApi.Users,
            new
            {
                userName = account.UserName,
                displayName = "Job User",
                email = account.Email,
                kind = UsersApi.ExternalKind,
                binding = UsersApi.PasswordBinding,
                organizationId = account.OrganizationId,
            },
            adminToken);
        var body = await UsersApi.ReadAsync(created, HttpStatusCode.Created);
        Assert.Equal(UsersApi.EmailDelivery, body.GetProperty("activation").GetProperty("delivery").GetInt32());
        return body.GetProperty("user").GetProperty("id").GetString()!;
    }

    /// <summary>
    /// Triggers outbox-drain and waits for its run to finish. A run that met a scheduled tick of the same job (every
    /// 5 minutes on this job-server node) fails as overlapped by design; it is triggered again.
    /// </summary>
    private async Task<JsonElement> RunOutboxDrainAsync(ApiClient api, string adminToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var runId = await TriggerAsync(api, adminToken);
            var run = await WaitForFinishAsync(api, runId, adminToken);
            var overlapped = run.GetProperty("status").GetInt32() == Failed && await LastManualErrorCodeAsync() == JobRunFailureCodes.RunOverlapped;
            if (!overlapped || attempt == MaxTriggerAttempts)
            {
                return run;
            }
        }
    }

    /// <summary>Triggers a run; a 409 (a scheduled run of the same job is queued or running) is retried until the timeout.</summary>
    private static async Task<string> TriggerAsync(ApiClient api, string adminToken)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            using var response = await api.PostAsync(Runs, body: null, adminToken);
            if (response.StatusCode != HttpStatusCode.Conflict || waited.Elapsed > RunTimeout)
            {
                return (await ApiClient.ReadAsync(response, HttpStatusCode.Accepted)).GetProperty("runId").GetString()!;
            }

            await Task.Delay(PollInterval);
        }
    }

    private static async Task<JsonElement> WaitForFinishAsync(ApiClient api, string runId, string adminToken)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            using var response = await api.GetAsync(RunsQuery, adminToken);
            var runs = await ApiClient.ReadAsync(response, HttpStatusCode.OK);
            var run = runs.EnumerateArray().FirstOrDefault(item => item.GetProperty("id").GetString() == runId);
            if (run.ValueKind == JsonValueKind.Object && run.GetProperty("status").GetInt32() is Succeeded or Failed)
            {
                return run;
            }

            Assert.True(waited.Elapsed < RunTimeout, $"Run {runId} did not finish within {RunTimeout}.");
            await Task.Delay(PollInterval);
        }
    }

    /// <summary>
    /// Every succeeded run (the manual one, and any scheduled tick that may have taken the message first) writes its
    /// audit rows as SYSTEM, and at least one run made an audited write, so the check is not vacuous.
    /// </summary>
    private async Task AssertEveryAuditedWriteOfARunIsSystemAsync()
    {
        var correlations = await TestSqlServer.ReadStringsAsync(
            host.DatabaseName,
            SucceededRunCorrelations,
            new Dictionary<string, object> { ["@succeeded"] = Succeeded });
        var audited = 0;
        foreach (var correlation in correlations)
        {
            var parameters = new Dictionary<string, object> { ["@correlation"] = correlation, ["@system"] = SystemActor.Name };
            audited += await CountAsync(AuditRowsOfRun, parameters);
            Assert.Equal(0, await CountAsync(NonSystemAuditRowsOfRun, parameters));
        }

        Assert.True(audited > 0, "No succeeded run made an audited write, so the SYSTEM actor of the audit trail was not exercised.");
    }

    private async Task<string?> LastManualErrorCodeAsync() =>
        await TestSqlServer.ScalarAsync(
            host.DatabaseName,
            LastManualErrorCode,
            new Dictionary<string, object> { ["@key"] = OutboxDrainKey, ["@manual"] = ManualTrigger }) as string;

    private async Task<int> CountAsync(string sql, IReadOnlyDictionary<string, object> parameters) =>
        Convert.ToInt32(await TestSqlServer.ScalarAsync(host.DatabaseName, sql, parameters), CultureInfo.InvariantCulture);

    private sealed record NewAccount(string UserName, string Email, string OrganizationId);
}
