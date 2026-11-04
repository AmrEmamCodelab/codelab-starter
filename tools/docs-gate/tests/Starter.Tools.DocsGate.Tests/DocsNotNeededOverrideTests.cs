using Starter.Tools.DocsGate.Checks;

namespace Starter.Tools.DocsGate.Tests;

public sealed class DocsNotNeededOverrideTests
{
    [Fact]
    public void The_environment_reason_wins()
    {
        var reason = DocsNotNeededOverride.Resolve(
            Environment(reason: "  from build.ps1  ", prDescription: "Docs-Not-Needed: from PR"),
            latestCommitMessage: "Docs-Not-Needed: from commit");

        Assert.Equal("from build.ps1", reason);
    }

    [Fact]
    public void The_pr_description_trailer_is_read()
    {
        var reason = DocsNotNeededOverride.Resolve(
            Environment(reason: null, prDescription: "## What\nRefactor.\n\nDocs-Not-Needed: internal rename\n"),
            latestCommitMessage: null);

        Assert.Equal("internal rename", reason);
    }

    [Fact]
    public void The_commit_message_trailer_is_read()
    {
        var reason = DocsNotNeededOverride.Resolve(
            Environment(reason: null, prDescription: null),
            latestCommitMessage: "Tidy names\r\n\r\ndocs-not-needed: whitespace only\r\n");

        Assert.Equal("whitespace only", reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Docs-Not-Needed:")]
    [InlineData("Docs-Not-Needed:    ")]
    [InlineData("No trailer here")]
    public void No_reason_means_no_override(string? commitMessage)
    {
        var reason = DocsNotNeededOverride.Resolve(Environment(reason: "   ", prDescription: null), commitMessage);

        Assert.Null(reason);
    }

    private static Func<string, string?> Environment(string? reason, string? prDescription) =>
        name => name switch
        {
            DocsNotNeededOverride.ReasonEnvironmentVariable => reason,
            DocsNotNeededOverride.PrDescriptionEnvironmentVariable => prDescription,
            _ => null,
        };
}
