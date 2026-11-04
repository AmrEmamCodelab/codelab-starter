using System.Reflection;

namespace Codelab.Observability;

/// <summary>The release a host reports in <see cref="LogPropertyNames.Release"/>.</summary>
public static class ReleaseVersion
{
    /// <summary>The value when the assembly carries no informational version.</summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// The assembly's informational version (<c>&lt;semver&gt;+&lt;short sha&gt;</c>, stamped by Codelab.Build.Sdk), or
    /// <see cref="Unknown"/>.
    /// </summary>
    public static string Of(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { Length: > 0 } version
            ? version
            : Unknown;
    }
}
