using Codelab.Security.Keys;
using Codelab.Security.PublicIds;
using Codelab.Security.Replay;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Sessions;
using Codelab.Security.Signing;
using Codelab.Security.Storage;
using Codelab.Security.Tickets;
using Codelab.Security.Uploads;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Codelab.Security.Hosting;

/// <summary>Turns a key-material verdict into an options verdict.</summary>
internal static class Verdicts
{
    internal static ValidateOptionsResult From(KeyMaterialResult result) =>
        result.IsValid ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(result.Error!);
}

/// <summary>The PublicId ring: structure, key-id grammar, not published.</summary>
internal sealed class PublicIdKeyringOptionsValidator(KeyRingGuard guard) : IValidateOptions<PublicIdKeyringOptions>
{
    public ValidateOptionsResult Validate(string? name, PublicIdKeyringOptions options)
    {
        var grammar = PublicIdKeyring.Validate(options);
        return Verdicts.From(grammar.IsValid ? guard.Check(options, PublicIdKeyringOptions.SectionName) : grammar);
    }
}

/// <summary>Request signing: nothing while Off; otherwise audience, age, ring, not published.</summary>
internal sealed class RequestSigningOptionsValidator(KeyRingGuard guard) : IValidateOptions<RequestSigningOptions>
{
    public ValidateOptionsResult Validate(string? name, RequestSigningOptions options)
    {
        if (options.Mode is SecurityEnforcementMode.Off)
        {
            return ValidateOptionsResult.Success;
        }

        var structure = options.ValidateStructure();
        return Verdicts.From(structure.IsValid ? guard.CheckNotPublished(options.Keys, RequestSigningOptions.SectionName) : structure);
    }
}

/// <summary>Response signing needs request signing (the response key is the request's session key).</summary>
internal sealed class ResponseSigningOptionsValidator(IOptionsMonitor<RequestSigningOptions> requestSigning) : IValidateOptions<ResponseSigningOptions>
{
    public ValidateOptionsResult Validate(string? name, ResponseSigningOptions options) =>
        options.Mode is not SecurityEnforcementMode.Off && requestSigning.CurrentValue.Mode is SecurityEnforcementMode.Off
            ? ValidateOptionsResult.Fail($"{ResponseSigningOptions.SectionName}:Mode cannot be on while {RequestSigningOptions.SectionName}:Mode is Off.")
            : ValidateOptionsResult.Success;
}

/// <summary>
/// Replay: positive limits; outside Development, the per-process memory store needs an acknowledgement whenever the
/// replay guard is in use: request signing in Enforce, the service-assertion server scheme (jti), or sealed tickets
/// (one-time open). A second replica would accept a nonce, jti or ticket the first one already consumed.
/// </summary>
internal sealed class ReplayProtectionOptionsValidator(
    IOptionsMonitor<RequestSigningOptions> requestSigning,
    IOptionsMonitor<SealedTicketOptions> tickets,
    IServiceProvider services,
    IHostEnvironment environment)
    : IValidateOptions<ReplayProtectionOptions>
{
    public ValidateOptionsResult Validate(string? name, ReplayProtectionOptions options)
    {
        if (options.RetentionMarginSeconds < 0 || options.MemoryEntryLimit <= 0)
        {
            return ValidateOptionsResult.Fail($"{ReplayProtectionOptions.SectionName}: RetentionMarginSeconds must not be negative and MemoryEntryLimit must be positive.");
        }

        var users = ReplayGuardUsers();
        var unacknowledged = users.Count > 0 && options.Store is SecurityStoreKind.Memory && !environment.IsDevelopment() && !options.AcknowledgeSingleInstance;
        return unacknowledged
            ? ValidateOptionsResult.Fail($"{string.Join(", ", users)} use the per-process memory replay store. Set {ReplayProtectionOptions.SectionName}:Store to SqlServer, or {ReplayProtectionOptions.SectionName}:{nameof(ReplayProtectionOptions.AcknowledgeSingleInstance)} = true for a single-instance deployment.")
            : ValidateOptionsResult.Success;
    }

    private List<string> ReplayGuardUsers()
    {
        var users = new List<string>();
        if (requestSigning.CurrentValue.Mode is SecurityEnforcementMode.Enforce)
        {
            users.Add($"request signing in Enforce ({RequestSigningOptions.SectionName})");
        }

        if (services.GetService<ServiceAssertionServerMarker>() is not null)
        {
            users.Add($"the service-assertion server ({ServiceAssertionServerOptions.SectionName})");
        }

        if (tickets.CurrentValue.Enabled)
        {
            users.Add($"sealed tickets ({SealedTicketOptions.SectionName})");
        }

        return users;
    }
}

