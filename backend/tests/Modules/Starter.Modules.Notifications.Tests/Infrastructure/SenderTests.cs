using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Codelab.Integration.Gateway;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Starter.Domain.Localization;
using Starter.Modules.Notifications.Application;
using Starter.Modules.Notifications.Infrastructure.Options;
using Starter.Modules.Notifications.Infrastructure.Sending;

namespace Starter.Modules.Notifications.Tests.Infrastructure;

public sealed class FileDropEmailSenderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "starter-notifications-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task It_writes_one_valid_eml_file_with_the_utf8_subject_and_html_body()
    {
        var options = new EmailDeliveryOptions { PickupDirectory = _folder, FromAddress = "no-reply@example.test", FromName = "Starter" };
        var sender = new FileDropEmailSender(Options.Create(options));

        await sender.SendAsync(new EmailMessage("user@example.test", "إشعار تجريبي", "<p>مرحباً &amp; welcome</p>"), CancellationToken.None);

        var file = Assert.Single(Directory.GetFiles(_folder, "*.eml"));
        var eml = await File.ReadAllTextAsync(file);
        var (headers, body) = Split(eml);
        Assert.Matches(new Regex("^To: user@example\\.test\\r?$", RegexOptions.Multiline), headers);
        Assert.Contains("no-reply@example.test", Header(headers, "From"), StringComparison.Ordinal);
        Assert.Contains("text/html", Header(headers, "Content-Type"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("utf-8", Header(headers, "Content-Type"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("إشعار تجريبي", DecodeWords(Header(headers, "Subject")));
        Assert.Contains("<p>مرحباً &amp; welcome</p>", DecodeBody(headers, body), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    internal static (string Headers, string Body) Split(string eml)
    {
        var index = eml.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        return (eml[..index], eml[(index + 4)..]);
    }

    internal static string Header(string headers, string name)
    {
        var unfolded = Regex.Replace(headers, "\\r\\n[ \\t]+", " ");
        var match = Regex.Match(unfolded, $"^{name}: (.*)$", RegexOptions.Multiline);
        Assert.True(match.Success, $"header {name} missing");
        return match.Groups[1].Value.TrimEnd('\r');
    }

    internal static string DecodeWords(string value) =>
        Regex.Replace(value, "=\\?utf-8\\?B\\?([^?]+)\\?=\\s*", match => Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups[1].Value)), RegexOptions.IgnoreCase);

    internal static string DecodeBody(string headers, string body)
    {
        var transfer = Header(headers, "Content-Transfer-Encoding");
        if (transfer.Equals("base64", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(Regex.Replace(body, "\\s", string.Empty)));
        }

        var softBreaksRemoved = body.Replace("=\r\n", string.Empty, StringComparison.Ordinal);
        var bytes = new List<byte>();
        for (var index = 0; index < softBreaksRemoved.Length; index++)
        {
            if (softBreaksRemoved[index] == '=' && index + 2 < softBreaksRemoved.Length)
            {
                bytes.Add(Convert.ToByte(softBreaksRemoved.Substring(index + 1, 2), 16));
                index += 2;
            }
            else
            {
                bytes.Add((byte)softBreaksRemoved[index]);
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}

public sealed class FileSmsSenderTests : IDisposable
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "starter-notifications-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task It_records_Arabic_and_English_messages_as_readable_json_lines()
    {
        var path = Path.Combine(_folder, "nested", "sms.jsonl");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 9, 30, 0, TimeSpan.Zero));
        using var sender = new FileSmsSender(Options.Create(new SmsDeliveryOptions { Enabled = true, FilePath = path }), clock);

        await sender.SendAsync(new SmsMessage("+10000000001", "رسالة نصية تجريبية", AppLanguage.Arabic), CancellationToken.None);
        await sender.SendAsync(new SmsMessage("+10000000001", "Test SMS", AppLanguage.English), CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8);
        Assert.Equal(2, lines.Length);
        Assert.Contains("رسالة نصية تجريبية", lines[0], StringComparison.Ordinal); // not \u-escaped
        var arabic = JsonSerializer.Deserialize<SmsFileRecord>(lines[0], WebJson)!;
        var english = JsonSerializer.Deserialize<SmsFileRecord>(lines[1], WebJson)!;
        Assert.Equal(new SmsFileRecord(clock.GetUtcNow(), "+10000000001", (int)AppLanguage.Arabic, "رسالة نصية تجريبية"), arabic);
        Assert.Equal((int)AppLanguage.English, english.Language);
        Assert.Equal("Test SMS", english.Text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}

public sealed class SmtpEmailSenderTests
{
    [Fact]
    public async Task An_unreachable_relay_is_an_upstream_failure()
    {
        var options = new EmailDeliveryOptions
        {
            Mode = EmailDeliveryMode.Smtp,
            Smtp = new SmtpRelayOptions { Host = "127.0.0.1", Port = 1, TimeoutSeconds = 2 },
        };
        var sender = new SmtpEmailSender(Options.Create(options));

        var exception = await Assert.ThrowsAsync<Codelab.Errors.UpstreamUnavailableException>(() =>
            sender.SendAsync(new EmailMessage("user@example.test", "s", "<p>b</p>"), CancellationToken.None));

        Assert.Equal(SmtpEmailSender.UpstreamName, exception.Upstream);
    }

    [Fact]
    public async Task A_message_reaches_the_relay_with_the_sender_recipient_utf8_subject_and_html_body()
    {
        await using var relay = SmtpStub.Start();
        var sender = new SmtpEmailSender(Options.Create(RelayOptions(relay, enableTls: false)));

        await sender.SendAsync(new EmailMessage("user@example.test", "إشعار تجريبي", "<p>مرحباً &amp; welcome</p>"), CancellationToken.None);

        var received = Assert.Single(relay.Received);
        Assert.Equal("<no-reply@example.test>", received.MailFrom);
        Assert.Equal(["<user@example.test>"], received.Recipients);
        var (headers, body) = FileDropEmailSenderTests.Split(received.Data);
        Assert.Contains("text/html", FileDropEmailSenderTests.Header(headers, "Content-Type"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("إشعار تجريبي", FileDropEmailSenderTests.DecodeWords(FileDropEmailSenderTests.Header(headers, "Subject")));
        Assert.Contains("<p>مرحباً &amp; welcome</p>", FileDropEmailSenderTests.DecodeBody(headers, body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_tls_on_a_relay_that_offers_no_starttls_is_refused_never_sent_in_clear()
    {
        await using var relay = SmtpStub.Start();
        var sender = new SmtpEmailSender(Options.Create(RelayOptions(relay, enableTls: true)));

        await Assert.ThrowsAsync<Codelab.Errors.UpstreamUnavailableException>(() =>
            sender.SendAsync(new EmailMessage("user@example.test", "s", "<p>b</p>"), CancellationToken.None));

        Assert.Empty(relay.Received);
    }

    private static EmailDeliveryOptions RelayOptions(SmtpStub relay, bool enableTls) => new()
    {
        Mode = EmailDeliveryMode.Smtp,
        FromAddress = "no-reply@example.test",
        FromName = "Starter",
        Smtp = new SmtpRelayOptions { Host = SmtpStub.Host, Port = relay.Port, EnableTls = enableTls, TimeoutSeconds = 10 },
    };
}

public sealed class DeliveryOptionsTests
{
    private static readonly IHostEnvironment Development = new NamedEnvironment(Environments.Development);
    private static readonly IHostEnvironment Production = new NamedEnvironment(Environments.Production);
    private static readonly IHostEnvironment Staging = new NamedEnvironment(Environments.Staging);

    [Fact]
    public void The_defaults_are_smtp_without_a_relay_so_a_deployment_must_name_one_and_sms_off()
    {
        var email = new EmailDeliveryOptions();
        var sms = new SmsDeliveryOptions();

        Assert.Equal(EmailDeliveryMode.Smtp, email.Mode);
        Assert.False(email.AllowFileSink);
        Assert.True(new EmailDeliveryOptionsValidator(Production).Validate(name: null, email).Failed); // no Smtp:Host
        Assert.True(SmsValidator(Production).Validate(name: null, sms).Succeeded);
        Assert.False(sms.Enabled);
        Assert.StartsWith(Path.GetTempPath(), email.ResolvedPickupDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(SmsDeliveryOptions.DefaultFileName, sms.ResolvedFilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void The_file_drop_is_valid_in_development()
    {
        Assert.True(new EmailDeliveryOptionsValidator(Development).Validate(name: null, new EmailDeliveryOptions { Mode = EmailDeliveryMode.FileDrop }).Succeeded);
    }

    [Fact]
    public void The_file_drop_outside_development_fails_at_start_naming_the_relay_and_the_opt_in()
    {
        var options = new EmailDeliveryOptions { Mode = EmailDeliveryMode.FileDrop };

        foreach (var environment in new[] { Production, Staging })
        {
            var result = new EmailDeliveryOptionsValidator(environment).Validate(name: null, options);

            Assert.True(result.Failed);
            var failure = Assert.Single(result.Failures!);
            Assert.Contains(nameof(EmailDeliveryOptions.AllowFileSink), failure, StringComparison.Ordinal);
            Assert.Contains(nameof(SmtpRelayOptions.Host), failure, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_test_or_ci_host_may_opt_in_to_the_file_drop_outside_development()
    {
        var options = new EmailDeliveryOptions { Mode = EmailDeliveryMode.FileDrop, AllowFileSink = true };

        Assert.True(new EmailDeliveryOptionsValidator(Production).Validate(name: null, options).Succeeded);
    }

    [Fact]
    public void An_enabled_sms_file_sink_outside_development_fails_at_start_unless_opted_in()
    {
        var production = SmsValidator(Production);

        Assert.True(production.Validate(name: null, new SmsDeliveryOptions { Enabled = true, Mode = SmsDeliveryMode.File }).Failed);
        Assert.True(production.Validate(name: null, new SmsDeliveryOptions { Enabled = false, Mode = SmsDeliveryMode.File }).Succeeded);
        Assert.True(production.Validate(name: null, new SmsDeliveryOptions { Enabled = true, Mode = SmsDeliveryMode.File, AllowFileSink = true }).Succeeded);
        Assert.True(SmsValidator(Development).Validate(name: null, new SmsDeliveryOptions { Enabled = true, Mode = SmsDeliveryMode.File }).Succeeded);
    }

    [Fact]
    public void Smtp_needs_a_host_a_port_and_a_timeout_in_range()
    {
        var options = new EmailDeliveryOptions { Mode = EmailDeliveryMode.Smtp, Smtp = new SmtpRelayOptions { Port = 0, TimeoutSeconds = 0 } };

        var result = new EmailDeliveryOptionsValidator(Development).Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures!.Count());
        options.Smtp = new SmtpRelayOptions { Host = "relay.internal.test" };
        Assert.True(new EmailDeliveryOptionsValidator(Development).Validate(name: null, options).Succeeded);
    }

    [Theory]
    [InlineData(9, "no-reply@example.test", "Starter", null)]
    [InlineData(1, "not an address", "Starter", null)]
    [InlineData(1, "no-reply@example.test", "", null)]
    [InlineData(1, "no-reply@example.test", "Starter", "relative\\folder")]
    public void Bad_email_settings_fail_at_start(int mode, string from, string fromName, string? folder)
    {
        var options = new EmailDeliveryOptions { Mode = (EmailDeliveryMode)mode, FromAddress = from, FromName = fromName, PickupDirectory = folder };

        Assert.True(new EmailDeliveryOptionsValidator(Development).Validate(name: null, options).Failed);
    }

    [Fact]
    public void The_gateway_sms_mode_needs_the_gateway_base_url_while_sms_is_enabled()
    {
        var withoutGateway = SmsValidator(Production);
        var withGateway = SmsValidator(Production, new Uri("https://gateway.example.test/"));
        var enabled = new SmsDeliveryOptions { Enabled = true, Mode = SmsDeliveryMode.Gateway };

        var refused = withoutGateway.Validate(name: null, enabled);

        Assert.True(refused.Failed);
        Assert.Equal(
            "Codelab:Integration:Gateway:BaseUrl is required when Notifications:Sms:Mode is 2 (Gateway) and SMS is enabled: the SMS relay is the integration gateway.",
            Assert.Single(refused.Failures!));
        Assert.True(withGateway.Validate(name: null, enabled).Succeeded);
        Assert.True(withoutGateway.Validate(name: null, new SmsDeliveryOptions { Enabled = false, Mode = SmsDeliveryMode.Gateway }).Succeeded);
    }

    [Fact]
    public void Bad_sms_settings_fail_at_start()
    {
        var validator = SmsValidator(Development);

        Assert.True(validator.Validate(name: null, new SmsDeliveryOptions { Mode = (SmsDeliveryMode)9 }).Failed);
        Assert.True(validator.Validate(name: null, new SmsDeliveryOptions { FilePath = "sms.jsonl" }).Failed);
    }

    [Fact]
    public void The_sms_modes_have_their_contract_values()
    {
        Assert.Equal(1, (int)SmsDeliveryMode.File);
        Assert.Equal(2, (int)SmsDeliveryMode.Gateway);
    }

    internal static SmsDeliveryOptionsValidator SmsValidator(IHostEnvironment environment, Uri? gatewayBaseUrl = null) =>
        new(environment, Options.Create(new GatewayOptions { BaseUrl = gatewayBaseUrl }));
}

/// <summary>A host environment with only a name (the validators read nothing else).</summary>
internal sealed class NamedEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "Starter.Tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
