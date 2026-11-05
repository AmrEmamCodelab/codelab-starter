using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Net;
using System.Text;

namespace Codelab.Identity.ActiveDirectory;

/// <summary>The production <see cref="ILdapConnectionFactory"/>: one <see cref="SystemLdapConnection"/> per sign-in, no shared state.</summary>
internal sealed class SystemLdapConnectionFactory : ILdapConnectionFactory
{
    public ILdapConnection Open(LdapEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return new SystemLdapConnection(endpoint);
    }
}

/// <summary>
/// <see cref="ILdapConnection"/> over <see cref="LdapConnection"/> (<c>System.DirectoryServices.Protocols</c>).
/// <para>
/// <b>Server certificates are validated.</b> <see cref="LdapSessionOptions.VerifyServerCertificate"/> is never set, so
/// the operating system's chain and name validation decide, and there is no configuration key that changes that. An
/// LDAPS failure in a new environment is a CA that is not trusted on that host: install it, never accept any certificate.
/// </para>
/// </summary>
internal sealed class SystemLdapConnection : ILdapConnection
{
    private const int LdapProtocolVersion = 3;
    private const string BindOperation = "bind";
    private const string SearchOperation = "search";
    private const string WhoAmIOperation = "whoami";

    /// <summary>The RFC 4532 "Who am I?" extended operation.</summary>
    private const string WhoAmIOid = "1.3.6.1.4.1.4203.1.11.3";

    private readonly LdapConnection connection;
    private readonly LdapEndpoint endpoint;

    public SystemLdapConnection(LdapEndpoint endpoint)
    {
        this.endpoint = endpoint;
        connection = new LdapConnection(new LdapDirectoryIdentifier(endpoint.Host, endpoint.Port, fullyQualifiedDnsHostName: true, connectionless: false))
        {
            AuthType = AuthType.Basic,
            AutoBind = false,
            Timeout = endpoint.Timeout,
        };
        connection.SessionOptions.ProtocolVersion = LdapProtocolVersion;
        connection.SessionOptions.SecureSocketLayer = endpoint.UseLdaps;
    }

    public void Bind(LdapBindRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            connection.Bind(new NetworkCredential(request.BindName, request.Password));
        }
        catch (DirectoryException failure)
        {
            throw Translate(failure, BindOperation);
        }
    }

    public string? WhoAmI()
    {
        try
        {
            var response = (ExtendedResponse)connection.SendRequest(new ExtendedRequest(WhoAmIOid), endpoint.Timeout);
            return response.ResponseValue is { } value ? Encoding.UTF8.GetString(value) : string.Empty;
        }
        catch (DirectoryException failure)
        {
            throw Translate(failure, WhoAmIOperation);
        }
    }

    public IReadOnlyList<LdapEntry> Search(LdapSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var search = new SearchRequest(request.BaseDn, request.Filter, SearchScope.Subtree, [.. request.Attributes])
            {
                TimeLimit = request.Timeout,
            };
            var response = (SearchResponse)connection.SendRequest(search, request.Timeout);
            return [.. response.Entries.Cast<SearchResultEntry>().Select(EntryOf)];
        }
        catch (DirectoryException failure)
        {
            throw Translate(failure, SearchOperation);
        }
    }

    public void Dispose() => connection.Dispose();

    private static LdapEntry EntryOf(SearchResultEntry entry)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DirectoryAttribute attribute in entry.Attributes.Values)
        {
            if (attribute.Count > 0 && attribute[0] is string value)
            {
                attributes[attribute.Name] = value;
            }
        }

        return new LdapEntry(entry.DistinguishedName, attributes);
    }

    /// <summary>The fault with its result code. The message names the endpoint and the operation, never what was bound.</summary>
    private LdapOperationException Translate(DirectoryException failure, string operation)
    {
        var (code, serverMessage) = failure switch
        {
            LdapException ldap => ((int?)ldap.ErrorCode, ldap.ServerErrorMessage),
            DirectoryOperationException operationFailure when operationFailure.Response is { } response =>
                ((int?)response.ResultCode, response.ErrorMessage),
            _ => ((int?)null, (string?)null),
        };
        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"LDAP {operation} against {endpoint.Host}:{endpoint.Port} (ldaps={endpoint.UseLdaps}) failed with result code {LdapFailureMapping.Describe(code)}.");
        return new LdapOperationException(message, resultCode: code, serverMessage: serverMessage, innerException: failure);
    }
}
