using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Codelab.Security.PublicIds;
using Codelab.Security.Signing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Starter.Application.Authorization;
using Starter.Application.Users;
using Starter.Domain.Attachments;
using Starter.Modules.ServiceRequests.Api;
using Starter.Modules.ServiceRequests.Application;
using Starter.Tests.Hosting;

namespace Starter.Modules.ServiceRequests.Tests.Http;

/// <summary>
/// The real host (Development, so it migrates on start) on a throwaway SQL Server database <c>p7_sr_&lt;guid&gt;</c>,
/// created before the start and dropped afterwards, also when the start fails. The server is
/// <see cref="ServerVariable"/> (a server name or a connection string), else LocalDB. Only that database is ever
/// dropped. It needs <c>Starter.Api</c> to reference the Records module and this sample.
/// </summary>
public sealed class ServiceRequestsHttpHost : IAsyncLifetime, IDisposable
{
    /// <summary>The variable naming the test SQL Server.</summary>
    public const string ServerVariable = "CODELAB_TEST_SQL";

    /// <summary>The prefix of every database this host creates (and the only ones it drops).</summary>
    public const string DatabasePrefix = "p7_sr_";

    private const string DefaultServer = @"(localdb)\MSSQLLocalDB";
    private const char ConnectionStringSeparator = '=';
    private const string MasterDatabase = "master";
    private const string BearerScheme = "Bearer";
    private const string LoginPath = "/api/auth/login";
    private const string AccessTokenProperty = "accessToken";
    private const string AttachmentsTable = "[attachments].[RecordAttachments]";
    private const string PdfContentType = "application/pdf";
    private const long FakeFileSize = 1024;

    private StarterApiFactory? _factory;

    /// <summary>The database of this host.</summary>
    public string DatabaseName { get; } = DatabasePrefix + Guid.NewGuid().ToString("N");

    /// <summary>The generated accounts (the break-glass administrator holds every permission).</summary>
    public StarterTestAccounts Accounts { get; } = StarterTestAccounts.Generate();

    private StarterApiFactory Factory => _factory ?? throw new InvalidOperationException("The host has not started.");

    private static string Server
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(ServerVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return DefaultServer;
            }

            return value.Contains(ConnectionStringSeparator, StringComparison.Ordinal) ? new SqlConnectionStringBuilder(value).DataSource : value;
        }
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await ExecuteOnMasterAsync($"CREATE DATABASE [{DatabaseName}]");
        try
        {
            var options = new StarterHostOptions(
                ConnectionString: ConnectionStringFor(DatabaseName),
                UsesDatabase: true,
                ApplyMigrationsOnStartup: true,
                SigningMode: SecurityEnforcementMode.Off);
            _factory = new StarterApiFactory(Environments.Development, StarterHostSettings.For(options, Accounts));
            _ = _factory.Services;
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        try
        {
            if (_factory is not null)
            {
                await _factory.DisposeAsync();
                _factory = null;
            }
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await ExecuteOnMasterAsync(
                $"IF DB_ID(N'{DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]; END");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _factory?.Dispose();
        _factory = null;
    }

    /// <summary>A client signed in as the break-glass administrator.</summary>
    public Task<HttpClient> AdministratorClientAsync() => SignedInClientAsync(Accounts.BreakGlassUserName, Accounts.BreakGlassPassword);

    private async Task<HttpClient> SignedInClientAsync(string userName, string password)
    {
        var client = Factory.CreateClient();
        using var login = await client.PostAsJsonAsync(LoginPath, new LoginRequest(userName, password));
        login.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(BearerScheme, body.RootElement.GetProperty(AccessTokenProperty).GetString());
        return client;
    }

    /// <summary>
    /// A client signed in as the Development user, a plain requester granted only the sample's requester keys
    /// (<see cref="ServiceRequestsPermissionKeys.Create"/>, <see cref="ServiceRequestsPermissionKeys.ViewOwn"/>) first.
    /// </summary>
    public async Task<HttpClient> RequesterClientAsync()
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountAdministration>();
            var page = await accounts.ListAsync(new AccountQuery(Accounts.DevUserName, null, null, null, Page: 1, PageSize: 1), CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IUserPermissionGrants>().EnsureGrantedAsync(
                Assert.Single(page.Items).UserId,
                [ServiceRequestsPermissionKeys.Create, ServiceRequestsPermissionKeys.ViewOwn],
                CancellationToken.None);
        }

        return await SignedInClientAsync(Accounts.DevUserName, Accounts.DevUserPassword);
    }

    /// <summary>
    /// Links a (fake) file of the required request document type to the record, straight in the attachments table, as
    /// a receipt-verified upload would: the HTTP upload path is the Attachments module's and is proven there.
    /// </summary>
    public async Task LinkRequiredDocumentAsync(string recordToken)
    {
        var codec = Factory.Services.GetRequiredService<IPublicIdCodec>();
        if (!codec.TryUnprotect(ServiceRequestRecordIds.DomainLabel, recordToken, out var recordId))
        {
            throw new InvalidOperationException($"The record token {recordToken} is not a record id.");
        }

        await using var connection = new SqlConnection(ConnectionStringFor(DatabaseName));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO {AttachmentsTable} (RecordType, RecordId, ServiceAttachmentId, FileId, FileName, SizeBytes, Sha256Hex, ContentType, UploadedBySubject, UploadedAt) "
            + "VALUES (@recordType, @recordId, @typeId, @fileId, @fileName, @size, @sha, @contentType, @subject, SYSDATETIMEOFFSET())";
        command.Parameters.AddWithValue("@recordType", AttachableRecordTypes.ServiceRequest);
        command.Parameters.AddWithValue("@recordId", recordId);
        command.Parameters.AddWithValue("@typeId", AttachmentTypeIds.ServiceRequestDocument);
        command.Parameters.AddWithValue("@fileId", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("@fileName", "request.pdf");
        command.Parameters.AddWithValue("@size", FakeFileSize);
        command.Parameters.AddWithValue("@sha", Convert.ToHexStringLower(SHA256.HashData(Guid.NewGuid().ToByteArray())));
        command.Parameters.AddWithValue("@contentType", PdfContentType);
        command.Parameters.AddWithValue("@subject", Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }

    private sealed record LoginRequest(string UserName, string Password);

    private static string ConnectionStringFor(string database) =>
        new SqlConnectionStringBuilder
        {
            DataSource = Server,
            InitialCatalog = database,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
        }.ConnectionString;

    private static async Task ExecuteOnMasterAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionStringFor(MasterDatabase));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
