using System.Text.RegularExpressions;

namespace Codelab.Identity.Tests;

/// <summary>
/// Source guards over the package: no certificate-validation bypass anywhere, and exactly one TLS callback, which
/// returns the platform's own verdict.
/// </summary>
public sealed partial class PackageSourceTests
{
    private const string SolutionFileName = "codelab-starter.slnx";
    private const string PackageFolder = "packages/dotnet/Codelab.Identity";

    [Fact]
    public void No_source_bypasses_certificate_validation()
    {
        var offenders = Sources().Where(file => Bypass().IsMatch(File.ReadAllText(file))).Select(Path.GetFileName).ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_only_certificate_callback_returns_the_platform_verdict()
    {
        var callbacks = Sources().Where(file => File.ReadAllText(file).Contains("new SslStream(", StringComparison.Ordinal)).Select(Path.GetFileName).ToList();

        Assert.Equal(["TlsEndpointInspector.cs"], callbacks);
        Assert.Contains("return errors == SslPolicyErrors.None;", File.ReadAllText(Sources().Single(file => file.EndsWith("TlsEndpointInspector.cs", StringComparison.Ordinal))), StringComparison.Ordinal);
    }

    [Fact]
    public void The_guard_pattern_catches_the_shapes_it_exists_for()
    {
        Assert.Matches(Bypass(), "handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAccept" + "AnyServerCertificateValidator;");
        Assert.Matches(Bypass(), "connection.SessionOptions.VerifyServerCertificate = (_, _) => tr" + "ue;");
    }

    private static IEnumerable<string> Sources()
    {
        var folder = Path.Combine(Root(), PackageFolder);
        return Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment is "bin" or "obj"));
    }

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No {SolutionFileName} above {AppContext.BaseDirectory}.");
    }

    [GeneratedRegex(@"ServerCertificateCustomValidationCallback|DangerousAcceptAny|VerifyServerCertificate\s*=|RemoteCertificateValidationCallback\s*=")]
    private static partial Regex Bypass();
}
