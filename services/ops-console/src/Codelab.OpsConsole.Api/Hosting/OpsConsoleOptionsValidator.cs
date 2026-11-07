using Microsoft.Extensions.Options;

namespace Codelab.OpsConsole.Api.Hosting;

/// <summary>
/// Refuses to start on a console that could not work or would leak: no operator group, no API address, an address that
/// would carry credentials in clear text off the machine, out-of-range limits, or the dev operator outside Development.
/// </summary>
public sealed class OpsConsoleOptionsValidator(IHostEnvironment environment) : IValidateOptions<OpsConsoleOptions>
{
    /// <summary>The longest accepted call timeout, in seconds.</summary>
    public const int MaxTimeoutSeconds = 120;

    /// <summary>The most events one lookup may ask Seq for.</summary>
    public const int MaxEventsCeiling = 1000;

    /// <summary>The longest accepted look-back, in days.</summary>
    public const int MaxLookbackDays = 90;

    private const string Section = OpsConsoleOptions.SectionName;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpsConsoleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var isDevelopment = environment.IsDevelopment();
        var failures = new List<string>();
        CheckOperators(options, isDevelopment, failures);
        CheckApi(options.Api, isDevelopment, failures);
        CheckSeq(options.Seq, isDevelopment, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckOperators(OpsConsoleOptions options, bool isDevelopment, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.OperatorGroup))
        {
            failures.Add($"{Section}:OperatorGroup is required: the AD group SID (or DOMAIN\\Group name) operators belong to.");
        }

        if (options.DevOperator.Enabled && !isDevelopment)
        {
            failures.Add($"{Section}:DevOperator:Enabled is allowed only in Development; operators sign in with Windows everywhere else.");
        }

        if (options.DevOperator.Enabled && string.IsNullOrWhiteSpace(options.DevOperator.Name))
        {
            failures.Add($"{Section}:DevOperator:Name is required when the dev operator is enabled.");
        }
    }

    private static void CheckApi(OpsApiOptions api, bool isDevelopment, List<string> failures)
    {
        if (!UpstreamAddress.IsAcceptable(api.BaseUrl, isDevelopment))
        {
            failures.Add($"{Section}:Api:BaseUrl must be an absolute https URL (http only in Development or to a loopback host).");
        }

        CheckRange(api.TimeoutSeconds, MaxTimeoutSeconds, $"{Section}:Api:TimeoutSeconds", failures);
    }

    private static void CheckSeq(SeqReadOptions seq, bool isDevelopment, List<string> failures)
    {
        if (seq.IsConfigured && !UpstreamAddress.IsAcceptable(seq.BaseUrl, isDevelopment))
        {
            failures.Add($"{Section}:Seq:BaseUrl must be an absolute https URL (http only in Development or to a loopback host), or empty.");
        }

        CheckRange(seq.TimeoutSeconds, MaxTimeoutSeconds, $"{Section}:Seq:TimeoutSeconds", failures);
        CheckRange(seq.MaxEvents, MaxEventsCeiling, $"{Section}:Seq:MaxEvents", failures);
        CheckRange(seq.LookbackDays, MaxLookbackDays, $"{Section}:Seq:LookbackDays", failures);
    }

    private static void CheckRange(int value, int maximum, string setting, List<string> failures)
    {
        if (value < 1 || value > maximum)
        {
            failures.Add($"{setting} must be between 1 and {maximum}.");
        }
    }
}
