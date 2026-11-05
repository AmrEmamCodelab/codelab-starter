namespace Starter.DevIdp.Hosting;

/// <summary>
/// The dev IdP signs anyone in as any fixture user, so it must never run where it could be reached as a real
/// identity provider: every entry point calls <see cref="EnsureDevelopment"/> and refuses any environment other than
/// <c>Development</c> (Staging, Production, and any custom name).
/// </summary>
public static class DevIdpEnvironmentGuard
{
    /// <summary>Throws <see cref="DevIdpEnvironmentException"/> unless <paramref name="environment"/> is Development.</summary>
    public static void EnsureDevelopment(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment())
        {
            throw new DevIdpEnvironmentException(environment.EnvironmentName);
        }
    }
}

/// <summary>The dev IdP was asked to start outside the Development environment.</summary>
public sealed class DevIdpEnvironmentException : InvalidOperationException
{
    /// <summary>Creates the exception for <paramref name="environmentName"/>.</summary>
    public DevIdpEnvironmentException(string environmentName)
        : base($"The dev IdP runs only in the {Environments.Development} environment; refusing to start in '{environmentName}'. It is a test double and is never deployed.")
    {
        EnvironmentName = environmentName;
    }

    /// <summary>The refused environment.</summary>
    public string EnvironmentName { get; }
}
