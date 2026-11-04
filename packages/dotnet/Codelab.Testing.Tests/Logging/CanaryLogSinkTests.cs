using Codelab.Testing.Checks;
using Codelab.Testing.Logging;
using Serilog;

namespace Codelab.Testing.Tests.Logging;

public sealed class CanaryLogSinkTests
{
    private readonly string _email = CanaryLogSink.NewCanary("email");
    private readonly string _name = CanaryLogSink.NewCanary("name");

    [Fact]
    public void Masked_logs_pass()
    {
        var sink = new CanaryLogSink([_email, _name]);
        using (var logger = Logger(sink))
        {
            logger.Information("Record {RecordRef} saved for {UserHash}", "r-1", "u-hash");
        }

        Assert.True(sink.Check().IsSuccessful);
        Assert.Single(sink.Events);
    }

    [Fact]
    public void A_canary_in_a_property_is_reported_without_repeating_it()
    {
        var sink = new CanaryLogSink([_email, _name]);
        using (var logger = Logger(sink))
        {
            logger.Information("Signed in {Email}", _email);
        }

        var leaks = sink.FindLeaks();

        Assert.Contains(leaks, leak => leak.Location == CanaryLeak.PropertyLocation + "Email" && leak.CanaryIndex == 0);
        Assert.Contains(leaks, leak => leak.Location == CanaryLeak.MessageLocation);
        Assert.DoesNotContain(_email, sink.Check().Describe(), StringComparison.Ordinal);
        Assert.Throws<ContractViolationException>(sink.Check().ThrowIfFailed);
    }

    [Fact]
    public void Canaries_are_found_inside_structures_templates_and_exceptions()
    {
        var sink = new CanaryLogSink([_email, _name]);
        using (var logger = Logger(sink))
        {
            logger.Information("Profile {@Profile}", new { Contact = new { Address = _email } });
            logger.Information("Welcome " + _name);
            logger.Error(new InvalidOperationException($"duplicate {_email}"), "Save failed");
        }

        var locations = sink.FindLeaks().Select(leak => (leak.EventIndex, leak.Location)).ToList();

        Assert.Contains((0, CanaryLeak.PropertyLocation + "Profile"), locations);
        Assert.Contains((1, CanaryLeak.TemplateLocation), locations);
        Assert.Contains((2, CanaryLeak.ExceptionLocation), locations);
    }

    [Fact]
    public void Nothing_logged_is_vacuous_and_canaries_are_required()
    {
        var sink = new CanaryLogSink([_email]);

        Assert.True(sink.Check().IsVacuous);
        Assert.Throws<ArgumentException>(() => new CanaryLogSink([string.Empty]));
        Assert.StartsWith("canary-email-", _email, StringComparison.Ordinal);
        Assert.NotEqual(_email, CanaryLogSink.NewCanary("email"));
    }

    private static Serilog.Core.Logger Logger(CanaryLogSink sink) =>
        new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
}
