using System.Net.Mail;
using Codelab.Integration.Gateway;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Starter.Modules.Notifications.Infrastructure.Options;

/// <summary>Where e-mail goes. Stored and configured as the integer value.</summary>
public enum EmailDeliveryMode
{
    /// <summary>
    /// Writes each message as an <c>.eml</c> file into <see cref="EmailDeliveryOptions.PickupDirectory"/> (development and
    /// CI: no SMTP sink is reachable there). Nothing leaves the machine. Refused at start outside Development unless
    /// <see cref="EmailDeliveryOptions.AllowFileSink"/> is set: the files hold activation links and personal data.
    /// </summary>
    FileDrop = 1,

    /// <summary>Sends through the SMTP relay inside the backend zone (<see cref="EmailDeliveryOptions.Smtp"/>; no internet egress).</summary>
    Smtp = 2,
}

/// <summary>Where SMS goes. Configured as the integer value.</summary>
public enum SmsDeliveryMode
{
    /// <summary>
    /// Appends each message as a JSON line to <see cref="SmsDeliveryOptions.FilePath"/> (development and CI). Refused at
    /// start outside Development while SMS is enabled, unless <see cref="SmsDeliveryOptions.AllowFileSink"/> is set.
    /// </summary>
    File = 1,

    /// <summary>
    /// The integration gateway's SMS relay profile (<c>GatewaySmsSender</c>, signed with this API's ES256 service
    /// assertion): the production path of a backend without egress. Needs <c>Codelab:Integration:Gateway:BaseUrl</c>.
    /// </summary>
    Gateway = 2,
}

/// <summary>E-mail delivery (section <c>Notifications:Email</c>), validated on start.</summary>
public sealed class EmailDeliveryOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Notifications:Email";

    /// <summary>The default sender address (a reserved domain: every deployment sets its own).</summary>
    public const string DefaultFromAddress = "no-reply@example.test";

    /// <summary>The default sender display name.</summary>
    public const string DefaultFromName = "Starter";

    /// <summary>The folder under the temp folder that holds dropped mail when <see cref="PickupDirectory"/> is unset.</summary>
    public const string DefaultPickupFolderName = "starter-mail";

    /// <summary>The longest sender display name.</summary>
    public const int FromNameMaxLength = 128;

    /// <summary>
    /// SMTP (default: a deployment that names no relay fails at start on the missing <c>Smtp:Host</c>) or file drop
    /// (Development; elsewhere only with <see cref="AllowFileSink"/>).
    /// </summary>
    public EmailDeliveryMode Mode { get; set; } = EmailDeliveryMode.Smtp;

    /// <summary>
    /// True to allow <see cref="EmailDeliveryMode.FileDrop"/> outside the Development environment (a CI or test host
    /// only). Default false: a production host never writes activation links or personal data to a local folder.
    /// </summary>
    public bool AllowFileSink { get; set; }

    /// <summary>The sender address.</summary>
    public string FromAddress { get; set; } = DefaultFromAddress;

    /// <summary>The sender display name.</summary>
    public string FromName { get; set; } = DefaultFromName;

    /// <summary>An absolute folder for <see cref="EmailDeliveryMode.FileDrop"/>; unset means <c>%TEMP%\starter-mail</c>.</summary>
    public string? PickupDirectory { get; set; }

    /// <summary>The relay for <see cref="EmailDeliveryMode.Smtp"/>.</summary>
    public SmtpRelayOptions Smtp { get; set; } = new();

    /// <summary>The pickup folder in use.</summary>
    public string ResolvedPickupDirectory =>
        string.IsNullOrWhiteSpace(PickupDirectory) ? Path.Combine(Path.GetTempPath(), DefaultPickupFolderName) : PickupDirectory;
}

