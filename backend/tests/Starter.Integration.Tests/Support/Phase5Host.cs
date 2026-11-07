using System.Globalization;
using Codelab.Testing.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog.Core;
using Starter.Application.Messaging;
using Starter.Application.Notifications;
using Starter.Domain.Localization; // codelab:module Portal
using Starter.Infrastructure.Messaging;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Jobs.Infrastructure.Options;
using Starter.Modules.Notifications.Infrastructure.Options;
using Starter.Modules.Portal.Infrastructure.Options; // codelab:module Portal
using Starter.Modules.Users.Infrastructure.Options;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Support;

/// <summary>
/// The Development host for the phase 5 flows (settings, outbox, notifications, portal, activation, lookups, PII):
/// its own throwaway database like every <see cref="IntegrationHost"/>, plus
/// <list type="bullet">
/// <item>a <see cref="ShiftableClock"/> as the host's <see cref="TimeProvider"/> (real time unless a test shifts it);</item>
/// <item>e-mail dropped as <c>.eml</c> files into <see cref="Mail"/> and SMS (enabled) recorded in <see cref="Sms"/>,
/// both under a temp folder this fixture owns and deletes;</item>
/// <item>the contact inbox <see cref="InboxEmail"/>, answered in English;</item>
/// <item>the PII canary log sink (<see cref="LogSink"/>, watching <see cref="Canaries"/>) inside the retained sinks;</item>
/// <item>the recipients directory behind <see cref="Recipients"/> and two probe outbox handlers (<see cref="Probes"/>);</item>
/// <item>no job server (<c>Jobs:Enabled</c> false): tests drain the outbox through <see cref="DrainOutboxAsync"/>.</item>
/// </list>
/// </summary>
public class Phase5Host() : IntegrationHost(Environments.Development)
{
    /// <summary>The web origin the activation links point at.</summary>
    public const string LinkBase = "http://localhost:4280";

    /// <summary>The prefix of the temp folder each fixture creates (and deletes) for its mail and SMS drops.</summary>
    public const string DropFolderPrefix = "starter-it-drops-";

    private const string MailFolderName = "mail";
    private const string SmsFolderName = "sms";
    private const string SmsFileName = "sms.jsonl";
    private const int ShortSuffixLength = 8;

    private readonly string _dropFolder = Path.Combine(Path.GetTempPath(), DropFolderPrefix + Guid.NewGuid().ToString("N"));

    /// <summary>The host clock.</summary>
    public ShiftableClock Clock { get; } = new();

    /// <summary>The planted personal data the PII tests use.</summary>
    public PiiCanaries Canaries { get; } = PiiCanaries.Generate();

    /// <summary>The canary sink inside the host's retained log sinks (sees every event a file or Seq sink would).</summary>
    public CanaryLogSink LogSink => _logSink ??= new CanaryLogSink(Canaries.All);

    /// <summary>The test switch over the recipients directory.</summary>
    public RecipientOverrides Recipients { get; } = new();

    /// <summary>What the probe outbox handlers saw.</summary>
    public ProbeLedger Probes { get; } = new();

    /// <summary>The contact inbox address of this host (unique, reserved domain).</summary>
    public string InboxEmail { get; } = $"inbox-{Guid.NewGuid().ToString("N")[..ShortSuffixLength]}@{PiiCanaries.EmailDomain}";

    /// <summary>The dropped e-mail.</summary>
    public MailDrop Mail => new(Path.Combine(_dropFolder, MailFolderName));

    /// <summary>The recorded SMS.</summary>
    public SmsDrop Sms => new(Path.Combine(_dropFolder, SmsFolderName, SmsFileName));

    /// <summary>True when this host runs the job server.</summary>
    protected virtual bool RunsJobServer => false;

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>
    {
        [StarterHostSettings.Key(JobsOptions.SectionName, nameof(JobsOptions.Enabled))] = RunsJobServer.ToString(),
        [StarterHostSettings.Key(EmailDeliveryOptions.SectionName, nameof(EmailDeliveryOptions.Mode))] = Number((int)EmailDeliveryMode.FileDrop),
        [StarterHostSettings.Key(EmailDeliveryOptions.SectionName, nameof(EmailDeliveryOptions.PickupDirectory))] = Mail.Directory,
        [StarterHostSettings.Key(SmsDeliveryOptions.SectionName, nameof(SmsDeliveryOptions.Enabled))] = bool.TrueString,
        [StarterHostSettings.Key(SmsDeliveryOptions.SectionName, nameof(SmsDeliveryOptions.Mode))] = Number((int)SmsDeliveryMode.File),
        [StarterHostSettings.Key(SmsDeliveryOptions.SectionName, nameof(SmsDeliveryOptions.FilePath))] = Sms.Path,
        [StarterHostSettings.Key(PortalContactOptions.SectionName, nameof(PortalContactOptions.InboxEmail))] = InboxEmail, // codelab:module Portal
        [StarterHostSettings.Key(PortalContactOptions.SectionName, nameof(PortalContactOptions.InboxLanguage))] = Number((int)AppLanguage.English), // codelab:module Portal
    };

