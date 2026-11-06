using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Codelab.Security.PublicIds;
using Starter.Application.Records;
using Starter.Integration.Tests.Access;
using Starter.Integration.Tests.Support;
using Starter.Integration.Tests.Users;

namespace Starter.Integration.Tests.Assignments;

/// <summary>A signed-in staff user of the queue tests: their PublicId, internal id and access token.</summary>
/// <param name="Id">The user's PublicId (family <c>users</c>).</param>
/// <param name="UserId">The internal user id (decoded from the PublicId).</param>
/// <param name="Token">The access token.</param>
/// <param name="DisplayName">The display name.</param>
public sealed record QueueStaff(string Id, long UserId, string Token, string DisplayName);

/// <summary>
/// The queue API as the contract states it (paths and JSON names only; no module types on the wire), plus the setup
/// the tests need: organizations, staff users with grants, records in the in-memory directory.
/// </summary>
internal sealed class QueueApi(AssignmentsHttpHost host) : IDisposable
{
    public const string WorkPermission = "assignments.work";
    public const string AssignPermission = "assignments.assign";
    public const string Unassigned = "unassigned";
    public const string Mine = "mine";
    public const string All = "all";
    private const string Root = "/api/queues/";
    private const string BearerScheme = "Bearer";
    private const string UsersLabel = "users";
    private const string OrganizationsLabel = "organizations";
    private const string RecordsLabel = "records";

    private static long _nextRecordId = Random.Shared.NextInt64(1_000_000, 1_000_000_000);

    private readonly HttpClient _http = host.Factory.CreateClient();
    private readonly StarterClient _session = host.CreateClient();
    private readonly UsersApi _users = new(host.Factory.CreateClient());
    private readonly AccessApi _access = new(host.Factory.CreateClient());
    private SessionTokens? _admin;

    public static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    public static long NextRecordId() => Interlocked.Increment(ref _nextRecordId);

    public static string QueuePath(string recordType, string query) => Root + recordType + "?" + query;

    public static string ItemPath(string recordType, string recordId, string action) => Root + recordType + "/" + recordId + "/" + action;

    public string RecordPublicId(long recordId) => host.Service<IPublicIdCodec>().Protect(RecordsLabel, recordId);

    public async Task<SessionTokens> AdminAsync() =>
        _admin ??= await _session.LoginAsync(host.Accounts.BreakGlassUserName, host.Accounts.BreakGlassPassword);

    /// <summary>Creates an organization; returns its PublicId and internal id.</summary>
    public async Task<(string Id, long OrganizationId)> CreateOrganizationAsync(string code)
    {
        var admin = await AdminAsync();
        var id = await _users.CreateExternalOrganizationAsync(code, admin.AccessToken);
        Assert.True(host.Service<IPublicIdCodec>().TryUnprotect(OrganizationsLabel, id, out var organizationId));
        return (id, organizationId);
    }

    /// <summary>Creates a user in <paramref name="organizationId"/>, grants <paramref name="permissions"/>, activates and signs in.</summary>
    public async Task<QueueStaff> CreateStaffAsync(string userName, string organizationId, params object[] permissions)
    {
        var admin = await AdminAsync();
        var displayName = "Queue " + userName;
        using var created = await _users.PostJsonAsync(
            UsersApi.Users,
            new { userName, displayName, email = (string?)null, kind = UsersApi.ExternalKind, binding = UsersApi.PasswordBinding, organizationId },
            admin.AccessToken);
        var body = await UsersApi.ReadAsync(created, HttpStatusCode.Created);
        var id = body.GetProperty("user").GetProperty("id").GetString()!;
        using (var granted = await _access.PutGrantsAsync(admin.AccessToken, id, [], permissions))
        {
            await UsersApi.ReadAsync(granted, HttpStatusCode.OK);
        }

        var password = "Aa1!" + Guid.NewGuid().ToString("N") + Suffix().ToUpperInvariant();
        var ticket = UsersApi.TicketOf(body.GetProperty("activation").GetProperty("activationLink").GetString()!);
        using (var activated = await _users.PostJsonAsync(UsersApi.Activate, new { ticket, password }, accessToken: null))
        {
            Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);
        }

        var session = await _session.LoginAsync(userName, password);
        Assert.True(host.Service<IPublicIdCodec>().TryUnprotect(UsersLabel, id, out var userId));
        return new QueueStaff(id, userId, session.AccessToken, displayName);
    }

    /// <summary>Puts a test record in the directory: submitted (unless a draft) a few minutes ago, owned by <paramref name="ownerUserId"/>.</summary>
    public RecordSummary PutRecord(long recordId, long ownerUserId, long? organizationId, int status = TestRecord.Submitted)
    {
        var now = host.Service<TimeProvider>().GetUtcNow().AddMinutes(-1);
        var isDraft = status == TestRecord.Draft;
        var record = new RecordSummary(
            Id: recordId,
            RecordType: TestRecord.Type,
            Number: isDraft ? null : TestRecord.Prefix + "-2026-" + recordId.ToString(CultureInfo.InvariantCulture),
            Status: status,
            OwnerUserId: ownerUserId,
            OrganizationId: organizationId,
            CreatedAt: now.AddHours(-1),
            SubmittedAt: isDraft ? null : now,
            UpdatedAt: now,
            IsTerminal: status == TestRecord.Closed);
        host.Directory.Put(record);
        return record;
    }

    public Task<HttpResponseMessage> ListAsync(QueueStaff staff, string query, string recordType = TestRecord.Type) =>
        SendAsync(HttpMethod.Get, QueuePath(recordType, query), staff.Token, body: null);

    public Task<HttpResponseMessage> ClaimAsync(QueueStaff staff, long recordId) =>
        SendAsync(HttpMethod.Post, ItemPath(TestRecord.Type, RecordPublicId(recordId), "claim"), staff.Token, body: null);

    public Task<HttpResponseMessage> ReleaseAsync(QueueStaff staff, long recordId) =>
        SendAsync(HttpMethod.Post, ItemPath(TestRecord.Type, RecordPublicId(recordId), "release"), staff.Token, body: null);

    public Task<HttpResponseMessage> AssignAsync(QueueStaff staff, long recordId, object body) =>
        SendAsync(HttpMethod.Post, ItemPath(TestRecord.Type, RecordPublicId(recordId), "assign"), staff.Token, body);

    /// <summary>The ids of a 200 queue page, and the whole body.</summary>
    public static async Task<(IReadOnlyList<string> Ids, JsonElement Body)> ReadPageAsync(HttpResponseMessage response)
    {
        var body = await UsersApi.ReadAsync(response, HttpStatusCode.OK);
        return ([.. body.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!)], body);
    }

    public static object Grant(string key, int scope = AccessApi.Scopes.Global) => AccessApi.PermissionEntry(key, scope);

    public void Dispose()
    {
        _http.Dispose();
        _session.Dispose();
        _users.Dispose();
        _access.Dispose();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _http.SendAsync(request);
    }
}
