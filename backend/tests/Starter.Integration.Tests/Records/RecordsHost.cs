using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using Codelab.Security.Keys;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Uploads;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Starter.Application.Authorization;
using Starter.Application.Notifications;
using Starter.Application.Records;
using Starter.Application.Users;
using Starter.Infrastructure.Workflow;
using Starter.Integration.Tests.Attachments.Http;
using Starter.Integration.Tests.Support;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Infrastructure.Options;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Records;

/// <summary>
/// The Development host for the generic Records flows on real SQL: its own throwaway database like every
/// <see cref="IntegrationHost"/>, plus the TEST-ONLY record type <see cref="ProbeRecord"/> registered the way a record-type
/// module registers one (registration source, workflow, permissions, a typed draft controller; the attachable access
/// comes from the Records module itself), a required attachment type for it (inserted after start, as a migration of
/// the owning module would), receipts on with a ring generated at run time and the attachment service in memory, and
/// the notifications recorded instead of sent.
/// </summary>
public sealed class RecordsHost() : IntegrationHost(Environments.Development)
{
    private const string FirstItem = "0";
    private const string ServiceName = "starter-api";
    private const string Audience = "starter-attachments";

    private readonly string _clientKeyId = StarterTestAccounts.NewKeyId();
    private readonly string _receiptKeyId = StarterTestAccounts.NewKeyId();
    private readonly SemaphoreSlim _seedLock = new(initialCount: 1, maxCount: 1);
    private bool _seeded;

    /// <summary>The attachment service as the host sees it.</summary>
    public InMemoryAttachmentStorage Storage { get; } = new();

    /// <summary>Every notification the host was asked to send.</summary>
    public RecordedNotifications Notifications { get; } = new();

    /// <summary>A JSON client of the started host.</summary>
    public ApiClient CreateApi() => new(Factory.CreateClient());

    /// <summary>A host service (singletons such as the ticket ring and the receipt signer).</summary>
    public T Service<T>()
        where T : notnull => Factory.Services.GetRequiredService<T>();

    /// <summary>The break-glass administrator's token (every permission: staff and oversight).</summary>
    public async Task<string> StaffTokenAsync()
    {
        using var client = CreateClient();
        return (await client.LoginAsync(Accounts.BreakGlassUserName, Accounts.BreakGlassPassword)).AccessToken;
    }

    /// <summary>The Development user's token, granted the probe type's requester permissions first.</summary>
    public async Task<string> RequesterTokenAsync()
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountAdministration>();
            var page = await accounts.ListAsync(new AccountQuery(Accounts.DevUserName, null, null, null, Page: 1, PageSize: 1), CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IUserPermissionGrants>()
                .EnsureGrantedAsync(Assert.Single(page.Items).UserId, ProbeRecord.RequesterPermissions, CancellationToken.None);
        }

        using var client = CreateClient();
        return (await client.LoginAsync(Accounts.DevUserName, Accounts.DevUserPassword)).AccessToken;
    }

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>
    {
        [StarterHostSettings.Key(AttachmentsOptions.SectionName, nameof(AttachmentsOptions.ServiceBaseUrl))] = AttachmentsHttpHost.ServiceBaseUrl,
        [StarterHostSettings.Key(UploadSafetyOptions.SectionName, nameof(UploadSafetyOptions.ReceiptsEnabled))] = bool.TrueString,
        [StarterHostSettings.Key(UploadSafetyOptions.SectionName, nameof(KeyRingOptions.ActiveKeyId))] = _receiptKeyId,
        [StarterHostSettings.Key(UploadSafetyOptions.SectionName, nameof(KeyRingOptions.Keys), FirstItem, nameof(KeyEntryOptions.KeyId))] = _receiptKeyId,
        [StarterHostSettings.Key(UploadSafetyOptions.SectionName, nameof(KeyRingOptions.Keys), FirstItem, nameof(KeyEntryOptions.MaterialBase64))] = StarterTestAccounts.NewKeyBase64(),
        [StarterHostSettings.Key(ServiceAssertionClientOptions.SectionName, nameof(ServiceAssertionClientOptions.ServiceName))] = ServiceName,
        [StarterHostSettings.Key(ServiceAssertionClientOptions.SectionName, nameof(ServiceAssertionClientOptions.Audience))] = Audience,
        [StarterHostSettings.Key(ServiceAssertionClientOptions.SectionName, nameof(ServiceAssertionClientOptions.KeyId))] = _clientKeyId,
        [StarterHostSettings.Key(ServiceAssertionClientOptions.SectionName, nameof(ServiceAssertionClientOptions.PrivateKeyPem))] = NewClientKeyPem(),
    };

    /// <inheritdoc />
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.RemoveAll<IAttachmentStorageClient>();
        services.AddSingleton<IAttachmentStorageClient>(Storage);
        services.RemoveAll<INotificationService>();
        services.AddSingleton<INotificationService>(Notifications);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, ProbeRecord.PermissionCatalog>());
        services.AddSingleton<IRecordTypeRegistrationSource, ProbeRecord.RegistrationSource>();
        services.AddRecordWorkflow<ProbeRecord.WorkflowDefinition>();
        services.AddControllers().ConfigureApplicationPartManager(parts => parts.ApplicationParts.Add(new ProbeControllerPart()));
    }

    /// <summary>
    /// Seeds the probe type's required attachment type once (what the owning module's migration would insert); every
    /// test calls it first. The Attachments module reads the types per request, so a row added after start counts.
    /// </summary>
    public async Task ReadyAsync()
    {
        await _seedLock.WaitAsync();
        try
        {
            if (!_seeded)
            {
                await TestSqlServer.ExecuteAsync(DatabaseName, ProbeRecord.RequiredDocumentInsert, ProbeRecord.RequiredDocumentValues);
                _seeded = true;
            }
        }
        finally
        {
            _seedLock.Release();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _seedLock.Dispose();
        }

        base.Dispose(disposing);
    }

    private static string NewClientKeyPem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem();
    }

    /// <summary>Exposes only <see cref="ProbeRecordsController"/> from this test assembly to MVC.</summary>
    private sealed class ProbeControllerPart : ApplicationPart, IApplicationPartTypeProvider
    {
        public override string Name => nameof(ProbeRecordsController);

        public IEnumerable<TypeInfo> Types => [typeof(ProbeRecordsController).GetTypeInfo()];
    }
}

/// <summary>Records every user notification (the probe templates are not seeded, so nothing is rendered).</summary>
public sealed class RecordedNotifications : INotificationService
{
    private readonly ConcurrentQueue<UserNotification> _sent = new();

    /// <summary>Every notification so far, in order.</summary>
    public IReadOnlyList<UserNotification> Sent => [.. _sent];

    /// <inheritdoc />
    public Task NotifyUserAsync(UserNotification notification, CancellationToken cancellationToken)
    {
        _sent.Enqueue(notification);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendToAddressAsync(AddressedNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task SendImmediateEmailAsync(UserNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Tests sharing the Records host.</summary>
[CollectionDefinition(Name)]
public sealed class RecordsHostGroup : ICollectionFixture<RecordsHost>
{
    /// <summary>The collection name.</summary>
    public const string Name = "records host";
}