/// <summary>
/// The internal SMTP relay (section <c>Notifications:Email:Smtp</c>). No user name or password: the relay accepts the
/// application server by address inside the backend zone, so no mail secret lives in configuration.
/// </summary>
public sealed class SmtpRelayOptions
{
    /// <summary>The default SMTP port of an internal relay.</summary>
    public const int DefaultPort = 25;

    /// <summary>The highest TCP port.</summary>
    public const int MaxPort = 65535;

    /// <summary>The default send timeout, in seconds.</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>The longest send timeout, in seconds.</summary>
    public const int MaxTimeoutSeconds = 300;

    /// <summary>The relay host name.</summary>
    public string? Host { get; set; }

    /// <summary>The relay port (1 to 65535).</summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>STARTTLS on the connection (default true; false only for a relay that offers none).</summary>
    public bool EnableTls { get; set; } = true;

    /// <summary>The send timeout in seconds (1 to 300).</summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
}

/// <summary>SMS delivery (section <c>Notifications:Sms</c>), validated on start. Off unless the project enables it.</summary>
public sealed class SmsDeliveryOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Notifications:Sms";

    /// <summary>The folder under the temp folder that holds the SMS file when <see cref="FilePath"/> is unset.</summary>
    public const string DefaultFolderName = "starter-sms";

    /// <summary>The SMS file name in the default folder.</summary>
    public const string DefaultFileName = "sms.jsonl";

    /// <summary>Whether SMS is delivered at all (default false: SMS channels end Disabled).</summary>
    public bool Enabled { get; set; }

    /// <summary>File (default; Development, elsewhere only with <see cref="AllowFileSink"/>) or the integration gateway.</summary>
    public SmsDeliveryMode Mode { get; set; } = SmsDeliveryMode.File;

    /// <summary>
    /// True to allow <see cref="SmsDeliveryMode.File"/> outside the Development environment while SMS is enabled (a CI
    /// or test host only). Default false.
    /// </summary>
    public bool AllowFileSink { get; set; }

    /// <summary>An absolute file for <see cref="SmsDeliveryMode.File"/>; unset means <c>%TEMP%\starter-sms\sms.jsonl</c>.</summary>
    public string? FilePath { get; set; }

    /// <summary>The SMS file in use.</summary>
    public string ResolvedFilePath =>
        string.IsNullOrWhiteSpace(FilePath) ? Path.Combine(Path.GetTempPath(), DefaultFolderName, DefaultFileName) : FilePath;
}

