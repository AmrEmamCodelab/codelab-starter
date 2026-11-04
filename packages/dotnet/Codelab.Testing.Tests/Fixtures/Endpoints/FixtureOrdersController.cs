using Microsoft.AspNetCore.Mvc;

namespace Codelab.Testing.Tests.Fixtures.Endpoints;

/// <summary>An MVC controller, so the contract checks are proven on controller endpoints too.</summary>
[ApiController]
[Route("orders")]
public sealed class FixtureOrdersController : ControllerBase
{
    [HttpPost]
    [FixturePermission(FixturePermissions.RecordsView)]
    public ActionResult<ItemView> Create([FromBody] OrderInput input) =>
        new ItemView(new WireId<ItemView>(input.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)), nameof(OrderInput));

    [HttpGet(FixtureRoutes.OrderById)]
    [FixturePermission(FixturePermissions.RecordsView)]
    public ActionResult<ItemView> Get(long orderId) => NotFound(orderId);
}
