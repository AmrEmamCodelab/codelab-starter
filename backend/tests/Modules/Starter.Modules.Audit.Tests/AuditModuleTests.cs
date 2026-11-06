using System.Globalization;
using Codelab.Security.PublicIds;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Authorization;
using Starter.Application.Validation;
using Starter.Modules.Audit.Api;
using Starter.Modules.Audit.Application;
using Starter.Modules.Audit.Infrastructure;

namespace Starter.Modules.Audit.Tests;

public sealed class AuditModuleTests
{
    [Fact]
    public void The_module_is_named_after_its_assembly() => Assert.Equal("Audit", new AuditModule().Name);

    [Fact]
    public void Every_catalog_key_is_well_formed_unique_and_described()
    {
        var permissions = new AuditPermissionCatalogSource().Permissions;

        Assert.NotEmpty(permissions);
        Assert.All(permissions, permission => Assert.True(PermissionKeyFormat.IsValid(permission.Key), permission.Key));
        Assert.All(permissions, permission => Assert.False(string.IsNullOrWhiteSpace(permission.Description)));
        Assert.Equal(permissions.Count, permissions.Select(permission => permission.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_declared_key_is_in_the_catalog()
    {
        var declared = typeof(AuditPermissionKeys).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!);
        var catalog = new AuditPermissionCatalogSource().Permissions.Select(permission => permission.Key);

        Assert.Equal(declared.Order(StringComparer.Ordinal), catalog.Order(StringComparer.Ordinal));
        Assert.Equal("audit.view", AuditPermissionKeys.View);
    }

    [Fact]
    public void Registration_declares_the_catalog_source_the_validator_and_the_use_case()
    {
        var services = new ServiceCollection();

        services.AddAuditModule(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPermissionCatalogSource) && descriptor.ImplementationType == typeof(AuditPermissionCatalogSource));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IValidator<AuditTrailListQuery>) && descriptor.ImplementationType == typeof(AuditTrailListQueryValidator));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ListAuditTrail));
    }

    [Fact]
    public void Routes_and_id_families_are_fixed()
    {
        Assert.Equal("api/admin/audit", AuditRoutes.Root);
        Assert.Equal("audit-entries", AuditEntryIds.DomainLabel);
        Assert.Equal("audit-entities", AuditedEntityIds.DomainLabel);
    }
}

/// <summary>
/// A readable, reversible stand-in for the host codec (<c>domain:id</c>), installed once for this test assembly: the
/// tests check which family and which id a token carries, not the cryptography (Codelab.Security tests that).
/// </summary>
internal sealed class ReadableCodec : IPublicIdCodec
{
    private const char Separator = ':';
    private static readonly Lazy<IDisposable> Installation = new(() => PublicIdCodecAccessor.Install(new ReadableCodec()));

    public static void EnsureInstalled() => _ = Installation.Value;

    public static string TokenOf(string domain, long id) => domain + Separator + id.ToString(CultureInfo.InvariantCulture);

    public string Protect(string domain, long id) => TokenOf(domain, id);

    public bool TryUnprotect(string domain, string? token, out long id)
    {
        id = 0;
        var prefix = domain + Separator;
        return token is not null && token.StartsWith(prefix, StringComparison.Ordinal)
            && long.TryParse(token[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }
}
