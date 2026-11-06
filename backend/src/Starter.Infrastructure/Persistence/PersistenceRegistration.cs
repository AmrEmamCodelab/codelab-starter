using Codelab.Security.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Starter.Infrastructure.Auditing;
using Starter.Infrastructure.Startup;

namespace Starter.Infrastructure.Persistence;

/// <summary>The persistence registrations of the kernel and of each module's DbContext.</summary>
public static class PersistenceRegistration
{
    /// <summary>The name of every module's migrations history table (it lives in the module's own schema).</summary>
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    /// <summary>
    /// Binds <c>ConnectionStrings</c> and <c>Database</c>, gives the Codelab.Security SQL stores the application
    /// connection string when they have none of their own, registers the startup runner (migrations, then startup
    /// tasks) and the audit trail (<c>AddStarterAuditing</c>: every module context is audited). Call it BEFORE
    /// <c>AddCodelabSecurity</c>, so the runner creates the database before the security stores ensure their tables.
    /// </summary>
    public static IServiceCollection AddStarterPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<ConnectionStringsOptions>()
            .Bind(configuration.GetSection(ConnectionStringsOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ConnectionStringsOptions>, ConnectionStringsOptionsValidator>());
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>());
        services.AddOptions<SqlStoreOptions>().PostConfigure<IOptions<ConnectionStringsOptions>>(UseApplicationDatabase);
        services.AddHostedService<StartupTaskRunner>();
        services.AddStarterAuditing();
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TContext"/> on the application database with its migrations history table in
    /// <paramref name="schema"/>, attaches the scoped <see cref="AuditSaveChangesInterceptor"/> (every save of the context
    /// is audited; <c>AddStarterPersistence</c> registers it), and announces the context to the startup runner.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : ModuleDbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        services.AddDbContext<TContext>((provider, options) => options.UseSqlServer(
            provider.GetRequiredService<IOptions<ConnectionStringsOptions>>().Value.Starter,
            sql => sql.MigrationsHistoryTable(MigrationsHistoryTable, schema))
            .AddInterceptors(provider.GetRequiredService<AuditSaveChangesInterceptor>()));
        services.AddScoped<IModuleDatabase, ModuleDatabase<TContext>>();
        return services;
    }

    private static void UseApplicationDatabase(SqlStoreOptions store, IOptions<ConnectionStringsOptions> connections)
    {
        if (string.IsNullOrWhiteSpace(store.ConnectionString))
        {
            store.ConnectionString = connections.Value.Starter;
        }
    }
}
