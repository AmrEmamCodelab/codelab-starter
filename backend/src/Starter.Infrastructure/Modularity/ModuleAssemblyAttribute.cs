namespace Starter.Infrastructure.Modularity;

/// <summary>
/// Names one module assembly the host references. Never written by hand: the backend build turns every module
/// ProjectReference of the host project into one of these (backend/Directory.Build.targets), so the host project's
/// reference list is the only place that lists modules.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ModuleAssemblyAttribute(string assemblyName) : Attribute
{
    /// <summary>The simple name of the module assembly.</summary>
    public string AssemblyName { get; } = assemblyName;
}
