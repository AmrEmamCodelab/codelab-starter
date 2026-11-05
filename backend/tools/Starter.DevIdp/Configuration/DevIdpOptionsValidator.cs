using Microsoft.Extensions.Options;

namespace Starter.DevIdp.Configuration;

/// <summary>
/// Fails the start on an incoherent <see cref="DevIdpOptions"/>: an issuer that is not an absolute http(s) URL without
/// a path, a lifetime below one second, no client, a client without an id or a redirect URI, a URI that is not
/// absolute, a duplicate client id, no user, or a user without a unique subject.
/// </summary>
internal sealed class DevIdpOptionsValidator : IValidateOptions<DevIdpOptions>
{
    private const string RootPath = "/";

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DevIdpOptions options)
    {
        var failures = new List<string>();
        ValidateIssuer(options.Issuer, failures);
        ValidateLifetimes(options, failures);
        ValidateClients(options.Clients, failures);
        ValidateUsers(options.Users, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateIssuer(string? issuer, List<string> failures)
    {
        var valid = Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.AbsolutePath == RootPath
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
        if (!valid)
        {
            failures.Add($"{DevIdpOptions.SectionName}:Issuer must be an absolute http(s) URL with no path, query or fragment.");
        }
    }

    private static void ValidateLifetimes(DevIdpOptions options, List<string> failures)
    {
        if (options.CodeLifetimeSeconds < 1)
        {
            failures.Add($"{DevIdpOptions.SectionName}:CodeLifetimeSeconds must be at least 1.");
        }

        if (options.TokenLifetimeSeconds < 1)
        {
            failures.Add($"{DevIdpOptions.SectionName}:TokenLifetimeSeconds must be at least 1.");
        }
    }

    private static void ValidateClients(IList<DevIdpClient> clients, List<string> failures)
    {
        if (clients.Count == 0)
        {
            failures.Add($"{DevIdpOptions.SectionName}:Clients must register at least one client.");
        }

        foreach (var client in clients)
        {
            ValidateClient(client, failures);
        }

        var duplicates = clients.GroupBy(client => client.ClientId, StringComparer.Ordinal).Where(group => group.Count() > 1);
        failures.AddRange(duplicates.Select(group => $"{DevIdpOptions.SectionName}:Clients registers '{group.Key}' more than once."));
    }

    private static void ValidateClient(DevIdpClient client, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(client.ClientId))
        {
            failures.Add($"{DevIdpOptions.SectionName}:Clients has a client without a ClientId.");
        }

        if (client.RedirectUris.Count == 0)
        {
            failures.Add($"{DevIdpOptions.SectionName}:Clients '{client.ClientId}' has no RedirectUris.");
        }

        var notAbsolute = client.RedirectUris
            .Concat(client.PostLogoutRedirectUris)
            .Where(uri => !Uri.TryCreate(uri, UriKind.Absolute, out _));
        failures.AddRange(notAbsolute.Select(uri =>
            $"{DevIdpOptions.SectionName}:Clients '{client.ClientId}' has a URI that is not absolute: '{uri}'."));
    }

    private static void ValidateUsers(IList<DevIdpUser> users, List<string> failures)
    {
        if (users.Count == 0)
        {
            failures.Add($"{DevIdpOptions.SectionName}:Users must list at least one fixture user.");
        }

        if (users.Any(user => string.IsNullOrWhiteSpace(user.Subject)))
        {
            failures.Add($"{DevIdpOptions.SectionName}:Users has a user without a Subject.");
        }

        var duplicates = users.GroupBy(user => user.Subject, StringComparer.Ordinal).Where(group => group.Count() > 1);
        failures.AddRange(duplicates.Select(group => $"{DevIdpOptions.SectionName}:Users lists subject '{group.Key}' more than once."));
    }
}