/// <summary>The SQL connection is required when any store uses SQL Server.</summary>
internal sealed class SqlStoreOptionsValidator(IOptionsMonitor<ReplayProtectionOptions> replay, IOptionsMonitor<SecuritySessionOptions> sessions)
    : IValidateOptions<SqlStoreOptions>
{
    public ValidateOptionsResult Validate(string? name, SqlStoreOptions options)
    {
        var sqlInUse = replay.CurrentValue.Store is SecurityStoreKind.SqlServer || sessions.CurrentValue.Store is SecurityStoreKind.SqlServer;
        if (sqlInUse && string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail($"{SqlStoreOptions.SectionName}:ConnectionString is required while a security store is SqlServer.");
        }

        return options.CommandTimeoutSeconds > 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail($"{SqlStoreOptions.SectionName}:CommandTimeoutSeconds must be positive.");
    }
}

/// <summary>
/// Sessions: the claim types must be named; outside Development, checking revocation against the per-process memory
/// store needs the single-instance acknowledgement (a sid revoked on one replica would still pass on another).
/// </summary>
internal sealed class SessionOptionsValidator(IOptionsMonitor<ReplayProtectionOptions> replay, IHostEnvironment environment)
    : IValidateOptions<SecuritySessionOptions>
{
    public ValidateOptionsResult Validate(string? name, SecuritySessionOptions options)
    {
        if (options.UserIdClaimTypes.Count == 0 || string.IsNullOrWhiteSpace(options.SessionIdClaimType) || string.IsNullOrWhiteSpace(options.SessionKeyIdClaimType) || string.IsNullOrWhiteSpace(options.SessionVersionClaimType))
        {
            return ValidateOptionsResult.Fail($"{SecuritySessionOptions.SectionName}: every claim type must be named.");
        }

        var unacknowledged = options.ValidateOnEveryRequest && options.Store is SecurityStoreKind.Memory
            && !environment.IsDevelopment() && !replay.CurrentValue.AcknowledgeSingleInstance;
        return unacknowledged
            ? ValidateOptionsResult.Fail($"{SecuritySessionOptions.SectionName}:Store is the per-process memory store, so a session revoked on one instance stays valid on another. Set it to SqlServer, or {ReplayProtectionOptions.SectionName}:{nameof(ReplayProtectionOptions.AcknowledgeSingleInstance)} = true for a single-instance deployment.")
            : ValidateOptionsResult.Success;
    }
}

/// <summary>Tickets: when enabled, a guarded ring and a positive maximum lifetime.</summary>
internal sealed class SealedTicketOptionsValidator(KeyRingGuard guard) : IValidateOptions<SealedTicketOptions>
{
    public ValidateOptionsResult Validate(string? name, SealedTicketOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        return options.MaxLifetimeSeconds <= 0
            ? ValidateOptionsResult.Fail($"{SealedTicketOptions.SectionName}:MaxLifetimeSeconds must be positive.")
            : Verdicts.From(guard.Check(options, SealedTicketOptions.SectionName));
    }
}

/// <summary>Uploads: known extensions, positive caps, and a guarded receipt ring when receipts are on.</summary>
internal sealed class UploadSafetyOptionsValidator(KeyRingGuard guard) : IValidateOptions<UploadSafetyOptions>
{
    public ValidateOptionsResult Validate(string? name, UploadSafetyOptions options)
    {
        var unknown = options.AllowedExtensions.Where(extension => !FileSignatureSniffer.KnownSignatures.ContainsKey(extension)).ToList();
        if (unknown.Count > 0)
        {
            return ValidateOptionsResult.Fail($"{UploadSafetyOptions.SectionName}:AllowedExtensions has no magic-byte signature for {string.Join(", ", unknown)}.");
        }

        if (options.MaxFileBytes <= 0 || options.MaxFileCount <= 0 || options.MaxTotalBytes <= 0)
        {
            return ValidateOptionsResult.Fail($"{UploadSafetyOptions.SectionName}: MaxFileBytes, MaxFileCount and MaxTotalBytes must be positive.");
        }

        return options.ReceiptsEnabled ? Verdicts.From(guard.Check(options, UploadSafetyOptions.SectionName)) : ValidateOptionsResult.Success;
    }
}
