using System.Reflection;
using Starter.Application.Authorization;
using Starter.Application.Users;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Tests.Fakes;

/// <summary>An in-memory accounts and sessions store; <see cref="SaveChangesAsync"/> assigns ids like the database.</summary>
internal sealed class InMemoryIdentityStore : IUserAccountRepository, IUserSessionRepository, IIdentityUnitOfWork
{
    private readonly List<UserAccount> _accounts = [];
    private readonly List<UserSession> _sessions = [];
    private long _nextId = 100;

    public IReadOnlyList<UserAccount> Accounts => _accounts;

    public IReadOnlyList<UserSession> Sessions => _sessions;

    public int SaveCount { get; private set; }

    public Task<UserAccount?> FindByUserNameAsync(string userName, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.SingleOrDefault(account => account.NormalizedUserName == UserNames.Normalize(userName)));

    public Task<UserAccount?> FindByIdAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.SingleOrDefault(account => account.Id == userId));

    public Task<UserAccount?> FindBySubjectAsync(Guid subjectId, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.SingleOrDefault(account => account.SubjectId == subjectId));

    public void Add(UserAccount account) => _accounts.Add(account);

    public Task<UserSession?> FindBySessionIdAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(_sessions.SingleOrDefault(session => session.SessionId == sessionId));

    public void Add(UserSession session) => _sessions.Add(session);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        foreach (var account in _accounts.Where(account => account.Id == 0))
        {
            SetId(account, _nextId++);
        }

        foreach (var session in _sessions.Where(session => session.Id == 0))
        {
            SetId(session, _nextId++);
        }

        return Task.FromResult(0);
    }

    /// <summary>Adds an active account with <paramref name="password"/> (hashed by <see cref="PlainTextHasher"/>) and saves it.</summary>
    public UserAccount AddAccount(string userName, string password, bool isBreakGlass = false)
    {
        var account = UserAccount.Create(new NewUserAccount(
            SubjectId: Guid.NewGuid(),
            UserName: userName,
            DisplayName: userName + " display",
            PasswordHash: PlainTextHasher.HashOf(password),
            IsBreakGlass: isBreakGlass,
            CreatedAt: TestClock.Start));
        Add(account);
        SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
        return account;
    }

    private static void SetId(object entity, long id) =>
        entity.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);
}

/// <summary>A transparent hasher: the hash is a prefix plus the password, so tests can read what was stored.</summary>
internal sealed class PlainTextHasher : IPasswordHasher
{
    private const string Prefix = "hash:";
    private const string OutdatedPrefix = "old:";

    public int VerifyCount { get; private set; }

    public static string HashOf(string password) => Prefix + password;

    public static string OutdatedHashOf(string password) => OutdatedPrefix + password;

    public string Hash(string password) => HashOf(password);

    public PasswordVerification Verify(string? passwordHash, string password)
    {
        VerifyCount++;
        if (passwordHash == HashOf(password))
        {
            return PasswordVerification.Succeeded;
        }

        return passwordHash == OutdatedHashOf(password) ? PasswordVerification.SucceededRehashNeeded : PasswordVerification.Failed;
    }
}

/// <summary>Writes the claims into the token text, so tests can read them back.</summary>
internal sealed class RecordingAccessTokenIssuer : IAccessTokenIssuer
{
    public List<AccessTokenClaims> Issued { get; } = [];

    public string Issue(AccessTokenClaims claims)
    {
        Issued.Add(claims);
        return "access-" + Issued.Count;
    }
}

/// <summary>Sequential, well-formed session ids and secrets.</summary>
internal sealed class SequentialTokenGenerator : ISecureTokenGenerator
{
    private int _sessions;
    private int _secrets;

    public string NewSessionId() => (++_sessions).ToString("x32", System.Globalization.CultureInfo.InvariantCulture);

    public string NewRefreshSecret() => "secret" + ++_secrets;
}

internal sealed class FakeSessionVersions : ISessionVersions
{
    public Dictionary<Guid, long> Versions { get; } = [];

    public Task<long> CurrentAsync(Guid subjectId, CancellationToken cancellationToken) =>
        Task.FromResult(Versions.GetValueOrDefault(subjectId));

    public Task<long> BumpAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        var next = Versions.GetValueOrDefault(subjectId) + 1;
        Versions[subjectId] = next;
        return Task.FromResult(next);
    }
}

internal sealed class FakeSessionRevocation : ISessionRevocation
{
    public Dictionary<string, DateTimeOffset> Revoked { get; } = [];

    public Task RevokeAsync(string sessionId, DateTimeOffset until, CancellationToken cancellationToken)
    {
        Revoked[sessionId] = until;
        return Task.CompletedTask;
    }
}

internal sealed class FakePermissionGrants : IUserPermissionGrants
{
    public List<(long UserId, string[] Keys)> Calls { get; } = [];

    public Task EnsureGrantedAsync(long userId, IReadOnlyCollection<string> permissionKeys, CancellationToken cancellationToken)
    {
        Calls.Add((userId, permissionKeys.ToArray()));
        return Task.CompletedTask;
    }
}

internal sealed class FakeCurrentUser(string? sessionId) : ICurrentUserContext
{
    public bool IsAuthenticated => sessionId is not null;

    public Guid? SubjectId => null;

    public string? SessionId => sessionId;
}

internal static class TestClock
{
    public static readonly DateTimeOffset Start = new(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);
}
