using System.Net;
using System.Security.Cryptography;
using Codelab.Security.Keys;
using Codelab.Security.Replay;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Tickets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Starter.Application.Attachments;
using Starter.Application.Authorization;
using Starter.Application.Messaging;
using Starter.Domain.Attachments;
using Starter.Infrastructure.Startup;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Infrastructure;
using Starter.Modules.Attachments.Infrastructure.Options;
using Starter.Modules.Attachments.Infrastructure.Security;
using Starter.Modules.Attachments.Infrastructure.Startup;
using Starter.Modules.Attachments.Infrastructure.Storage;
using Starter.Modules.Attachments.Tests.Fakes;
using static Starter.Modules.Attachments.Tests.Infrastructure.InfrastructureFixtures;

namespace Starter.Modules.Attachments.Tests.Infrastructure;

public sealed class RegistrationTests
{
    private const string UploadKeyId = "u1k";
    private const int ReplayEntryLimit = 100;
    private const string SourcedRecordType = "sourced-files";

    [Fact]
    public void Development_registers_the_demo_record_type()
    {
        using var provider = Provider(Environments.Development, StubHandler.Status(HttpStatusCode.OK));
        using var scope = provider.CreateScope();

        Assert.True(scope.ServiceProvider.GetRequiredService<AttachableRecords>().IsKnown(AttachableRecordTypes.Demo));
    }

    [Fact]
    public void Production_does_not_know_the_demo_record_type()
    {
        using var provider = Provider(Environments.Production, StubHandler.Status(HttpStatusCode.OK));
        using var scope = provider.CreateScope();

        Assert.False(scope.ServiceProvider.GetRequiredService<AttachableRecords>().IsKnown(AttachableRecordTypes.Demo));
    }

    [Fact]
    public void The_gate_knows_every_access_an_access_source_serves()
    {
        using var provider = Provider(
            Environments.Production,
            StubHandler.Status(HttpStatusCode.OK),
            services => services.AddScoped<IAttachableRecordAccessSource>(_ => new SourceOf(SourcedRecordType)));
        using var scope = provider.CreateScope();

        Assert.True(scope.ServiceProvider.GetRequiredService<AttachableRecords>().IsKnown(SourcedRecordType));
    }

