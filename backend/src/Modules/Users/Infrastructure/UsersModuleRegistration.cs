using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Starter.Application.Authorization;
using Starter.Application.Validation;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Users.Application;
using Starter.Modules.Users.Application.Accounts;
using Starter.Modules.Users.Application.Import;
using Starter.Modules.Users.Application.Organizations;
using Starter.Modules.Users.Infrastructure.Import;
using Starter.Modules.Users.Infrastructure.Options;
using Starter.Modules.Users.Infrastructure.Persistence;

namespace Starter.Modules.Users.Infrastructure;

/// <summary>The Users module's registrations (called by <see cref="UsersModule"/>).</summary>
public static class UsersModuleRegistration
{
    /// <summary>
    /// Registers the permission catalog source, the options (validated on start), the DbContext in schema
    /// <c>users</c> and its repositories, the xlsx reader, the validators and the use cases. The accounts come from the
    /// kernel <c>IAccountAdministration</c> port (Identity), the outbox from the kernel <c>IOutbox</c>.
    /// </summary>
    public static IServiceCollection AddUsersModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, UsersPermissionCatalogSource>());
        services.TryAddSingleton(TimeProvider.System);
        AddOptions(services, configuration);
        services.AddModuleDbContext<UsersDbContext>(UsersDbContext.SchemaName);
        services.AddScoped<IUsersUnitOfWork>(provider => provider.GetRequiredService<UsersDbContext>());
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IImportBatchRepository, ImportBatchRepository>();
        services.AddSingleton<IUserPublicIds, UserPublicIds>();
        services.AddSingleton<IUserSheetReader, XlsxSheetReader>();
        AddValidators(services);
        AddUseCases(services);
        // scaffold:registrations
        return services;
    }

    /// <summary>Maps nothing extra: every Users endpoint is a controller, found by the kernel.</summary>
    public static IEndpointRouteBuilder UseUsersModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ActivationOptions>().Bind(configuration.GetSection(ActivationOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ActivationOptions>, ActivationOptionsValidator>());
        services.AddOptions<ImportOptions>().Bind(configuration.GetSection(ImportOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ImportOptions>, ImportOptionsValidator>());
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<ActivationOptions>>().Value.ToSettings());
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<ImportOptions>>().Value.ToSettings());
    }

    private static void AddValidators(IServiceCollection services)
    {
        services.AddSingleton<IValidator<CreateOrganizationCommand>, CreateOrganizationCommandValidator>();
        services.AddSingleton<IValidator<UpdateOrganizationCommand>, UpdateOrganizationCommandValidator>();
        services.AddSingleton<IValidator<CreateUserCommand>, CreateUserCommandValidator>();
        services.AddSingleton<IValidator<UpdateUserCommand>, UpdateUserCommandValidator>();
    }

    private static void AddUseCases(IServiceCollection services)
    {
        services.AddScoped<ScopedOrganizations>();
        services.AddScoped<AccountAccess>();
        services.AddScoped<ListOrganizations>();
        services.AddScoped<GetOrganization>();
        services.AddScoped<CreateOrganization>();
        services.AddScoped<UpdateOrganization>();
        services.AddScoped<SetOrganizationActive>();
        services.AddScoped<UserViews>();
        services.AddScoped<AccountActivations>();
        services.AddScoped<ListUsers>();
        services.AddScoped<GetUser>();
        services.AddScoped<CreateUser>();
        services.AddScoped<UpdateUser>();
        services.AddScoped<SetUserActive>();
        services.AddScoped<UnlockUser>();
        services.AddScoped<IssueUserActivation>();
        services.AddScoped<ImportRowValidator>();
        services.AddScoped<ImportBatches>();
        services.AddScoped<PreviewUserImport>();
        services.AddScoped<CommitUserImport>();
    }
}
