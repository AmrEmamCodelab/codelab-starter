using System.Collections.Concurrent;
using System.Security.Cryptography;
using Codelab.Security.Keys;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Uploads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Starter.Application.Attachments;
using Starter.Application.Messaging;
using Starter.Application.Results;
using Starter.Integration.Tests.Support;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Infrastructure.Options;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Attachments.Http;

/// <summary>
/// The Development host for the attachments HTTP flows: its own throwaway database like every
/// <see cref="IntegrationHost"/>, receipts on with a receipt ring generated at run time (the same ring the attachment
/// service would hold), a service-assertion client key generated at run time, and an in-memory
/// <see cref="IAttachmentStorageClient"/> (<see cref="Storage"/>) in place of the HTTP client, so no attachment service
/// runs. The test plays the service: it opens the issued ticket and signs the receipt with the host's own services.
/// </summary>
public sealed class AttachmentsHttpHost() : IntegrationHost(Environments.Development)
{
    /// <summary>The attachment service root the host is configured with (never called: storage is in memory).</summary>
    public const string ServiceBaseUrl = "http://localhost:5185";

    private const string ServiceName = "starter-api";
    private const string Audience = "starter-attachments";
    private const string FirstItem = "0";

    private readonly string _clientKeyId = StarterTestAccounts.NewKeyId();
    private readonly string _receiptKeyId = StarterTestAccounts.NewKeyId();

    /// <summary>The attachment service as the host sees it: the files it holds and the deletions it was asked for.</summary>
    public InMemoryAttachmentStorage Storage { get; } = new();

    /// <summary>A JSON client of the started host.</summary>
    public ApiClient CreateApi() => new(Factory.CreateClient());

    /// <summary>Signs the break-glass administrator in (every permission) and returns the access token.</summary>
    public async Task<string> AdminTokenAsync()
    {
        using var client = CreateClient();
        return (await client.LoginAsync(Accounts.BreakGlassUserName, Accounts.BreakGlassPassword)).AccessToken;
    }

    /// <summary>Signs the Development user in (no attachments permission) and returns the access token.</summary>
    public async Task<string> DevUserTokenAsync()
    {
        using var client = CreateClient();
        return (await client.LoginAsync(Accounts.DevUserName, Accounts.DevUserPassword)).AccessToken;
    }

    /// <summary>A host service (singletons such as the ticket service and the receipt signer).</summary>
    public T Service<T>()
        where T : notnull => Factory.Services.GetRequiredService<T>();

    /// <summary>
    /// What an owning module's submit would answer: the <see cref="RequiredAttachmentsGate"/> over the host's real
    /// <see cref="IRequiredAttachments"/> (real SQL), in its own DI scope as a request would run it.
    /// </summary>
    public async Task<Refusal?> SubmitRefusalAsync(string recordType, long recordId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IRequiredAttachments>()
            .RefuseWhileMissingAsync(recordType, recordId, CancellationToken.None);
    }

    /// <summary>Runs one outbox drain pass in its own DI scope, as the outbox-drain job does.</summary>
    public async Task<OutboxDrainResult> DrainOutboxAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOutboxDrainRunner>().DrainOnceAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>
    {
        [StarterHostSettings.Key(AttachmentsOptions.SectionName, nameof(AttachmentsOptions.ServiceBaseUrl))] = ServiceBaseUrl,
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
    }

    private static string NewClientKeyPem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem();
    }
}

/// <summary>An in-memory attachment service: the bytes per file id, and every deletion it was asked for.</summary>
public sealed class InMemoryAttachmentStorage : IAttachmentStorageClient
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _deleted = new();

    /// <summary>The file ids deleted so far, in order.</summary>
    public IReadOnlyList<string> Deleted => [.. _deleted];

    /// <summary>Stores <paramref name="bytes"/> as <paramref name="fileId"/> (what an upload would have done).</summary>
    public void Put(string fileId, byte[] bytes) => _files[fileId] = bytes;

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(string fileId, CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(_files.TryGetValue(fileId, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string fileId, CancellationToken cancellationToken)
    {
        _deleted.Enqueue(fileId);
        return Task.FromResult(_files.TryRemove(fileId, out _));
    }
}

/// <summary>Tests sharing the attachments host.</summary>
[CollectionDefinition(Name)]
public sealed class AttachmentsHttpHostGroup : ICollectionFixture<AttachmentsHttpHost>
{
    /// <summary>The collection name.</summary>
    public const string Name = "attachments http host";
}
