using System.Security.Cryptography;
using Codelab.Observability;
using Codelab.Security;
using Codelab.Security.Keys;
using Codelab.Security.PublicIds;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Signing;
using Codelab.Security.Storage;
using Codelab.Security.Tickets;
using Codelab.Security.Uploads;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Identity.Infrastructure.Options;

namespace Starter.Tests.Hosting;

/// <summary>How a test host is configured.</summary>
/// <param name="ConnectionString">The database the host uses (never opened when <paramref name="UsesDatabase"/> is false).</param>
/// <param name="UsesDatabase">True when the host may touch SQL (startup tasks, security store schema).</param>
/// <param name="ApplyMigrationsOnStartup">The kernel's migrate-on-start switch (valid only in Development).</param>
/// <param name="SigningMode">The request and response signing mode.</param>
public sealed record StarterHostOptions(string ConnectionString, bool UsesDatabase, bool ApplyMigrationsOnStartup, SecurityEnforcementMode SigningMode);

/// <summary>
/// Builds the configuration of a test host from the options classes' own key names, so a renamed key breaks the
/// build, not the test at run time. Every secret comes from <see cref="StarterTestAccounts"/>.
/// </summary>
public static class StarterHostSettings
{
    private const string Separator = ":";
    private const string FirstItem = "0";

    // The jobs module's job-server switch as a plain key, so the support code does not reference the module.
    private const string JobsEnabledKey = "Jobs:Enabled";

    // The Attachments module's service root as a plain key, so the support code does not reference the module; a reserved name that never resolves.
    private const string AttachmentsServiceBaseUrlKey = "Attachments:ServiceBaseUrl";
    private const string UnusedAttachmentServiceRoot = "https://attachments.invalid";
    private const string AttachmentsClientServiceName = "starter-api";
    private const string AttachmentsAudience = "starter-attachments";

    // The Attachments module's upload-ticket ring as a plain section: its own keys, never the API's ticket ring.
    private const string UploadTicketsSection = SecuritySections.Root + ":UploadTickets";
    private const string UploadTicketsEnabledKey = UploadTicketsSection + ":Enabled";

    /// <summary>The settings for <paramref name="options"/> and <paramref name="accounts"/>.</summary>
    public static IReadOnlyDictionary<string, string?> For(StarterHostOptions options, StarterTestAccounts accounts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(accounts);
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [Key(ConnectionStringsOptions.SectionName, nameof(ConnectionStringsOptions.Starter))] = options.ConnectionString,
            [Key(DatabaseOptions.SectionName, nameof(DatabaseOptions.ApplyMigrationsOnStartup))] = Flag(options.ApplyMigrationsOnStartup),
            [Key(SqlStoreOptions.SectionName, nameof(SqlStoreOptions.EnsureSchemaOnStart))] = Flag(options.UsesDatabase),
            [Key(RequestSigningOptions.SectionName, nameof(RequestSigningOptions.Mode))] = options.SigningMode.ToString(),
            [Key(ResponseSigningOptions.SectionName, nameof(ResponseSigningOptions.Mode))] = options.SigningMode.ToString(),
            [Key(BreakGlassOptions.SectionName, nameof(BreakGlassOptions.UserName))] = accounts.BreakGlassUserName,
            [Key(BreakGlassOptions.SectionName, nameof(BreakGlassOptions.Password))] = accounts.BreakGlassPassword,
            [Key(DevUsersOptions.SectionName, FirstItem, nameof(DevUserOptions.UserName))] = accounts.DevUserName,
            [Key(DevUsersOptions.SectionName, FirstItem, nameof(DevUserOptions.Password))] = accounts.DevUserPassword,
            [Key(JwtOptions.SectionName, nameof(JwtOptions.SigningKey))] = StarterTestAccounts.NewKeyBase64(),
            [UserIdHashSettings.KeyName] = StarterTestAccounts.NewKeyBase64(),
            [Key(LogFileSettings.KeyPrefix, nameof(LogFileSettings.Enabled))] = Flag(false),
            [Key(SeqSettings.KeyPrefix, nameof(SeqSettings.ServerUrl))] = string.Empty,
            // Sealed tickets on, so account activation links can be issued like in a real deployment.
            [Key(SealedTicketOptions.SectionName, nameof(SealedTicketOptions.Enabled))] = Flag(true),
            // No background job server: it would open SQL on start and run jobs against a throwaway database.
            [JobsEnabledKey] = Flag(false),
            // Attachments (phase 6): the service root, the ES256 client key of the service assertion and the upload receipt ring, all generated at run time.
            [AttachmentsServiceBaseUrlKey] = UnusedAttachmentServiceRoot,
            [Key(ServiceAssertionClientOptions.SectionName, nameof(ServiceAssertionClientOptions.ServiceName))] = AttachmentsClientServiceName,
            [Key(ServiceAssertionClientOptions.SectionName, nameof(ServiceAssertionClientOptions.Audience))] = AttachmentsAudience,
            [Key(ServiceAssertionClientOptions.SectionName, nameof(ServiceAssertionClientOptions.KeyId))] = StarterTestAccounts.NewKeyId(),
            [Key(ServiceAssertionClientOptions.SectionName, nameof(ServiceAssertionClientOptions.PrivateKeyPem))] = NewServiceAssertionKeyPem(),
            [Key(UploadSafetyOptions.SectionName, nameof(UploadSafetyOptions.ReceiptsEnabled))] = Flag(true),
            [UploadTicketsEnabledKey] = Flag(true),
        };
        AddKeyRing(settings, PublicIdKeyringOptions.SectionName);
        AddKeyRing(settings, RequestSigningOptions.SectionName);
        AddKeyRing(settings, SealedTicketOptions.SectionName);
        AddKeyRing(settings, UploadSafetyOptions.SectionName);
        AddKeyRing(settings, UploadTicketsSection);
        return settings;
    }

    /// <summary>A configuration key from its segments.</summary>
    public static string Key(params string[] segments) => string.Join(Separator, segments);

    private static void AddKeyRing(Dictionary<string, string?> settings, string section)
    {
        var keyId = StarterTestAccounts.NewKeyId();
        settings[Key(section, nameof(KeyRingOptions.ActiveKeyId))] = keyId;
        settings[Key(section, nameof(KeyRingOptions.Keys), FirstItem, nameof(KeyEntryOptions.KeyId))] = keyId;
        settings[Key(section, nameof(KeyRingOptions.Keys), FirstItem, nameof(KeyEntryOptions.MaterialBase64))] = StarterTestAccounts.NewKeyBase64();
    }

    private static string NewServiceAssertionKeyPem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem();
    }

    private static string Flag(bool value) => value ? bool.TrueString : bool.FalseString;
}
