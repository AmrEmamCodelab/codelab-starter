using System.Reflection;

namespace Codelab.Observability;

/// <summary>
/// Finds the host's own assembly from the host's application name. <c>Assembly.GetEntryAssembly()</c> is the wrong
/// source under a test runner or a WebApplicationFactory: it is the test host, so every log line and the OTel
/// <c>service.version</c> would carry the test SDK's version instead of the API's. ASP.NET Core sets the application name
/// to the host's assembly (WebApplicationFactory sets it to the entry point's assembly), so it names the right one.
/// </summary>
internal static class HostAssemblies
{
    /// <summary>
    /// The assembly named <paramref name="applicationName"/> when it can be loaded; otherwise the entry assembly (a host
    /// whose application name was changed to a display name), then Codelab.Observability itself.
    /// </summary>
    public static Assembly FromApplicationName(string? applicationName)
    {
        var fallback = Assembly.GetEntryAssembly() ?? typeof(HostAssemblies).Assembly;
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            return fallback;
        }

        try
        {
            return Assembly.Load(new AssemblyName(applicationName));
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException or ArgumentException)
        {
            return fallback;
        }
    }
}
