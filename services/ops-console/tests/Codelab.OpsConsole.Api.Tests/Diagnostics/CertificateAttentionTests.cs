using Codelab.OpsConsole.Api.Diagnostics;
using Codelab.OpsConsole.Application.Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Codelab.OpsConsole.Api.Tests.Diagnostics;

public sealed class CertificateAttentionTests
{
    [Fact]
    public void Only_certificates_that_are_not_ok_are_logged_with_the_integer_status_the_seq_signal_matches()
    {
        var logger = new FakeLogger();
        CertificateExpiry[] certificates =
        [
            new() { Name = "tls", Status = CertificateStatus.Ok, DaysLeft = 200 },
            new() { Name = "idp-signing-1", Status = CertificateStatus.Warning, DaysLeft = 21 },
            new() { Name = "idp-signing-2", Status = CertificateStatus.Critical, Code = "OPS_CERTIFICATE_UNREADABLE" },
        ];

        CertificateAttention.Log(logger, certificates);

        var records = logger.Collector.GetSnapshot();
        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal(LogLevel.Warning, record.Level));
        Assert.Equal("CertificateNeedsAttention", records[0].Id.Name);
        Assert.Equal("2", Property(records[0], "CertificateStatus"));
        Assert.Equal("idp-signing-1", Property(records[0], "CertificateName"));
        Assert.Equal("3", Property(records[1], "CertificateStatus"));
    }

    private static string? Property(FakeLogRecord record, string name) =>
        record.StructuredState!.Single(pair => pair.Key == name).Value;
}
