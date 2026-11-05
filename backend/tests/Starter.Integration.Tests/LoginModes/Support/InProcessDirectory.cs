using System.Collections.Concurrent;
using Codelab.Identity.ActiveDirectory;

namespace Starter.Integration.Tests.LoginModes.Support;

/// <summary>
/// The in-process LDAP double behind the Codelab.Identity seam (no network, no container): directory accounts keyed by
/// bind name (<c>user@Domain</c>), each with a password and the entry its own search returns. A wrong password answers
/// LDAP 49 with AD's <c>data 52e</c>, a disabled account 49 with <c>data 533</c>, and <see cref="IsDown"/> makes every
/// connection fail like an unreachable domain controller.
/// </summary>
public sealed class InProcessDirectory : ILdapConnectionFactory
{
    private const string WrongPasswordSubCode = "52e";
    private const string DisabledSubCode = "533";
    private const int ServerDown = 81;
    private const string WhoAmIPrefix = "u:EXAMPLE\\";

    private readonly ConcurrentDictionary<string, DirectoryAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True to fail every connection (a directory that cannot be reached).</summary>
    public bool IsDown { get; set; }

    /// <summary>Adds a directory account; <paramref name="bindName"/> is <c>login@Domain</c>.</summary>
    public void Add(string bindName, string password, string loginName, bool disabled = false)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ActiveDirectoryAttributes.LoginName] = loginName,
            [ActiveDirectoryAttributes.Upn] = bindName,
            [ActiveDirectoryAttributes.DisplayName] = loginName,
        };
        _accounts[bindName] = new DirectoryAccount(password, new LdapEntry($"CN={loginName},OU=Staff,DC=example,DC=test", attributes), disabled);
    }

    /// <inheritdoc />
    public ILdapConnection Open(LdapEndpoint endpoint) =>
        IsDown ? throw new LdapOperationException("server down", resultCode: ServerDown, serverMessage: null) : new Connection(this);

    private static LdapOperationException InvalidCredentials(string subCode) =>
        new("bind refused", resultCode: LdapResultCodes.InvalidCredentials, serverMessage: $"80090308: LdapErr: DSID-0C09044E, comment: AcceptSecurityContext error, data {subCode}, v4563");

    private sealed record DirectoryAccount(string Password, LdapEntry Entry, bool Disabled);

    private sealed class Connection(InProcessDirectory directory) : ILdapConnection
    {
        private DirectoryAccount? _bound;

        public void Bind(LdapBindRequest request)
        {
            if (!directory._accounts.TryGetValue(request.BindName, out var account) || !string.Equals(account.Password, request.Password, StringComparison.Ordinal))
            {
                throw InvalidCredentials(WrongPasswordSubCode);
            }

            _bound = account.Disabled ? throw InvalidCredentials(DisabledSubCode) : account;
        }

        public IReadOnlyList<LdapEntry> Search(LdapSearchRequest request) => _bound is null ? [] : [_bound.Entry];

        /// <summary>RFC 4532 "Who am I?" as AD answers it (<c>u:DOMAIN\sam</c>), so the production bound-identity check runs.</summary>
        public string? WhoAmI() => _bound is null ? null : WhoAmIPrefix + _bound.Entry.Attribute(ActiveDirectoryAttributes.LoginName);

        public void Dispose()
        {
        }
    }
}
