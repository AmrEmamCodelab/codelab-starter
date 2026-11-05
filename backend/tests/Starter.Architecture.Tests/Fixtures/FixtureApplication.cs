using Microsoft.AspNetCore.Mvc;

namespace Starter.Architecture.Tests.Fixtures.Application;

/// <summary>A use case: a concrete class in the Application layer.</summary>
public sealed class FixtureUseCase
{
    /// <summary>The use case's single operation.</summary>
    public string Execute() => nameof(FixtureUseCase);
}

/// <summary>A port (interface); a controller must never receive one.</summary>
public interface IFixtureRepository
{
    /// <summary>A data-access operation.</summary>
    string Load();
}

/// <summary>A controller in the wrong layer.</summary>
public sealed class MisplacedController(FixtureUseCase useCase) : ControllerBase
{
    /// <summary>A tagged action.</summary>
    [HttpGet]
    public ActionResult<string> Get() => useCase.Execute();
}
