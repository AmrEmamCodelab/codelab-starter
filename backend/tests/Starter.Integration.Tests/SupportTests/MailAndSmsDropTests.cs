using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests.SupportTests;

/// <summary>The <c>.eml</c> and SMS-file readers the phase 5 tests use (no database).</summary>
public sealed class MailAndSmsDropTests : IDisposable
{
    private const string ArabicSubject = "تفعيل حسابك";
    private const string Link = "http://localhost:4280/activate#ticket=abc&amp;x=1";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), Phase5Host.DropFolderPrefix + "unit-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task A_mail_written_by_the_pickup_folder_sender_is_read_back_decoded()
    {
        Directory.CreateDirectory(_folder);
        using (var mail = new MailMessage(new MailAddress("no-reply@example.test", "Starter"), new MailAddress("someone@example.test")))
        {
            mail.Subject = ArabicSubject;
            mail.SubjectEncoding = Encoding.UTF8;
            mail.Body = $"<div dir=\"rtl\"><p>مرحباً</p><p><a href=\"{Link}\">تفعيل</a></p></div>";
            mail.BodyEncoding = Encoding.UTF8;
            mail.IsBodyHtml = true;
            using var client = new SmtpClient { DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory, PickupDirectoryLocation = _folder };
            await client.SendMailAsync(mail);
        }

        var dropped = Assert.Single(new MailDrop(_folder).To("SOMEONE@example.test"));

        Assert.Equal("someone@example.test", dropped.To);
        Assert.Equal(ArabicSubject, dropped.Subject);
        Assert.Contains("مرحباً", dropped.Body, StringComparison.Ordinal);
        Assert.Equal("http://localhost:4280/activate#ticket=abc&x=1", dropped.FirstLink);
    }

    [Fact]
    public void A_missing_pickup_folder_means_no_mail() => Assert.Empty(new MailDrop(_folder).ReadAll());

    [Theory]
    [InlineData("=?utf-8?B?2KrZgdi52YrZhA==?=", "تفعيل")]
    [InlineData("=?utf-8?Q?caf=C3=A9_au_lait?=", "café au lait")]
    [InlineData("=?utf-8?B?2KrZgQ==?= =?utf-8?B?2LnZitmE?=", "تفعيل")]
    [InlineData("plain subject", "plain subject")]
    public void Encoded_words_are_decoded(string header, string expected) => Assert.Equal(expected, EmlReader.DecodeWords(header));

    [Fact]
    public void Quoted_printable_bodies_are_decoded_with_soft_line_breaks()
    {
        var body = "caf=C3=A9 =\r\nau lait";

        Assert.Equal("café au lait", EmlReader.DecodeBody(body, "quoted-printable"));
    }

    [Fact]
    public void Folded_headers_are_joined_and_an_angle_address_is_unwrapped()
    {
        var eml = "To: \"Some One\"\r\n <someone@example.test>\r\nSubject: hello\r\n\r\nbody";

        var mail = EmlReader.Parse(eml, "a.eml");

        Assert.Equal("someone@example.test", mail.To);
        Assert.Equal("hello", mail.Subject);
        Assert.Equal("body", mail.Body);
        Assert.Null(mail.FirstLink);
    }

    [Fact]
    public void A_message_without_a_body_separator_or_a_recipient_is_refused()
    {
        Assert.Throws<FormatException>(() => EmlReader.Parse("To: a@example.test", "a.eml"));
        Assert.Throws<FormatException>(() => EmlReader.Parse("Subject: x\r\n\r\nbody", "a.eml"));
    }

    [Fact]
    public void Sms_json_lines_are_read_in_order_and_filtered_by_number()
    {
        var lines = "{\"at\":\"2026-11-06T10:00:00+00:00\",\"to\":\"+9665001\",\"language\":1,\"text\":\"رسالة\"}\n"
            + "\n{\"at\":\"2026-11-06T10:01:00+00:00\",\"to\":\"+9665002\",\"language\":2,\"text\":\"Message\"}\n";
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "sms.jsonl");
        File.WriteAllText(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var all = new SmsDrop(path).ReadAll();
        var english = Assert.Single(new SmsDrop(path).To("+9665002"));

        Assert.Equal(["رسالة", "Message"], all.Select(sms => sms.Text));
        Assert.Equal(2, english.Language);
        Assert.Throws<JsonException>(() => SmsDrop.Parse("null"));
        Assert.Empty(new SmsDrop(Path.Combine(_folder, "none.jsonl")).ReadAll());
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
