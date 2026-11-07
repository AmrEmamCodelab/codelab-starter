namespace Codelab.OpsConsole.Api.Hosting;

/// <summary>
/// The rule for an address the console sends credentials to (the service assertion, the Seq key): absolute http(s),
/// and https outside Development unless the host is loopback (a Seq or API on the same server).
/// </summary>
public static class UpstreamAddress
{
    /// <summary>True when <paramref name="value"/> is an acceptable upstream base address.</summary>
    public static bool IsAcceptable(string? value, bool isDevelopment)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return uri.Scheme == Uri.UriSchemeHttp && (isDevelopment || uri.IsLoopback);
    }

    /// <summary><paramref name="value"/> as a base address that ends with a slash, so relative routes keep its path.</summary>
    public static Uri AsBaseAddress(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var trimmed = value.Trim();
        return new Uri(trimmed.EndsWith('/') ? trimmed : trimmed + "/", UriKind.Absolute);
    }
}
