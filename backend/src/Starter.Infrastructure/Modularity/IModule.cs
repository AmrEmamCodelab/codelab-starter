using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Starter.Infrastructure.Modularity;

/// <summary>
/// One backend module (ADR-004). The host discovers every implementation in the module assemblies it references
/// (<see cref="ModuleCatalog"/>), so adding or removing a module never edits <c>Program.cs</c>. Each module has exactly
/// one public sealed implementation, <c>&lt;Name&gt;Module</c>, that calls the module's own <c>Add&lt;Name&gt;Module</c>
/// and <c>Use&lt;Name&gt;Module</c> extensions.
/// </summary>
public interface IModule
{
    /// <summary>The module name (the last segment of its assembly name, see <see cref="ModuleNaming"/>).</summary>
    string Name { get; }

    /// <summary>Registers the module's services, options, DbContext, permission catalog source and startup tasks.</summary>
    void AddModule(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps endpoints the module does not expose as controllers (controllers are found automatically).</summary>
    void UseModule(IEndpointRouteBuilder endpoints);
}
