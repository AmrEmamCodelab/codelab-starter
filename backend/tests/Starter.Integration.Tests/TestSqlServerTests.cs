using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests;

/// <summary>Unit tests of the database guard: the fixtures may only ever create or drop a test database.</summary>
public sealed class TestSqlServerTests
{
    [Theory]
    [InlineData("StarterIt_0123456789abcdef0123456789abcdef")]
    [InlineData("ForkIt_abc")]
    public void ATestDatabaseNameIsAccepted(string name) => Assert.Equal(name, TestSqlServer.RequireTestName(name));

    [Theory]
    [InlineData("StarterDev")]
    [InlineData("master")]
    [InlineData("StarterIt_x]; DROP DATABASE [StarterDev")]
    [InlineData("StarterIt_x'--")]
    [InlineData(" ")]
    public void AnyOtherNameIsRefused(string name) => Assert.ThrowsAny<ArgumentException>(() => TestSqlServer.RequireTestName(name));

    [Fact]
    public void TheConnectionStringNamesTheDatabaseOnTheConfiguredServer()
    {
        var connection = TestSqlServer.ConnectionStringFor("StarterIt_abc");

        Assert.Contains("StarterIt_abc", connection, StringComparison.Ordinal);
        Assert.Contains(TestSqlServer.Server, connection, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, TestSqlServer.DefaultServer)]
    [InlineData(" ", TestSqlServer.DefaultServer)]
    [InlineData(@"(localdb)\Other", @"(localdb)\Other")]
    [InlineData(@"Server=(localdb)\Other;Integrated Security=true;TrustServerCertificate=true", @"(localdb)\Other")]
    public void TheServerComesFromANameOrAConnectionString(string? value, string expected) =>
        Assert.Equal(expected, TestSqlServer.ServerFrom(value));
}
