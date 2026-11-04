using Codelab.Testing.Checks;

namespace Codelab.Testing.Tests.Checks;

public sealed class CheckResultTests
{
    [Fact]
    public void A_result_with_items_and_no_violation_succeeds()
    {
        var result = new CheckResult("title", [], examined: 3);

        Assert.True(result.IsSuccessful);
        result.ThrowIfFailed();
    }

    [Fact]
    public void Violations_fail_and_the_message_lists_each_one()
    {
        var result = new CheckResult("Every endpoint has a permission", ["GET /a: missing", "GET /b: missing"], examined: 2);

        var exception = Assert.Throws<ContractViolationException>(result.ThrowIfFailed);

        Assert.Same(result, exception.Result);
        Assert.Equal(["Every endpoint has a permission", "  GET /a: missing", "  GET /b: missing"], exception.Message.Split(Environment.NewLine));
    }

    [Fact]
    public void Examining_nothing_fails_as_vacuous()
    {
        var result = new CheckResult("title", [], examined: 0);

        Assert.True(result.IsVacuous);
        Assert.False(result.IsSuccessful);
        Assert.Contains("examined nothing", Assert.Throws<ContractViolationException>(result.ThrowIfFailed).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Results_combine_with_their_titles_and_counts()
    {
        var combined = CheckResult.Combine("all", [new CheckResult("a", ["x"], 2), new CheckResult("b", [], 0)]);

        Assert.Equal(["a: x"], combined.Violations);
        Assert.Equal(2, combined.Examined);
    }

    [Fact]
    public void Arguments_are_guarded()
    {
        Assert.Throws<ArgumentException>(() => new CheckResult(" ", [], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CheckResult("t", [], -1));
        Assert.Throws<ArgumentNullException>(() => new ContractViolationException(null!));
    }
}
