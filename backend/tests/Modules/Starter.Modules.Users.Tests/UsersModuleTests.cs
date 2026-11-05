using System.Reflection;
using Codelab.Security.Signing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Authorization;
using Starter.Application.Validation;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Users.Api;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Accounts;
using Starter.Modules.Users.Application.Import;
using Starter.Modules.Users.Application.Organizations;
using Starter.Modules.Users.Infrastructure;
using Starter.Modules.Users.Infrastructure.Import;

namespace Starter.Modules.Users.Tests;

public sealed class UsersModuleTests
{
    [Fact]
    public void The_module_is_named_after_its_assembly()
    {
        Assert.Equal("Users", new UsersModule().Name);
    }

    [Fact]
    public void Every_catalog_key_is_well_formed_unique_and_described()
    {
        var permissions = new UsersPermissionCatalogSource().Permissions;

        Assert.NotEmpty(permissions);
        Assert.All(permissions, permission => Assert.True(PermissionKeyFormat.IsValid(permission.Key), permission.Key));
        Assert.All(permissions, permission => Assert.False(string.IsNullOrWhiteSpace(permission.Description)));
        Assert.Equal(permissions.Count, permissions.Select(permission => permission.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_declared_key_is_in_the_catalog()
    {
        var declared = typeof(UsersPermissionKeys).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!);
        var catalog = new UsersPermissionCatalogSource().Permissions.Select(permission => permission.Key);

        Assert.Equal(declared.Order(StringComparer.Ordinal), catalog.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_contract_keys_are_declared() =>
        Assert.Equal(
            ["organizations.manage", "organizations.view", "users.import", "users.manage", "users.view"],
            new UsersPermissionCatalogSource().Permissions.Select(permission => permission.Key).Order(StringComparer.Ordinal));

    [Fact]
    public void Registration_declares_the_catalog_source_the_context_the_reader_validators_and_use_cases()
    {
        var services = new ServiceCollection();

        services.AddUsersModule(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPermissionCatalogSource) && descriptor.ImplementationType == typeof(UsersPermissionCatalogSource));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IModuleDatabase));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IUserSheetReader) && descriptor.ImplementationType == typeof(XlsxSheetReader));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IValidator<CreateUserCommand>));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IValidator<UpdateUserCommand>));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IValidator<CreateOrganizationCommand>));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IValidator<UpdateOrganizationCommand>));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(CommitUserImport));
    }

    [Fact]
    public void Routes_and_id_labels_follow_the_contract()
    {
        Assert.Equal("api/admin/users", UsersRoutes.Users);
        Assert.Equal("api/admin/organizations", UsersRoutes.Organizations);
        Assert.Equal("api/admin/user-import", UsersRoutes.Import);
        Assert.Equal("users", UsersIds.DomainLabel);
        Assert.Equal("organizations", OrganizationIds.DomainLabel);
        Assert.Equal("user-imports", UserImportIds.DomainLabel);
    }

    [Fact]
    public void No_request_body_or_sheet_row_carries_a_password()
    {
        Type[] bodies = [typeof(CreateUserCommand), typeof(UpdateUserCommand), typeof(ImportRow)];

        Assert.All(bodies, body => Assert.DoesNotContain(body.GetProperties(), property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Only_the_multipart_preview_takes_its_body_out_of_the_request_signature()
    {
        var exemptions = typeof(UserImportController).GetMethods()
            .Select(method => (method.Name, Exemption: method.GetCustomAttribute<SignatureExemptAttribute>()))
            .Where(pair => pair.Exemption is not null)
            .ToList();

        var (name, exemption) = Assert.Single(exemptions);
        Assert.Equal(nameof(UserImportController.PreviewAsync), name);
        Assert.Equal(SignatureExemption.BodyOnly, exemption!.Scope);
    }
}
