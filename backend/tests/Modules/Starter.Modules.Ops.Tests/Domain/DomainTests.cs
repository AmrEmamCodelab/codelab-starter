using Starter.Modules.Ops.Domain;
using Starter.Modules.Ops.Domain.Certificates;
using Starter.Modules.Ops.Domain.Health;

namespace Starter.Modules.Ops.Tests.Domain;

public sealed class ReferenceCodeFormatTests
{
    [Theory]
    [InlineData("9C1E5-B7A20", "9C1E5-B7A20")]
    [InlineData("9c1e5-b7a20", "9C1E5-B7A20")]
    [InlineData("  9c1e5-b7a20 ", "9C1E5-B7A20")]
    public void A_well_formed_code_is_normalized_to_uppercase(string raw, string expected) =>
        Assert.Equal(expected, ReferenceCodeFormat.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9C1E5B7A20")]
    [InlineData("9C1E5_B7A20")]
    [InlineData("9C1E5-B7A2G")]
    [InlineData("9C1E5-B7A201")]
    [InlineData("9C1E-5B7A20")]
    [InlineData("'; DROP--x")]
    public void Anything_else_is_refused(string? raw) => Assert.Null(ReferenceCodeFormat.Normalize(raw));

    [Fact]
    public void It_matches_what_Codelab_Observability_mints()
    {
        var code = Codelab.Observability.ReferenceCode.From("9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b");

        Assert.Equal(code, ReferenceCodeFormat.Normalize(code));
        Assert.Equal(OpsLimits.ReferenceCodeLength, code.Length);
    }
}

public sealed class OperatorNameTests
{
    [Theory]
    [InlineData("CORP\\jdoe", "CORP\\jdoe")]
    [InlineData("  jdoe@corp.example  ", "jdoe@corp.example")]
    public void A_usable_name_is_trimmed(string raw, string expected) => Assert.Equal(expected, OperatorName.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("jdoe\r\nX-Injected: 1")]
    [InlineData("jd\u0000oe")]
    public void A_missing_blank_or_control_character_name_is_refused(string? raw) => Assert.Null(OperatorName.Normalize(raw));

    [Fact]
    public void A_name_longer_than_the_limit_is_refused()
    {
        Assert.NotNull(OperatorName.Normalize(new string('a', OpsLimits.OperatorMaxLength)));
        Assert.Null(OperatorName.Normalize(new string('a', OpsLimits.OperatorMaxLength + 1)));
    }
}

public sealed class OperatorAccessTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Record_keeps_every_value()
    {
        var access = OperatorAccess.Record(new OperatorAccessDetails("CORP\\jdoe", "starter-ops-console", OpsEndpoint.ReferenceCode, "9C1E5-B7A20", "abc", At));

        Assert.Equal("CORP\\jdoe", access.Operator);
        Assert.Equal("starter-ops-console", access.CallerService);
        Assert.Equal(OpsEndpoint.ReferenceCode, access.Endpoint);
        Assert.Equal("9C1E5-B7A20", access.Subject);
        Assert.Equal("abc", access.CorrelationId);
        Assert.Equal(At, access.At);
    }

    [Fact]
    public void A_long_correlation_id_is_cut_to_the_column()
    {
        var access = OperatorAccess.Record(new OperatorAccessDetails("op", "svc", OpsEndpoint.Health, null, new string('c', 100), At));

        Assert.Equal(OpsLimits.CorrelationIdMaxLength, access.CorrelationId!.Length);
    }

    [Theory]
    [InlineData("", "svc", OpsEndpoint.Health)]
    [InlineData("op", "", OpsEndpoint.Health)]
    [InlineData("op", "svc", (OpsEndpoint)99)]
    public void A_missing_value_or_an_unknown_endpoint_is_refused(string operatorName, string service, OpsEndpoint endpoint) =>
        Assert.Throws<ArgumentException>(() => OperatorAccess.Record(new OperatorAccessDetails(operatorName, service, endpoint, null, null, At)));

    [Fact]
    public void Endpoint_values_are_explicit_and_stable()
    {
        Assert.Equal(1, (int)OpsEndpoint.Health);
        Assert.Equal(2, (int)OpsEndpoint.Jobs);
        Assert.Equal(3, (int)OpsEndpoint.Certificates);
        Assert.Equal(4, (int)OpsEndpoint.ReferenceCode);
    }
}

