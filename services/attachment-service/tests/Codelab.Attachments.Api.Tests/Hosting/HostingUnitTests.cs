using Codelab.Attachments.Api.Authentication;
using Codelab.Attachments.Api.Hosting;
using Codelab.Attachments.Core.Storage;
using Codelab.Security;
using Codelab.Security.Cors;
using Codelab.Security.PublicIds;
using Codelab.Security.Tickets;
using Codelab.Security.Uploads;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Codelab.Attachments.Api.Tests.Hosting;

public sealed class HostingUnitTests
{
    [Theory]
    [InlineData(null, AttachmentsHostSettings.DefaultServiceName)]
    [InlineData("  ", AttachmentsHostSettings.DefaultServiceName)]
    [InlineData(" permits-attachments ", "permits-attachments")]
    public void The_service_name_comes_from_configuration(string? configured, string expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AttachmentsHostSettings.ServiceNameKey] = configured })
            .Build();

        Assert.Equal(expected, AttachmentsHostSettings.ServiceNameFrom(configuration));
    }

    [Fact]
    public void The_ticket_header_is_built_from_the_protocol_prefix()
    {
        Assert.Equal("X-ALTPFX-Upload-Ticket", UploadTickets.HeaderName(SecurityProtocol.Create("ALTPFX")));
    }

    [Fact]
    public void The_ephemeral_ring_is_a_valid_and_fresh_public_id_ring()
    {
        var first = EphemeralPublicIdRing.CreateSettings();
        var second = EphemeralPublicIdRing.CreateSettings();
        var options = new PublicIdKeyringOptions();
        new ConfigurationBuilder().AddInMemoryCollection(first).Build().GetSection(PublicIdKeyringOptions.SectionName).Bind(options);

        Assert.True(PublicIdKeyring.Validate(options).IsValid);
        Assert.Equal(EphemeralPublicIdRing.KeyId, options.ActiveKeyId);
        Assert.NotEqual(first.Values.Last(), second.Values.Last());
    }

    [Fact]
    public void A_configured_public_id_ring_is_refused_and_an_absent_one_is_replaced()
    {
        var configured = WebApplication.CreateBuilder();
        configured.Configuration[PublicIdKeyringOptions.SectionName + ":ActiveKeyId"] = "api";
        var empty = WebApplication.CreateBuilder();

        Assert.Throws<InvalidOperationException>(() => configured.AddEphemeralPublicIdRing());
        empty.AddEphemeralPublicIdRing();
        Assert.Equal(EphemeralPublicIdRing.KeyId, empty.Configuration[PublicIdKeyringOptions.SectionName + ":ActiveKeyId"]);
    }

    [Fact]
    public void The_local_secrets_file_is_named_after_the_environment()
    {
        Assert.Equal("appsettings.Development.local.json", LocalSecretsFile.FileNameFor(Environments.Development));
    }

    [Fact]
    public void The_required_features_validator_refuses_disabled_tickets_receipts_and_a_global_cors_policy()
    {
        var validator = new RequiredSecurityFeaturesValidator();
        var globalCors = new CodelabCorsOptions();
        globalCors.AllowedOrigins.Add("https://web.example.test");

        Assert.True(validator.Validate(name: null, new SealedTicketOptions { Enabled = false }).Failed);
        Assert.True(validator.Validate(name: null, new SealedTicketOptions { Enabled = true }).Succeeded);
        Assert.True(validator.Validate(name: null, new UploadSafetyOptions { ReceiptsEnabled = false }).Failed);
        Assert.True(validator.Validate(name: null, new UploadSafetyOptions { ReceiptsEnabled = true }).Succeeded);
        Assert.True(validator.Validate(name: null, globalCors).Failed);
        Assert.True(validator.Validate(name: null, new CodelabCorsOptions()).Succeeded);
    }

    [Theory]
    [InlineData("Development", "http://localhost:4280", true)]
    [InlineData("Production", "http://web.example.test", false)]
    [InlineData("Production", "https://web.example.test", true)]
    [InlineData("Production", "*", false)]
    [InlineData("Production", "https://web.example.test/path", false)]
    public void Upload_origins_follow_the_codelab_origin_rules(string environment, string origin, bool valid)
    {
        var validator = new UploadCorsOptionsValidator(new TestEnvironment(environment));
        var options = new UploadCorsOptions();
        options.AllowedOrigins.Add(origin);

        var result = validator.Validate(name: null, options);

        Assert.Equal(valid, result.Succeeded);
        if (!valid)
        {
            Assert.Contains(UploadCorsOptions.SectionName, result.FailureMessage, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_negative_preflight_age_is_refused()
    {
        var validator = new UploadCorsOptionsValidator(new TestEnvironment(Environments.Production));

        Assert.True(validator.Validate(name: null, new UploadCorsOptions { PreflightMaxAgeSeconds = -1 }).Failed);
    }

    [Fact]
    public void The_upload_policy_allows_post_with_the_ticket_header_and_no_credentials()
    {
        var protocol = SecurityProtocol.Create(SecurityProtocol.TestPrefix);
        var options = new UploadCorsOptions();
        options.AllowedOrigins.Add("https://web.example.test");

        var policy = UploadCors.Build(options, protocol);

        Assert.Equal(["https://web.example.test"], policy.Origins);
        Assert.Equal(["POST"], policy.Methods);
        Assert.Contains(UploadTickets.HeaderName(protocol), policy.Headers);
        Assert.False(policy.SupportsCredentials);
    }

    [Fact]
    public async Task Readiness_follows_the_storage_root()
    {
        var root = Directory.CreateTempSubdirectory("codelab-attachments-ready-").FullName;
        try
        {
            var present = new StorageReadinessCheck(Options.Create(new AttachmentStorageOptions { RootPath = root }));
            var missing = new StorageReadinessCheck(Options.Create(new AttachmentStorageOptions { RootPath = Path.Combine(root, "absent") }));
            var unset = new StorageReadinessCheck(Options.Create(new AttachmentStorageOptions()));

            Assert.Equal(HealthStatus.Healthy, (await present.CheckHealthAsync(new HealthCheckContext())).Status);
            var unhealthy = await missing.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Unhealthy, unhealthy.Status);
            Assert.DoesNotContain(root, unhealthy.Description, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(HealthStatus.Unhealthy, (await unset.CheckHealthAsync(new HealthCheckContext())).Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
