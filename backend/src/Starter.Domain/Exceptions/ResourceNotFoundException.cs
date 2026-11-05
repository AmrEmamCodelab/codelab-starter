namespace Starter.Domain.Exceptions;

/// <summary>
/// A resource does not exist, or belongs to someone the caller may not see (ERROR-HANDLING row 1); the host maps it
/// to 404. The message names the resource kind only, never an identifier a user typed.
/// </summary>
public sealed class ResourceNotFoundException(string resourceName)
    : Exception($"The requested {resourceName} was not found.")
{
    /// <summary>The resource kind (a constant, for example the module's resource names class).</summary>
    public string ResourceName { get; } = resourceName;
}
