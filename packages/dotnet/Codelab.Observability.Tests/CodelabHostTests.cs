using Microsoft.Extensions.Hosting;

namespace Codelab.Observability.Tests;

/// <summary>A host that fails to start leaves one Fatal in a node-local startup file and exits with the failure code.</summary>
public sealed class CodelabHostTests
{
    private static readonly ObservabilityOptions Options = new()
    {
        ServiceName = "starter-host-test",
        HostAssembly = typeof(CodelabHostTests).Assembly,
    };

    [Fact]
    public void A_build_failure_is_logged_once_and_returns_the_failure_exit_code()
    {
        var directory = Directory.CreateTempSubdirectory("codelab-host-test-");
        try
        {
            string[] args = [$"--Observability:LogFile:Directory={directory.FullName}"];

            var exit = CodelabHost.RunOwningProcess<IHost>(
                args, static _ => throw new InvalidOperationException("The duplicate key value is (jane@example.test)."), Options);

            Assert.Equal(CodelabHost.StartupFailureExitCode, exit);
            var file = Assert.Single(directory.GetFiles("starter-host-test-startup-*.json"));
            var text = File.ReadAllText(file.FullName);
            Assert.Contains("failed to start", text, StringComparison.Ordinal);
            Assert.Contains(LogPropertyNames.ErrorFingerprint, text, StringComparison.Ordinal);
            Assert.DoesNotContain("jane@example.test", text, StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_host_aborted_by_a_design_time_tool_is_not_a_failure()
    {
        Assert.Throws<HostAbortedException>(
            () => CodelabHost.RunOwningProcess<IHost>([], static _ => throw new HostAbortedException(), Options));
    }

    [Fact]
    public void Outside_the_owning_process_a_failure_propagates()
    {
        Assert.Throws<InvalidOperationException>(
            () => CodelabHost.Run<IHost>([], static _ => throw new InvalidOperationException("boom"), Options));
    }

    [Fact]
    public void Startup_configuration_reads_the_command_line()
    {
        var configuration = CodelabHost.StartupConfiguration(["--Observability:LogFile:Directory=X:/logs"]);

        Assert.Equal("X:/logs", ObservabilitySettings.From(configuration).LogFile.ResolvedDirectory);
    }

    [Fact]
    public void Release_is_the_informational_version_or_unknown()
    {
        Assert.NotEqual(ReleaseVersion.Unknown, ReleaseVersion.Of(typeof(CodelabHostTests).Assembly));
        Assert.Equal(TestHostsLogFile, LoggerPipeline.LogFileName("svc"));
    }

    private const string TestHostsLogFile = "svc-.json";
}