    [Fact]
    public void An_access_served_twice_is_a_wiring_fault()
    {
        using var provider = Provider(
            Environments.Production,
            StubHandler.Status(HttpStatusCode.OK),
            services =>
            {
                services.AddScoped<IAttachableRecordAccessSource>(_ => new SourceOf(SourcedRecordType));
                services.AddScoped<IAttachableRecordAccess>(_ => new SourceOf(SourcedRecordType).Accesses.Single());
            });
        using var scope = provider.CreateScope();

        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<AttachableRecords>());
    }

    [Fact]
    public async Task The_storage_client_calls_the_service_root_with_a_service_assertion()
    {
        var handler = StubHandler.Status(HttpStatusCode.NoContent);
        using var provider = Provider(Environments.Development, handler);
        using var scope = provider.CreateScope();

        var deleted = await scope.ServiceProvider.GetRequiredService<IAttachmentStorageClient>().DeleteAsync(FileId, CancellationToken.None);

        Assert.True(deleted);
        var request = Assert.Single(handler.Requests);
        Assert.Equal($"{ServiceRoot}/api/files/{FileId}", request.RequestUri!.AbsoluteUri);
        Assert.True(request.Headers.Contains(Protocol.ServiceAssertionHeaderName));
    }

    [Fact]
    public void The_module_registers_its_ports_handler_and_startup_check()
    {
        var services = new ServiceCollection();

        services.AddAttachmentsModule(Configuration());

        AssertRegistered<IAttachmentStore>(services);
        AssertRegistered<IAttachmentFileCleanup>(services);
        AssertRegistered<IRequiredAttachments>(services);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IOutboxHandler) && descriptor.ImplementationType == typeof(DeleteAttachmentFileHandler));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IStartupTask) && descriptor.ImplementationType == typeof(AttachmentCatalogStartupCheck));
        var tasks = services.Where(descriptor => descriptor.ServiceType == typeof(IStartupTask)).Select(descriptor => descriptor.ImplementationType).ToList();
        Assert.True(tasks.IndexOf(typeof(AttachmentTypeSeedStartupTask)) is >= 0 and var seeding && seeding < tasks.IndexOf(typeof(AttachmentCatalogStartupCheck)));
    }

    [Fact]
    public void Without_a_declaring_module_no_type_is_declared()
    {
        using var provider = Provider(Environments.Production, StubHandler.Status(HttpStatusCode.OK));

        Assert.Empty(provider.GetRequiredService<AttachmentTypeDeclarations>().Seeds);
    }

    [Fact]
    public void The_types_a_module_declares_reach_the_synchronizer()
    {
        using var provider = Provider(
            Environments.Production,
            StubHandler.Status(HttpStatusCode.OK),
            services => services.AddSingleton<IAttachmentTypeSeedSource>(new DeclaringSource()));

        var seed = Assert.Single(provider.GetRequiredService<AttachmentTypeDeclarations>().Seeds);

        Assert.Equal(DeclaringSource.Definition.Key, seed.Key);
    }

    [Fact]
    public void A_module_that_declares_the_demo_type_stops_the_host()
    {
        using var provider = Provider(
            Environments.Production,
            StubHandler.Status(HttpStatusCode.OK),
            services => services.AddSingleton<IAttachmentTypeSeedSource>(new DeclaringSource(AttachmentTypeIds.DemoDocument)));

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<AttachmentTypeDeclarations>());
    }

    [Fact]
    public void The_receipt_verifier_and_the_ticket_issuer_resolve()
    {
        using var provider = Provider(Environments.Development, StubHandler.Status(HttpStatusCode.OK));

        Assert.IsType<ReceiptVerifier>(provider.GetRequiredService<IAttachmentReceiptVerifier>());
        Assert.Same(provider.GetRequiredService<ReceiptVerifier>(), provider.GetRequiredService<IAttachmentReceiptVerifier>());
        Assert.IsType<UploadTicketIssuer>(provider.GetRequiredService<IUploadTicketIssuer>());
    }

    [Fact]
    public void The_module_seals_upload_tickets_with_its_own_ring_and_never_registers_the_api_ticket_service()
    {
        var services = new ServiceCollection();

        services.AddAttachmentsModule(Configuration());

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ISealedTicketService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(UploadTicketRing));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IValidateOptions<UploadTicketRingOptions>)
            && descriptor.ImplementationType == typeof(UploadTicketRingOptionsValidator));
    }

    [Fact]
    public void The_upload_ticket_ring_binds_from_its_own_section()
    {
        using var provider = Provider(Environments.Development, StubHandler.Status(HttpStatusCode.OK));

        var ring = provider.GetRequiredService<IOptions<UploadTicketRingOptions>>().Value;

        Assert.Equal("Codelab:Security:UploadTickets", UploadTicketRingOptions.SectionName);
        Assert.True(ring.Enabled);
        Assert.Equal(UploadKeyId, ring.ActiveKeyId);
        Assert.NotNull(provider.GetRequiredService<UploadTicketRing>());
    }

    private static void AssertRegistered<TService>(IServiceCollection services) =>
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TService));

    private static ServiceProvider Provider(string environment, StubHandler handler, Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environment));
        services.AddSingleton(Protocol);
        services.AddSingleton<IOrganizationScope>(new FakeOrganizationScope());
        services.AddSingleton<IReplayGuard>(new MemoryReplayGuard(TimeProvider.System, ReplayEntryLimit));
        services.AddSingleton(new KeyRingGuard(PublishedMaterialDenylist.Embedded, checkPublished: true));
        services.AddAttachmentsModule(Configuration());
        services.AddHttpClient<IAttachmentStorageClient, HttpAttachmentStorageClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        extra?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>A module that declares one attachment type (id 90 unless given).</summary>
    private sealed class DeclaringSource(int id = DeclaringSource.DefaultId) : IAttachmentTypeSeedSource
    {
        private const int DefaultId = 90;

        public static AttachmentTypeDefinition Definition { get; } = new(
            Id: DefaultId, Key: "sourced-file", RecordType: SourcedRecordType, NameAr: "ملف", NameEn: "File",
            IsRequired: false, MaxFiles: 2, AllowedExtensions: [".pdf"]);

        public IEnumerable<AttachmentTypeDefinition> Types => [Definition with { Id = id }];
    }

    /// <summary>A source serving one access that allows nothing, as the Records module serves one per record type.</summary>
    private sealed class SourceOf(string recordType) : IAttachableRecordAccessSource
    {
        public IEnumerable<IAttachableRecordAccess> Accesses { get; } = [new NoAccess(recordType)];

        private sealed class NoAccess(string recordType) : IAttachableRecordAccess
        {
            public string RecordType => recordType;

            public Task<bool> CanViewAsync(long recordId, CancellationToken cancellationToken) => Task.FromResult(false);

            public Task<bool> CanChangeAsync(long recordId, CancellationToken cancellationToken) => Task.FromResult(false);
        }
    }

    private static IConfiguration Configuration()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Attachments:ServiceBaseUrl"] = ServiceRoot,
            [$"{ServiceAssertionClientOptions.SectionName}:{nameof(ServiceAssertionClientOptions.ServiceName)}"] = "starter-api",
            [$"{ServiceAssertionClientOptions.SectionName}:{nameof(ServiceAssertionClientOptions.Audience)}"] = "starter-attachments",
            [$"{ServiceAssertionClientOptions.SectionName}:{nameof(ServiceAssertionClientOptions.KeyId)}"] = "s2s-test",
            [$"{ServiceAssertionClientOptions.SectionName}:{nameof(ServiceAssertionClientOptions.PrivateKeyPem)}"] = key.ExportPkcs8PrivateKeyPem(),
            [$"{UploadTicketRingOptions.SectionName}:{nameof(UploadTicketRingOptions.Enabled)}"] = bool.TrueString,
            [$"{UploadTicketRingOptions.SectionName}:{nameof(UploadTicketRingOptions.ActiveKeyId)}"] = UploadKeyId,
            [$"{UploadTicketRingOptions.SectionName}:{nameof(UploadTicketRingOptions.Keys)}:0:KeyId"] = UploadKeyId,
            [$"{UploadTicketRingOptions.SectionName}:{nameof(UploadTicketRingOptions.Keys)}:0:MaterialBase64"] = NewKey(UploadKeyId).MaterialBase64,
        }).Build();
    }
}