/// <summary>
/// Start-up validation of <see cref="EmailDeliveryOptions"/>. Outside Development a file drop is refused unless
/// <see cref="EmailDeliveryOptions.AllowFileSink"/> is set, so a deployment that forgets the relay fails at start
/// instead of writing every activation link to the server's disk and marking it sent.
/// </summary>
public sealed class EmailDeliveryOptionsValidator(IHostEnvironment environment) : IValidateOptions<EmailDeliveryOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, EmailDeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        if (!Enum.IsDefined(options.Mode))
        {
            problems.Add($"{EmailDeliveryOptions.SectionName}:{nameof(options.Mode)} must be 1 (FileDrop) or 2 (Smtp).");
        }

        if (!MailAddress.TryCreate(options.FromAddress, out _))
        {
            problems.Add($"{EmailDeliveryOptions.SectionName}:{nameof(options.FromAddress)} must be an e-mail address.");
        }

        if (string.IsNullOrWhiteSpace(options.FromName) || options.FromName.Length > EmailDeliveryOptions.FromNameMaxLength)
        {
            problems.Add($"{EmailDeliveryOptions.SectionName}:{nameof(options.FromName)} must be 1 to {EmailDeliveryOptions.FromNameMaxLength} characters.");
        }

        if (!string.IsNullOrWhiteSpace(options.PickupDirectory) && !Path.IsPathFullyQualified(options.PickupDirectory))
        {
            problems.Add($"{EmailDeliveryOptions.SectionName}:{nameof(options.PickupDirectory)} must be an absolute path.");
        }

        if (options.Mode == EmailDeliveryMode.Smtp)
        {
            problems.AddRange(SmtpProblems(options.Smtp));
        }

        problems.AddRange(FileSinkProblems(options));
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    private IEnumerable<string> FileSinkProblems(EmailDeliveryOptions options)
    {
        if (options.Mode == EmailDeliveryMode.FileDrop && !options.AllowFileSink && !environment.IsDevelopment())
        {
            yield return
                $"{EmailDeliveryOptions.SectionName}:{nameof(options.Mode)} 1 (FileDrop) is allowed only in Development: it writes every "
                + $"e-mail, activation links included, to a local folder. Set 2 (Smtp) with {nameof(options.Smtp)}:{nameof(SmtpRelayOptions.Host)}, "
                + $"or {EmailDeliveryOptions.SectionName}:{nameof(options.AllowFileSink)} true on a test or CI host.";
        }
    }

    private static IEnumerable<string> SmtpProblems(SmtpRelayOptions smtp)
    {
        const string Section = EmailDeliveryOptions.SectionName + ":" + nameof(EmailDeliveryOptions.Smtp);
        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            yield return $"{Section}:{nameof(smtp.Host)} is required when the mode is Smtp.";
        }

        if (smtp.Port < 1 || smtp.Port > SmtpRelayOptions.MaxPort)
        {
            yield return $"{Section}:{nameof(smtp.Port)} must be between 1 and {SmtpRelayOptions.MaxPort}.";
        }

        if (smtp.TimeoutSeconds < 1 || smtp.TimeoutSeconds > SmtpRelayOptions.MaxTimeoutSeconds)
        {
            yield return $"{Section}:{nameof(smtp.TimeoutSeconds)} must be between 1 and {SmtpRelayOptions.MaxTimeoutSeconds}.";
        }
    }
}

/// <summary>
/// Start-up validation of <see cref="SmsDeliveryOptions"/>. Outside Development an enabled file sink is refused unless
/// <see cref="SmsDeliveryOptions.AllowFileSink"/> is set; an enabled gateway mode needs the gateway's base URL (the
/// gateway client's own validator then checks its scheme, audience and the S2S identity).
/// </summary>
public sealed class SmsDeliveryOptionsValidator(IHostEnvironment environment, IOptions<GatewayOptions> gateway) : IValidateOptions<SmsDeliveryOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SmsDeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        if (!Enum.IsDefined(options.Mode))
        {
            problems.Add($"{SmsDeliveryOptions.SectionName}:{nameof(options.Mode)} must be 1 (File) or 2 (Gateway).");
        }

        if (options.Enabled && options.Mode == SmsDeliveryMode.Gateway && gateway.Value.BaseUrl is null)
        {
            problems.Add(
                $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.BaseUrl)} is required when {SmsDeliveryOptions.SectionName}:{nameof(options.Mode)} "
                + $"is {(int)SmsDeliveryMode.Gateway} (Gateway) and SMS is enabled: the SMS relay is the integration gateway.");
        }

        if (!string.IsNullOrWhiteSpace(options.FilePath) && !Path.IsPathFullyQualified(options.FilePath))
        {
            problems.Add($"{SmsDeliveryOptions.SectionName}:{nameof(options.FilePath)} must be an absolute path.");
        }

        problems.AddRange(FileSinkProblems(options));
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    private IEnumerable<string> FileSinkProblems(SmsDeliveryOptions options)
    {
        if (options.Enabled && options.Mode == SmsDeliveryMode.File && !options.AllowFileSink && !environment.IsDevelopment())
        {
            yield return
                $"{SmsDeliveryOptions.SectionName}:{nameof(options.Mode)} 1 (File) is allowed only in Development: it writes every SMS to a "
                + $"local file. Disable SMS, or set {SmsDeliveryOptions.SectionName}:{nameof(options.AllowFileSink)} true on a test or CI host.";
        }
    }
}
