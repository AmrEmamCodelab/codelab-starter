namespace Codelab.Identity.ActiveDirectory;

/// <summary>
/// Bound from <see cref="SectionName"/>: where the directory is and how a user is found in it. The provider binds
/// as the user (no service account, so no directory secret in configuration), then searches for the user's own
/// entry under <see cref="BaseDn"/> to read the identity attributes. <see cref="ActiveDirectoryDeploymentGuard"/>
/// refuses an unusable section at start.
/// </summary>
public sealed class ActiveDirectoryOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Codelab:Identity:ActiveDirectory";

    /// <summary>The LDAPS port (TLS from the first byte).</summary>
    public const int LdapsPort = 636;

    /// <summary>The cleartext LDAP port. Allowed only in Development: a simple bind sends the password.</summary>
    public const int LdapPort = 389;

    /// <summary>The default bound on one sign-in (connect, bind and search together), in seconds.</summary>
    public const int DefaultTimeoutSeconds = 10;

    /// <summary>The placeholder in <see cref="SearchFilter"/> for the escaped logon name (the part before <c>@</c>).</summary>
    public const string LoginNamePlaceholder = "{0}";

    /// <summary>The placeholder in <see cref="SearchFilter"/> for the escaped bind name (<c>user@domain</c>).</summary>
    public const string BindNamePlaceholder = "{1}";

    /// <summary>The default user search: a person whose logon name or user principal name is the submitted one.</summary>
    public const string DefaultSearchFilter =
        "(&(objectCategory=person)(objectClass=user)(|(sAMAccountName={0})(userPrincipalName={1})))";

    /// <summary>Whether the Active Directory provider is on. Off: nothing else here is read and every sign-in is Unavailable.</summary>
    public bool Enabled { get; set; }

    /// <summary>The domain controller (or the domain's DNS name). Its certificate must name it for LDAPS to validate.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>The LDAP port: <see cref="LdapsPort"/> by default.</summary>
    public int Port { get; set; } = LdapsPort;

    /// <summary>Whether the connection is TLS. Required outside Development. There is no switch that skips certificate validation.</summary>
    public bool UseLdaps { get; set; } = true;

    /// <summary>The DNS domain a bare logon name is bound in, as <c>user@Domain</c> (for example <c>example.org</c>).</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// The NetBIOS name of <see cref="Domain"/> (for example <c>EXAMPLE</c>). A <c>DOMAIN\user</c> name is accepted only
    /// with this prefix; empty refuses the down-level form. Any other prefix names another (trusted) domain, whose account
    /// the bind would check while the search under <see cref="BaseDn"/> found a local namesake.
    /// </summary>
    public string NetBiosName { get; set; } = string.Empty;

    /// <summary>
    /// The UPN suffixes, besides <see cref="Domain"/>, that a typed <c>user@suffix</c> may carry (alternative suffixes of
    /// this domain). Any other suffix is refused before the bind, for the same reason as a foreign <see cref="NetBiosName"/>.
    /// </summary>
    public IList<string> UpnSuffixes { get; set; } = new List<string>();

    /// <summary>The subtree the user search starts from (for example <c>DC=example,DC=org</c>). It also limits who can sign in.</summary>
    public string BaseDn { get; set; } = string.Empty;

    /// <summary>
    /// The user search filter. Must contain <see cref="LoginNamePlaceholder"/>; may contain
    /// <see cref="BindNamePlaceholder"/>. Values are escaped per RFC 4515 before substitution.
    /// </summary>
    public string SearchFilter { get; set; } = DefaultSearchFilter;

    /// <summary>The bound on one sign-in, in seconds (<see cref="DefaultTimeoutSeconds"/>).</summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>Which directory value finds the local account (<see cref="LoginMatchField.LoginName"/> by default).</summary>
    public LoginMatchField MatchBy { get; set; } = LoginMatchField.LoginName;

    /// <summary><see cref="TimeoutSeconds"/> as a time span.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
}