    private CanaryLogSink? _logSink;

    /// <summary>A JSON client of the started host.</summary>
    public ApiClient CreateApi() => new(Factory.CreateClient());

    /// <summary>Signs the break-glass administrator in (every permission) and returns the access token.</summary>
    public async Task<string> AdminTokenAsync()
    {
        using var client = CreateClient();
        return (await client.LoginAsync(Accounts.BreakGlassUserName, Accounts.BreakGlassPassword)).AccessToken;
    }

    /// <summary>Runs one outbox drain pass in its own DI scope, as the outbox-drain job does.</summary>
    public async Task<OutboxDrainResult> DrainOutboxAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOutboxDrainRunner>().DrainOnceAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
        services.AddSingleton<ILogEventSink>(LogSink);
        services.AddSingleton(Recipients);
        services.Decorate<INotificationRecipients>((_, inner) => new OverridingRecipients(inner, Recipients));
        services.AddSingleton(Probes);
        services.AddOutboxHandler<IdempotentProbeHandler>();
        services.AddOutboxHandler<FailingProbeHandler>();
        services.PostConfigure<ActivationOptions>(options => options.LinkBaseUrl = LinkBase);
    }

    /// <inheritdoc />
    protected override void AfterStop()
    {
        if (Directory.Exists(_dropFolder))
        {
            Directory.Delete(_dropFolder, recursive: true);
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The phase 5 host with the job server on (Hangfire on the test database, schema <c>hangfire</c>).</summary>
public sealed class JobServerHost : Phase5Host
{
    /// <inheritdoc />
    protected override bool RunsJobServer => true;
}

/// <summary>
/// The job server host on a database that does not exist yet: the host's first migration creates it, so the scheduler
/// storage is resolved before its database exists (the first run of a new fork).
/// </summary>
public sealed class FreshDatabaseJobServerHost : Phase5Host
{
    /// <inheritdoc />
    protected override bool RunsJobServer => true;

    /// <inheritdoc />
    protected override bool CreatesDatabaseBeforeStart => false;

    /// <summary>
    /// The phase 5 settings with the connection pool's blocking period off: EF's existence probe of the missing
    /// database fails one open, and SqlClient would replay that failure to the next open on the same pool for up to
    /// five seconds (the security-store schema step), which is a test-timing artefact, not what this host proves.
    /// </summary>
    protected override IReadOnlyDictionary<string, string?> ExtraSettings
    {
        get
        {
            var settings = new Dictionary<string, string?>(base.ExtraSettings, StringComparer.OrdinalIgnoreCase)
            {
                [StarterHostSettings.Key(ConnectionStringsOptions.SectionName, nameof(ConnectionStringsOptions.Starter))] =
                    new SqlConnectionStringBuilder(TestSqlServer.ConnectionStringFor(DatabaseName)) { PoolBlockingPeriod = PoolBlockingPeriod.NeverBlock }.ConnectionString,
            };
            return settings;
        }
    }
}

/// <summary>Tests sharing the job server host on a database the host creates.</summary>
[CollectionDefinition(Name)]
public sealed class FreshDatabaseJobServerHostGroup : ICollectionFixture<FreshDatabaseJobServerHost>
{
    /// <summary>The collection name.</summary>
    public const string Name = "job server host on a fresh database";
}

/// <summary>Tests sharing the phase 5 host.</summary>
[CollectionDefinition(Name)]
public sealed class Phase5HostGroup : ICollectionFixture<Phase5Host>
{
    /// <summary>The collection name.</summary>
    public const string Name = "phase 5 host";
}

/// <summary>Tests sharing the job server host.</summary>
[CollectionDefinition(Name)]
public sealed class JobServerHostGroup : ICollectionFixture<JobServerHost>
{
    /// <summary>The collection name.</summary>
    public const string Name = "job server host";
}
