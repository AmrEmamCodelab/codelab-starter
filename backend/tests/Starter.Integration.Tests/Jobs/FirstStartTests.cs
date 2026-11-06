using Starter.Integration.Tests.Support;
using Starter.Modules.Jobs.Application.BuiltIn;
using Starter.Modules.Jobs.Infrastructure.Scheduling;

namespace Starter.Integration.Tests.Jobs;

/// <summary>
/// Regression: a job-server node whose database does not exist yet (first local run, new fork) used to stop with
/// <c>Invalid object name 'hangfire.Hash'</c>, because the scheduler storage tried to install its schema while the host
/// was built, before the migrations created the database. The fixture starting at all is half the proof.
/// </summary>
[Collection(FreshDatabaseJobServerHostGroup.Name)]
public sealed class FirstStartTests(FreshDatabaseJobServerHost host)
{
    /// <summary>The scheduler's set of recurring job ids.</summary>
    private const string RecurringJobsSetKey = "recurring-jobs";

    private const string SetValues = "SELECT [Value] FROM [" + SchedulerRegistration.SchemaName + "].[Set] WHERE [Key] = @key";

    [Fact]
    public async Task A_job_server_on_a_database_the_host_created_starts_with_the_scheduler_schema_and_its_schedules()
    {
        var scheduled = await TestSqlServer.ReadStringsAsync(
            host.DatabaseName,
            SetValues,
            new Dictionary<string, object> { ["@key"] = RecurringJobsSetKey });

        Assert.Contains(JobsJobKeys.OutboxDrain, scheduled);
    }
}
