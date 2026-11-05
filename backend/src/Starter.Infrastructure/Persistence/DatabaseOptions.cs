using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Starter.Infrastructure.Persistence;

/// <summary>Database behaviour on start (section <c>Database</c>).</summary>
public sealed class DatabaseOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Database";

    /// <summary>
    /// Apply every module's pending EF Core migrations when the host starts. True only in
    /// <c>appsettings.Development.json</c>; every other environment gets its schema from the DBA scripts.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; }
}

/// <summary>
/// The connection strings (section <c>ConnectionStrings</c>). The property name is the solution token, so a fork's
/// rename moves the key together with the code (<c>ConnectionStrings:Starter</c> in the starter).
/// </summary>
public sealed class ConnectionStringsOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "ConnectionStrings";

    /// <summary>The application database (every module's DbContext and the Codelab.Security SQL stores).</summary>
    public string? Starter { get; set; }
}

/// <summary>Start-up validation of <see cref="ConnectionStringsOptions"/>.</summary>
public sealed class ConnectionStringsOptionsValidator : IValidateOptions<ConnectionStringsOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ConnectionStringsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return string.IsNullOrWhiteSpace(options.Starter)
            ? ValidateOptionsResult.Fail($"{ConnectionStringsOptions.SectionName}:{nameof(options.Starter)} is required.")
            : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Start-up validation of <see cref="DatabaseOptions"/>: migrations on start are a Development convenience only;
/// every other environment gets its schema from the DBA scripts, so the switch refuses the start there.
/// </summary>
public sealed class DatabaseOptionsValidator(IHostEnvironment environment) : IValidateOptions<DatabaseOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.ApplyMigrationsOnStartup && !environment.IsDevelopment()
            ? ValidateOptionsResult.Fail($"{DatabaseOptions.SectionName}:{nameof(options.ApplyMigrationsOnStartup)} may be true only in Development; this environment is '{environment.EnvironmentName}'.")
            : ValidateOptionsResult.Success;
    }
}
