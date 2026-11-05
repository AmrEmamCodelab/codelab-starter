using System.Text;

namespace Starter.Modules.Identity.Application.LoginModes;

/// <summary>The SPA's SSO callback route as the API redirects to it (built from <c>Auth:WebBaseUrl</c>).</summary>
public interface ISsoSpaCallback
{
    /// <summary>The absolute URL of <see cref="SsoSpaFragment.CallbackPath"/> on the SPA.</summary>
    Uri CallbackUrl { get; }
}

/// <summary>
/// The redirect from the API's SSO callback to the SPA. The outcome travels in the URL FRAGMENT
/// (<c>#ticket=&lt;one-time ticket&gt;&amp;returnUrl=&lt;path&gt;</c> or <c>#error=&lt;CODE&gt;&amp;returnUrl=&lt;path&gt;</c>):
/// a browser never sends the fragment to a server, so the ticket reaches no proxy, server log or Referer header.
/// The SPA reads it, removes it from the address bar and posts the ticket to <c>sso/exchange</c>.
/// </summary>
public static class SsoSpaFragment
{
    /// <summary>The SPA route that exchanges the ticket (mirrors the SPA's route table; not configuration).</summary>
    public const string CallbackPath = "/auth/sso/callback";

    /// <summary>The fragment member carrying the one-time exchange ticket.</summary>
    public const string TicketKey = "ticket";

    /// <summary>The fragment member carrying the error code (the SPA shows <c>errors.&lt;CODE&gt;</c>).</summary>
    public const string ErrorKey = "error";

    /// <summary>The fragment member carrying the path the SPA opens next (omitted when it is <c>/</c>).</summary>
    public const string ReturnUrlKey = "returnUrl";

    private const char Separator = '&';
    private const char Assignment = '=';

    /// <summary>The callback URL on <paramref name="callbackUrl"/> with <paramref name="result"/> in its fragment.</summary>
    public static Uri Location(Uri callbackUrl, SsoCallbackResult result)
    {
        ArgumentNullException.ThrowIfNull(callbackUrl);
        ArgumentNullException.ThrowIfNull(result);
        var fragment = new StringBuilder();
        Append(fragment, key: result.Ticket is null ? ErrorKey : TicketKey, value: result.Ticket ?? result.ErrorCode ?? LoginModeErrorCodes.SsoRejected);
        if (result.ReturnUrl != ReturnUrlPolicy.Root)
        {
            Append(fragment, key: ReturnUrlKey, value: result.ReturnUrl);
        }

        return new UriBuilder(callbackUrl) { Fragment = fragment.ToString() }.Uri;
    }

    private static void Append(StringBuilder fragment, string key, string value)
    {
        if (fragment.Length > 0)
        {
            fragment.Append(Separator);
        }

        fragment.Append(key).Append(Assignment).Append(Uri.EscapeDataString(value));
    }
}
