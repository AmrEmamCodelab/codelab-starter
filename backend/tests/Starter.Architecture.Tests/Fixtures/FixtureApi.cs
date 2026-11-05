using Microsoft.AspNetCore.Mvc;
using Starter.Architecture.Tests.Fixtures.Application;

namespace Starter.Architecture.Tests.Fixtures.Api;

/// <summary>The thin shape: sealed, in Api, one use case, an HTTP method on every action.</summary>
public sealed class ThinController(FixtureUseCase useCase) : ControllerBase
{
    /// <summary>A tagged action.</summary>
    [HttpGet]
    public ActionResult<string> Get() => useCase.Execute();
}

/// <summary>Not sealed, receives a port, and has an action without an HTTP method.</summary>
public class FatController(FixtureUseCase useCase, IFixtureRepository repository) : ControllerBase
{
    /// <summary>A tagged action.</summary>
    [HttpGet]
    public ActionResult<string> Get() => useCase.Execute();

    /// <summary>An action without an HTTP method attribute.</summary>
    public ActionResult<string> Untagged() => repository.Load();
}