public sealed class ReferenceCodeSightingTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void From_normalizes_the_code_and_cuts_long_values()
    {
        var sighting = ReferenceCodeSighting.From(new ReferenceCodeSightingDetails("9c1e5-b7a20", "9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b", "PROPFIND-EXTENDED-METHOD", "/" + new string('p', 600), 409, At))!;

        Assert.Equal("9C1E5-B7A20", sighting.ReferenceCode);
        Assert.Equal(OpsLimits.MethodMaxLength, sighting.Method.Length);
        Assert.Equal(OpsLimits.PathMaxLength, sighting.Path.Length);
        Assert.Equal(409, sighting.StatusCode);
        Assert.Equal(At, sighting.OccurredAt);
    }

    [Theory]
    [InlineData("not-a-code", "9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b")]
    [InlineData("9C1E5-B7A20", "")]
    public void A_bad_code_or_a_missing_correlation_id_is_not_a_sighting(string code, string correlationId) =>
        Assert.Null(ReferenceCodeSighting.From(new ReferenceCodeSightingDetails(code, correlationId, "GET", "/", 500, At)));
}

public sealed class CertificateExpiryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly CertificateThresholds Thresholds = new(WarningDays: 30, CriticalDays: 7);

    [Theory]
    [InlineData(400.0, CertificateStatus.Ok, 400)]
    [InlineData(30.0, CertificateStatus.Ok, 30)]
    [InlineData(29.99, CertificateStatus.Warning, 29)]
    [InlineData(7.0, CertificateStatus.Warning, 7)]
    [InlineData(6.99, CertificateStatus.Critical, 6)]
    [InlineData(0.01, CertificateStatus.Critical, 0)]
    [InlineData(0.0, CertificateStatus.Expired, 0)]
    [InlineData(-3.5, CertificateStatus.Expired, -4)]
    public void Days_left_round_down_and_pick_the_status(double daysLeft, CertificateStatus expected, int expectedDays)
    {
        var verdict = CertificateExpiry.Evaluate(Now.AddDays(daysLeft), Now, Thresholds);

        Assert.Equal(expected, verdict.Status);
        Assert.Equal(expectedDays, verdict.DaysLeft);
    }

    [Fact]
    public void An_unreadable_certificate_is_critical_without_days() =>
        Assert.Equal(new CertificateVerdict(CertificateStatus.Critical, DaysLeft: null), CertificateExpiry.Unreadable);

    [Theory]
    [InlineData(30, 7, true)]
    [InlineData(365, 1, true)]
    [InlineData(7, 7, false)]
    [InlineData(30, 0, false)]
    [InlineData(366, 7, false)]
    public void Thresholds_need_one_to_critical_below_warning_up_to_a_year(int warning, int critical, bool valid) =>
        Assert.Equal(valid, new CertificateThresholds(WarningDays: warning, CriticalDays: critical).IsValid);

    [Fact]
    public void Status_values_are_the_contract()
    {
        Assert.Equal(1, (int)CertificateStatus.Ok);
        Assert.Equal(2, (int)CertificateStatus.Warning);
        Assert.Equal(3, (int)CertificateStatus.Critical);
        Assert.Equal(4, (int)CertificateStatus.Expired);
    }
}

public sealed class HealthRollupTests
{
    [Fact]
    public void The_system_status_is_the_worst_component()
    {
        Assert.Equal(ComponentStatus.Healthy, HealthRollup.Overall([]));
        Assert.Equal(ComponentStatus.Healthy, HealthRollup.Overall([ComponentStatus.Healthy, ComponentStatus.Healthy]));
        Assert.Equal(ComponentStatus.Degraded, HealthRollup.Overall([ComponentStatus.Healthy, ComponentStatus.Degraded]));
        Assert.Equal(ComponentStatus.Unhealthy, HealthRollup.Overall([ComponentStatus.Unhealthy, ComponentStatus.Degraded]));
    }

    [Fact]
    public void A_ready_answer_slower_than_half_the_timeout_is_degraded()
    {
        var timeout = TimeSpan.FromSeconds(4);

        Assert.Equal(ComponentStatus.Healthy, HealthRollup.ForReadyAnswer(TimeSpan.FromSeconds(2), timeout));
        Assert.Equal(ComponentStatus.Degraded, HealthRollup.ForReadyAnswer(TimeSpan.FromSeconds(2.1), timeout));
    }

    [Fact]
    public void Status_values_are_explicit()
    {
        Assert.Equal(1, (int)ComponentStatus.Healthy);
        Assert.Equal(2, (int)ComponentStatus.Degraded);
        Assert.Equal(3, (int)ComponentStatus.Unhealthy);
    }
}
