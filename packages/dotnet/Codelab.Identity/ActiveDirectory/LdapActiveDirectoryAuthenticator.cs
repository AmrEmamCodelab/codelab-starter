using Codelab.Identity.Diagnostics;
using Microsoft.Extensions.Options;

namespace Codelab.Identity.ActiveDirectory;

/// <summary>
/// <see cref="IActiveDirectoryAuthenticator"/> over an <see cref="ILdapConnectionFactory"/>: one connection, a simple bind
/// as the user, then a subtree search for the user's own entry under <c>BaseDn</c>. The found entry must be the bound
/// account (<see cref="BoundIdentity"/>, from the RFC 4532 "Who am I?" answer) before its identity is returned.
/// <para>
/// <b>Never throws for a directory fault.</b> A refused bind maps through <see cref="LdapFailureMapping"/>; a search
/// fault, a connect fault and the overall timeout are <see cref="DirectoryAuthStatus.Unavailable"/>. Cancellation
/// propagates.
/// </para>
/// <para>
/// <b>Logs no credential and no user name.</b> A log line carries the status, the LDAP result code and the endpoint;
/// the user name is personal data and the password never leaves the bind request.
/// </para>
/// </summary>
internal sealed class LdapActiveDirectoryAuthenticator(
    IOptions<ActiveDirectoryOptions> options,
    ILdapConnectionFactory connections,
    TimeProvider timeProvider,
    ILogger<LdapActiveDirectoryAuthenticator> logger) : IActiveDirectoryAuthenticator
{
    /// <summary>The longest password sent to the directory; anything longer is refused without a bind.</summary>
    internal const int MaxPasswordLength = 1024;

    public async Task<DirectoryAuthResult> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value;
        if (!settings.Enabled)
        {
            IdentityLog.DirectoryDisabled(logger, ActiveDirectoryOptions.SectionName);
            return DirectoryAuthResult.Unavailable;
        }

        var (verdict, name) = DirectoryUserName.Parse(userName, settings);
        if (verdict == DirectoryUserName.Verdict.ForeignDomain)
        {
            IdentityLog.DirectoryForeignDomain(logger);
        }

        if (name is null || string.IsNullOrEmpty(password) || password.Length > MaxPasswordLength)
        {
            return DirectoryAuthResult.InvalidCredentials;
        }

        return await RunBoundedAsync(settings, name, password, cancellationToken);
    }

    /// <summary>
    /// The LDAP API is synchronous, so the attempt runs on the thread pool and is awaited for at most the configured
    /// timeout (measured on the injected clock). The connection carries the same timeout, so an abandoned attempt ends too.
    /// </summary>
    private async Task<DirectoryAuthResult> RunBoundedAsync(
        ActiveDirectoryOptions settings, DirectoryUserName name, string password, CancellationToken cancellationToken)
    {
        var attempt = Task.Run(() => Authenticate(settings, name, password), CancellationToken.None);
        try
        {
            return await attempt.WaitAsync(settings.Timeout, timeProvider, cancellationToken);
        }
        catch (TimeoutException)
        {
            ObserveLateFault(attempt);
            IdentityLog.DirectoryTimedOut(logger, settings.Host, settings.Port, settings.TimeoutSeconds);
            return DirectoryAuthResult.Unavailable;
        }
    }

    private DirectoryAuthResult Authenticate(ActiveDirectoryOptions settings, DirectoryUserName name, string password)
    {
        var endpoint = new LdapEndpoint(settings.Host, settings.Port, settings.UseLdaps, settings.Timeout);
        try
        {
            using var connection = connections.Open(endpoint);
            var bound = Bind(connection, name, password);
            return bound ?? Search(connection, settings, name, authorizationId: connection.WhoAmI());
        }
        catch (LdapOperationException failure)
        {
            IdentityLog.DirectoryUnavailable(logger, endpoint.Host, endpoint.Port, LdapFailureMapping.Describe(failure.ResultCode));
            return DirectoryAuthResult.Unavailable;
        }
    }

    /// <summary>Null when the bind succeeded; otherwise the refusal. A bind fault below LDAP rethrows as unavailable.</summary>
    private DirectoryAuthResult? Bind(ILdapConnection connection, DirectoryUserName name, string password)
    {
        try
        {
            connection.Bind(new LdapBindRequest(BindName: name.BindName, Password: password));
            return null;
        }
        catch (LdapOperationException failure) when (LdapFailureMapping.ForBind(failure) is not DirectoryAuthStatus.Unavailable)
        {
            var status = LdapFailureMapping.ForBind(failure);
            IdentityLog.DirectoryRefused(logger, status);
            return status == DirectoryAuthStatus.AccountDisabled
                ? DirectoryAuthResult.AccountDisabled
                : DirectoryAuthResult.InvalidCredentials;
        }
    }

    private DirectoryAuthResult Search(
        ILdapConnection connection, ActiveDirectoryOptions settings, DirectoryUserName name, string? authorizationId)
    {
        var entries = connection.Search(new LdapSearchRequest(
            BaseDn: settings.BaseDn,
            Filter: LdapFilter.Build(settings.SearchFilter, name),
            Attributes: ActiveDirectoryAttributes.Requested,
            Timeout: settings.Timeout));

        if (entries.Count != 1)
        {
            // Nobody or several: never pick the first, which would sign in as whoever the directory listed first.
            IdentityLog.DirectoryNotExactlyOne(logger, entries.Count);
            return DirectoryAuthResult.InvalidCredentials;
        }

        var entry = entries[0];
        var (matches, check) = BoundIdentity.Verify(authorizationId, entry, name, settings);
        if (!matches)
        {
            // The password was checked against another account (an explicit UPN held elsewhere): never sign in as this entry.
            IdentityLog.DirectoryBoundIdentityMismatch(logger, check);
            return DirectoryAuthResult.InvalidCredentials;
        }

        if (LdapFailureMapping.IsDisabled(entry.Attribute(ActiveDirectoryAttributes.UserAccountControl)))
        {
            IdentityLog.DirectoryRefused(logger, DirectoryAuthStatus.AccountDisabled);
            return DirectoryAuthResult.AccountDisabled;
        }

        return DirectoryAuthResult.Succeeded(IdentityOf(entry, name));
    }

    private static DirectoryIdentity IdentityOf(LdapEntry entry, DirectoryUserName name) => new(
        LoginName: Clean(entry.Attribute(ActiveDirectoryAttributes.LoginName)) ?? name.LoginName,
        Upn: Clean(entry.Attribute(ActiveDirectoryAttributes.Upn)),
        Email: Clean(entry.Attribute(ActiveDirectoryAttributes.Email)),
        DisplayName: Clean(entry.Attribute(ActiveDirectoryAttributes.DisplayName)));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>An attempt abandoned at the timeout may still fault later; observe it so it is never unobserved.</summary>
    private static void ObserveLateFault(Task attempt) =>
        attempt.ContinueWith(
            finished => _ = finished.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
