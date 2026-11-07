using System.Text.RegularExpressions;
using Codelab.Gateway.Api.Relay;
using Microsoft.Extensions.Options;

namespace Codelab.Gateway.Api.Sms;

/// <summary>
/// Validates <see cref="SmsProviderOptions"/> at start (registered only when the SMS profile is enabled): the URL
/// template renders to a usable upstream address, the templates name only known placeholders, the method and body
/// agree, the credential header is a pair of a valid header name and a single-line value, and the answer paths are
/// dotted member names. Messages name settings, never values (the credential is a secret).
/// </summary>
internal sealed partial class SmsProviderOptionsValidator(IHostEnvironment environment) : IValidateOptions<SmsProviderOptions>
{
    private const string SampleValue = "0";

    public ValidateOptionsResult Validate(string? name, SmsProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        problems.AddRange(TemplateProblems(options, environment.IsDevelopment()));
        problems.AddRange(MethodProblems(options));
        problems.AddRange(CredentialProblems(options));
        problems.AddRange(AnswerProblems(options));
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    /// <summary>The upstream address the URL template renders to (with neutral sample values), or null.</summary>
    internal static Uri? SampleAddress(string? urlTemplate)
    {
        if (string.IsNullOrWhiteSpace(urlTemplate))
        {
            return null;
        }

        var rendered = SmsTemplate.RenderUrl(urlTemplate, new SmsTemplateValues(SampleValue, SampleValue, SampleValue, SampleValue));
        return Uri.TryCreate(rendered, UriKind.Absolute, out var address) ? address : null;
    }

    private static IEnumerable<string> TemplateProblems(SmsProviderOptions options, bool isDevelopment)
    {
        if (UpstreamAddress.Problem(SampleAddress(options.UrlTemplate), Setting(nameof(SmsProviderOptions.UrlTemplate)), isDevelopment) is { } address)
        {
            yield return address;
        }

        foreach (var placeholder in SmsTemplate.UnknownPlaceholders(options.UrlTemplate).Concat(SmsTemplate.UnknownPlaceholders(options.BodyTemplate)))
        {
            yield return $"{SmsProviderOptions.SectionName}: the templates use the unknown placeholder {placeholder} (known: {string.Join(", ", SmsTemplate.Placeholders)}).";
        }

        if (string.IsNullOrWhiteSpace(options.LanguageCodes.Arabic) || string.IsNullOrWhiteSpace(options.LanguageCodes.English))
        {
            yield return $"{Setting(nameof(SmsProviderOptions.LanguageCodes))}: Arabic and English are required.";
        }
    }

    private static IEnumerable<string> MethodProblems(SmsProviderOptions options)
    {
        if (!Enum.IsDefined(options.Method) || !Enum.IsDefined(options.BodyFormat))
        {
            yield return $"{Setting(nameof(SmsProviderOptions.Method))} must be {(int)SmsProviderMethod.Get} (GET) or {(int)SmsProviderMethod.Post} (POST), and {nameof(SmsProviderOptions.BodyFormat)} {(int)SmsBodyFormat.Json} (JSON) or {(int)SmsBodyFormat.Form} (form).";
        }

        var hasBody = !string.IsNullOrWhiteSpace(options.BodyTemplate);
        if (options.Method == SmsProviderMethod.Post && !hasBody)
        {
            yield return $"{Setting(nameof(SmsProviderOptions.BodyTemplate))} is required when {nameof(SmsProviderOptions.Method)} is POST.";
        }

        if (options.Method == SmsProviderMethod.Get && hasBody)
        {
            yield return $"{Setting(nameof(SmsProviderOptions.BodyTemplate))} must be empty when {nameof(SmsProviderOptions.Method)} is GET.";
        }
    }

    private static IEnumerable<string> CredentialProblems(SmsProviderOptions options)
    {
        var hasName = !string.IsNullOrWhiteSpace(options.AuthHeaderName);
        var hasValue = !string.IsNullOrEmpty(options.AuthHeaderValue);
        if (hasName != hasValue)
        {
            yield return $"{Setting(nameof(SmsProviderOptions.AuthHeaderName))} and {nameof(SmsProviderOptions.AuthHeaderValue)} are set together or not at all.";
        }

        if (hasName && !HeaderName().IsMatch(options.AuthHeaderName!))
        {
            yield return $"{Setting(nameof(SmsProviderOptions.AuthHeaderName))} is not a valid HTTP header name.";
        }

        if (hasValue && options.AuthHeaderValue!.Any(char.IsControl))
        {
            yield return $"{Setting(nameof(SmsProviderOptions.AuthHeaderValue))} must be a single line without control characters.";
        }
    }

    private static IEnumerable<string> AnswerProblems(SmsProviderOptions options)
    {
        foreach (var (setting, path) in new[] { (nameof(SmsProviderOptions.AcceptedMember), options.AcceptedMember), (nameof(SmsProviderOptions.MessageIdMember), options.MessageIdMember) })
        {
            if (!string.IsNullOrEmpty(path) && !MemberPath().IsMatch(path))
            {
                yield return $"{Setting(setting)} must be a dotted path of member names (for example result.id).";
            }
        }
    }

    private static string Setting(string setting) => SmsProviderOptions.SectionName + ":" + setting;

    [GeneratedRegex("^[A-Za-z0-9!#$%&'*+.^_`|~-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderName();

    [GeneratedRegex(@"^[A-Za-z_$][A-Za-z0-9_$-]*(?:\.[A-Za-z_$][A-Za-z0-9_$-]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex MemberPath();
}
