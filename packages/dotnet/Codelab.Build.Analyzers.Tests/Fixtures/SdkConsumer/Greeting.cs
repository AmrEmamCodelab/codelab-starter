namespace Starter.SdkConsumer;

/// <summary>Clean fixture code: builds with every Codelab.Build.Sdk gate on.</summary>
public static class Greeting
{
    private const int MaximumNameLength = 64;

    /// <summary>Returns a greeting for a name, shortened to a fixed maximum length.</summary>
    public static string For(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var shortName = name.Length > MaximumNameLength ? name[..MaximumNameLength] : name;
        return $"Hello {shortName}";
    }
}
