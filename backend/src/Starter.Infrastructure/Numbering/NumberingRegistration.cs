using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Starter.Application.Numbering;
using Starter.Infrastructure.Persistence;

namespace Starter.Infrastructure.Numbering;

/// <summary>The kernel numbering registrations (called from <c>AddStarterKernel</c>).</summary>
public static class NumberingRegistration
{
    /// <summary>
    /// Registers <see cref="IRequestNumberAllocator"/> on SQL sequences in schema <c>numbering</c>
    /// (<see cref="NumberingDbContext"/>, migrated on start with the module contexts when
    /// <c>Database:ApplyMigrationsOnStartup</c> is on) and <see cref="NumberingOptions"/> (<c>Numbering</c>, validated on
    /// start). Requires <c>AddStarterPersistence</c> first. A second call is a no-op, so the schema is migrated once.
    /// </summary>
    public static IServiceCollection AddStarterNumbering(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IRequestNumberAllocator)))
        {
            return services;
        }

        services.AddOptions<NumberingOptions>().Bind(configuration.GetSection(NumberingOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NumberingOptions>, NumberingOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddModuleDbContext<NumberingDbContext>(NumberingDbContext.SchemaName);
        services.TryAddScoped<INumberSequences, SqlNumberSequences>();
        services.TryAddScoped<IRequestNumberAllocator, RequestNumberAllocator>();
        return services;
    }
}
