using Codelab.Identity.ActiveDirectory;

namespace Codelab.Identity.Tests.TestSupport;

/// <summary>
/// An in-memory directory behind <see cref="ILdapConnectionFactory"/>: accounts keyed by bind name, each with its secret
/// and its entry. It answers a bind like Active Directory (result code 49, with a <c>data</c> sub-code for a disabled
/// account) and records every bind and search so a test can assert what was (not) sent.
/// </summary>
internal sealed class FakeDirectory : ILdapConnectionFactory
{
    private readonly Dictionary<string, FakeAccount> accounts = new(StringComparer.OrdinalIgnoreCase);

    public List<LdapEndpoint> Opened { get; } = [];

    public List<string> BoundNames { get; } = [];

    public List<LdapSearchRequest> Searches { get; } = [];

    /// <summary>Thrown by <see cref="Open"/> (a server that is down).</summary>
    public LdapOperationException? OpenFailure { get; set; }

    /// <summary>Thrown by every bind instead of checking the account.</summary>
    public LdapOperationException? BindFailure { get; set; }

    /// <summary>Thrown by every search.</summary>
    public LdapOperationException? SearchFailure { get; set; }

    /// <summary>When set, every search answers these entries instead of the bound account's.</summary>
    public IReadOnlyList<LdapEntry>? SearchAnswer { get; set; }

    /// <summary>
    /// When set, "Who am I?" answers <c>u:{WhoAmIDomain}\{bound sAMAccountName}</c> (Active Directory's form); when null
    /// (the default) the connection cannot ask, like a seam implementation without it.
    /// </summary>
    public string? WhoAmIDomain { get; set; }

    /// <summary>When set, "Who am I?" answers exactly this, overriding <see cref="WhoAmIDomain"/>.</summary>
    public string? WhoAmIAnswer { get; set; }

    /// <summary>Thrown by "Who am I?".</summary>
    public LdapOperationException? WhoAmIFailure { get; set; }

    /// <summary>When set, the bind blocks until it is released (a directory that never answers).</summary>
    public ManualResetEventSlim? Hang { get; set; }

    public FakeDirectory Add(string bindName, string secret, LdapEntry entry, string? disabledSubCode = null)
    {
        accounts[bindName] = new FakeAccount(secret, entry, disabledSubCode);
        return this;
    }

    public ILdapConnection Open(LdapEndpoint endpoint)
    {
        Opened.Add(endpoint);
        if (OpenFailure is not null)
        {
            throw OpenFailure;
        }

        return new Connection(this);
    }

    public static LdapEntry Entry(string loginName, string? upn = null, string? mail = null, string? displayName = null, string? userAccountControl = null)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActiveDirectoryAttributes.LoginName] = loginName,
        };
        AddIfSet(attributes, ActiveDirectoryAttributes.Upn, upn);
        AddIfSet(attributes, ActiveDirectoryAttributes.Email, mail);
        AddIfSet(attributes, ActiveDirectoryAttributes.DisplayName, displayName);
        AddIfSet(attributes, ActiveDirectoryAttributes.UserAccountControl, userAccountControl);
        return new LdapEntry($"CN={loginName},OU=Staff,DC=example,DC=org", attributes);
    }

    public static LdapOperationException InvalidCredentials(string subCode) =>
        new("bind refused", resultCode: LdapResultCodes.InvalidCredentials, serverMessage: $"80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data {subCode}, v4563");

    public static LdapOperationException Code(int? resultCode) => new("fault", resultCode: resultCode, serverMessage: null);

    private static void AddIfSet(Dictionary<string, string> attributes, string name, string? value)
    {
        if (value is not null)
        {
            attributes[name] = value;
        }
    }

    private sealed record FakeAccount(string Secret, LdapEntry Entry, string? DisabledSubCode);

    private sealed class Connection(FakeDirectory directory) : ILdapConnection
    {
        private FakeAccount? bound;

        public void Bind(LdapBindRequest request)
        {
            directory.BoundNames.Add(request.BindName);
            directory.Hang?.Wait(TimeSpan.FromSeconds(30));
            if (directory.BindFailure is not null)
            {
                throw directory.BindFailure;
            }

            if (!directory.accounts.TryGetValue(request.BindName, out var account) || account.Secret != request.Password)
            {
                throw InvalidCredentials("52e");
            }

            if (account.DisabledSubCode is not null)
            {
                throw InvalidCredentials(account.DisabledSubCode);
            }

            bound = account;
        }

        public IReadOnlyList<LdapEntry> Search(LdapSearchRequest request)
        {
            directory.Searches.Add(request);
            if (directory.SearchFailure is not null)
            {
                throw directory.SearchFailure;
            }

            return directory.SearchAnswer ?? (bound is null ? [] : [bound.Entry]);
        }

        public string? WhoAmI()
        {
            if (directory.WhoAmIFailure is not null)
            {
                throw directory.WhoAmIFailure;
            }

            if (directory.WhoAmIAnswer is not null)
            {
                return directory.WhoAmIAnswer;
            }

            return directory.WhoAmIDomain is null || bound is null
                ? null
                : $"u:{directory.WhoAmIDomain}\\{bound.Entry.Attribute(ActiveDirectoryAttributes.LoginName)}";
        }

        public void Dispose()
        {
        }
    }
}
