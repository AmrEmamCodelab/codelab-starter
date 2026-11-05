using Microsoft.Data.SqlClient;

namespace Codelab.Security.Tests.TestSupport;

/// <summary>
/// Reads <see cref="ServerVariable"/> the same way the integration tier does: the value may be a server name
/// (<c>(localdb)\P2Proof</c>, <c>myserver\instance</c>) or a connection string (any value containing <c>=</c>).
/// Empty means <see cref="DefaultServer"/>, the same default as the integration tier, so one setting serves both.
/// </summary>
public static class SqlTestServer
{
    /// <summary>The environment variable that names the test SQL Server.</summary>
    public const string ServerVariable = "CODELAB_TEST_SQL";

    /// <summary>The server used when <see cref="ServerVariable"/> is not set (the integration tier's default too).</summary>
    public const string DefaultServer = @"(localdb)\MSSQLLocalDB";

    private const char ConnectionStringSeparator = '=';

    /// <summary>The connection string builder for the configured server, from the environment.</summary>
    public static SqlConnectionStringBuilder FromEnvironment() => From(Environment.GetEnvironmentVariable(ServerVariable));

    /// <summary>
    /// The connection string builder for <paramref name="value"/>: a connection string is used as given; a server name
    /// (or an empty value, meaning <see cref="DefaultServer"/>) gets Windows authentication and a trusted certificate.
    /// </summary>
    public static SqlConnectionStringBuilder From(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Contains(ConnectionStringSeparator, StringComparison.Ordinal))
        {
            return new SqlConnectionStringBuilder(value);
        }

        return new SqlConnectionStringBuilder
        {
            DataSource = string.IsNullOrWhiteSpace(value) ? DefaultServer : value.Trim(),
            IntegratedSecurity = true,
            TrustServerCertificate = true,
        };
    }
}
