using Microsoft.Extensions.Configuration;

namespace Codelab.Observability;

/// <summary>
/// Everything a deployment decides about observability, bound from the <see cref="SectionName"/> configuration
/// section. What only the host's code can decide (its name, its PII predicate) is in <see cref="ObservabilityOptions"/>.
/// </summary>
public sealed class ObservabilitySettings
{
    /// <summary>The configuration section: <c>Observability</c>.</summary>
    public const string SectionName = "Observability";

    /// <summary>The full key of <see cref="DeploymentEnvironment"/>, for error messages.</summary>
    public const string DeploymentEnvironmentKey = SectionName + ":" + nameof(DeploymentEnvironment);

    /// <summary>The tier, one of <see cref="DeploymentEnvironments.All"/>; defaults to the environment name in lowercase.</summary>
    public string? DeploymentEnvironment { get; set; }

    /// <summary>The rolling CLEF file, on by default.</summary>
    public LogFileSettings LogFile { get; set; } = new();

    /// <summary>The opt-in Seq sink (off until a server URL is set).</summary>
    public SeqSettings Seq { get; set; } = new();

    /// <summary>The opt-in OpenTelemetry tracer (off by default).</summary>
    public TracingSettings Tracing { get; set; } = new();

    /// <summary>The user-id hash key.</summary>
    public UserIdHashSettings UserIdHash { get; set; } = new();

    /// <summary>
    /// Binds the <see cref="SectionName"/> section; a missing section yields the defaults. Validation is separate
    /// (<see cref="Validate"/>) so a caller can read the settings without failing.
    /// </summary>
    public static ObservabilitySettings From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetSection(SectionName).Get<ObservabilitySettings>() ?? new ObservabilitySettings();
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/>, naming the key, for a setting the host must refuse to start
    /// with. The user-id hash key is checked by <see cref="Observability.UserIdHash.Create(UserIdHashSettings, Microsoft.Extensions.Hosting.IHostEnvironment)"/>.
    /// </summary>
    public void Validate(bool isDevelopment)
    {
        LogFile.Validate();
        Seq.Validate(isDevelopment);
        Tracing.Validate();
    }
}

/// <summary>The rolling CLEF file (<c>Observability:LogFile</c>), the record of what happened.</summary>
public sealed class LogFileSettings
{
    /// <summary>The full key prefix, for error messages.</summary>
    public const string KeyPrefix = ObservabilitySettings.SectionName + ":" + nameof(ObservabilitySettings.LogFile);

    /// <summary>The default number of retained files.</summary>
    public const int DefaultRetainedFileCountLimit = 31;

    /// <summary>The default size limit of one file: 50 MB.</summary>
    public const long DefaultFileSizeLimitBytes = 50L * 1024 * 1024;

    /// <summary>The folder under the application base used when <see cref="Directory"/> is blank.</summary>
    public const string DefaultDirectoryName = "logs";

    /// <summary>Whether the file sink is on. Default true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The log folder; blank means <c>{AppContext.BaseDirectory}/logs</c>.</summary>
    public string? Directory { get; set; }

    /// <summary>How many FILES are kept (size it as retention days times files per day).</summary>
    public int RetainedFileCountLimit { get; set; } = DefaultRetainedFileCountLimit;

    /// <summary>The size at which a file rolls over.</summary>
    public long FileSizeLimitBytes { get; set; } = DefaultFileSizeLimitBytes;

    /// <summary>The folder actually used: <see cref="Directory"/>, or the application's own <c>logs</c> folder.</summary>
    public string ResolvedDirectory => string.IsNullOrWhiteSpace(Directory)
        ? Path.Combine(AppContext.BaseDirectory, DefaultDirectoryName)
        : Directory;

    internal void Validate()
    {
        if (RetainedFileCountLimit < 1 || FileSizeLimitBytes < 1)
        {
            throw new InvalidOperationException(
                $"{KeyPrefix}:{nameof(RetainedFileCountLimit)} and {KeyPrefix}:{nameof(FileSizeLimitBytes)} must both be positive "
                + $"(got {RetainedFileCountLimit} and {FileSizeLimitBytes}).");
        }
    }
}

/// <summary>The opt-in Seq sink (<c>Observability:Seq</c>). No URL, no sink.</summary>
public sealed class SeqSettings
{
    /// <summary>The full key prefix, for error messages.</summary>
    public const string KeyPrefix = ObservabilitySettings.SectionName + ":" + nameof(ObservabilitySettings.Seq);

    /// <summary>The bounded in-memory queue while Seq is unreachable (events beyond it are dropped; the file keeps them).</summary>
    public const int DefaultQueueSizeLimit = 100_000;

    /// <summary>The Seq address; https outside Development. Blank turns the sink off.</summary>
    public string? ServerUrl { get; set; }

    /// <summary>The tier's ingest-only API key: an app-pool or environment variable, never a settings file.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The queue bound; must be positive.</summary>
    public int QueueSizeLimit { get; set; } = DefaultQueueSizeLimit;

    /// <summary>Whether the sink is on.</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(ServerUrl);

    internal void Validate(bool isDevelopment)
    {
        if (QueueSizeLimit < 1)
        {
            throw new InvalidOperationException($"{KeyPrefix}:{nameof(QueueSizeLimit)} must be positive (got {QueueSizeLimit}).");
        }

        if (!Enabled)
        {
            return;
        }

        var allowed = Uri.TryCreate(ServerUrl!.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (isDevelopment && uri.Scheme == Uri.UriSchemeHttp));
        if (!allowed)
        {
            throw new InvalidOperationException(
                $"{KeyPrefix}:{nameof(ServerUrl)} must be an absolute https URL outside Development. Clear it to turn the Seq sink off.");
        }
    }
}

/// <summary>The opt-in OpenTelemetry tracer (<c>Observability:Tracing</c>), exported over OTLP.</summary>
public sealed class TracingSettings
{
    /// <summary>The full key prefix, for error messages.</summary>
    public const string KeyPrefix = ObservabilitySettings.SectionName + ":" + nameof(ObservabilitySettings.Tracing);

    /// <summary>Whether tracing is on. Default false: no tracer is registered and nothing is exported.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The OTLP/HTTP traces URL of the collector (for example <c>http://localhost:4318/v1/traces</c>, used as given);
    /// required when <see cref="Enabled"/>.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    internal void Validate()
    {
        if (Enabled && !Uri.TryCreate(OtlpEndpoint, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"{KeyPrefix}:{nameof(OtlpEndpoint)} must be an absolute URL when {KeyPrefix}:{nameof(Enabled)} is true.");
        }
    }
}

/// <summary>The user-id hash key (<c>Observability:UserIdHash</c>).</summary>
public sealed class UserIdHashSettings
{
    /// <summary>The full key of <see cref="Key"/>, for error messages.</summary>
    public const string KeyName = ObservabilitySettings.SectionName + ":" + nameof(ObservabilitySettings.UserIdHash) + ":" + nameof(Key);

    /// <summary>Base64 of at least <see cref="Observability.UserIdHash.MinimumKeyBytes"/> random bytes, generated per environment by init.</summary>
    public string? Key { get; set; }
}
