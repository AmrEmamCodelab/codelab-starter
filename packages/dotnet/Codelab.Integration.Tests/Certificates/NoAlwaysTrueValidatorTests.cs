using System.Text.RegularExpressions;
using Codelab.Integration.Tests.Support;

namespace Codelab.Integration.Tests.Certificates;

/// <summary>
/// A source guard over the package: no certificate callback that accepts everything, and exactly one place that sets
/// a callback at all (ServerCertificatePolicy), so a reviewer has one file to read.
/// </summary>
public sealed partial class NoAlwaysTrueValidatorTests
{
    private const string PolicyFileName = "ServerCertificatePolicy.cs";

    [Fact]
    public void The_package_source_contains_no_accept_everything_certificate_validator()
    {
        var offenders = PackageSources()
            .SelectMany(file => File.ReadAllLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(entry => AcceptEverything().IsMatch(entry.line))
            .Select(entry => $"{Path.GetFileName(entry.file)}:{entry.number}: {entry.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "Always-true certificate validation found:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Only_the_certificate_policy_sets_a_validation_callback()
    {
        var setters = PackageSources()
            .Where(file => CallbackAssignment().IsMatch(File.ReadAllText(file)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal([PolicyFileName], setters);
    }

    [Fact]
    public void The_guard_patterns_catch_the_shapes_they_exist_for()
    {
        Assert.Matches(AcceptEverything(), "handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => tr" + "ue;");
        Assert.Matches(AcceptEverything(), "ServerCertificateCustomValidationCallback = HttpClientHandler.Danger" + "ousAcceptAnyServerCertificateValidator");
        Assert.Matches(AcceptEverything(), "(sender, cert, chain, errors) => true");
        Assert.DoesNotMatch(AcceptEverything(), "return (_, certificate, chain, errors) => validator.Validate(certificate, chain, errors);");
    }

    private static IEnumerable<string> PackageSources()
    {
        var packageDirectory = Path.Combine(RepositoryPaths.Root, "packages", "dotnet", "Codelab.Integration");
        return Directory.EnumerateFiles(packageDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !RepositoryPaths.IsBuildOutput(file));
    }

    [GeneratedRegex(@"DangerousAcceptAny|ServerCertificateCustomValidationCallback|\(\s*\w+\s*,\s*\w+\s*,\s*\w+\s*,\s*\w+\s*\)\s*=>\s*true\b")]
    private static partial Regex AcceptEverything();

    [GeneratedRegex(@"RemoteCertificateValidationCallback\s*=")]
    private static partial Regex CallbackAssignment();
}
