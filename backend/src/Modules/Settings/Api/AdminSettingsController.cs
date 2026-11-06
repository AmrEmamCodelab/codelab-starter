using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Results;
using Starter.Modules.Settings.Application;
using Starter.Modules.Settings.Application.Admin;

namespace Starter.Modules.Settings.Api;

/// <summary>The runtime settings admin screen. Thin: each action calls one use case and maps its outcome.</summary>
[ApiController]
[Route(SettingsRoutes.Admin)]
[Produces(SettingsRoutes.JsonContentType)]
public sealed class AdminSettingsController(ListSettings listSettings, UpdateSetting updateSetting) : ControllerBase
{
    /// <summary>Every setting, ordered by group, then key.</summary>
    [HttpGet]
    [HasPermission(SettingsPermissionKeys.View)]
    [ProducesResponseType<IReadOnlyList<SettingResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SettingResponse>>> ListAsync(CancellationToken cancellationToken) =>
        (await listSettings.ExecuteAsync(cancellationToken)).ToActionResult(SettingResponse.ListFrom);

    /// <summary>Changes a setting's value; every node sees it within <c>SettingsLimits.CacheTtl</c>.</summary>
    [HttpPut(SettingsRoutes.KeyTemplate)]
    [HasPermission(SettingsPermissionKeys.Manage)]
    [ProducesResponseType<SettingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SettingResponse>> UpdateAsync(
        [FromRoute(Name = SettingsRoutes.KeyParameter)] string key,
        [FromBody] UpdateSettingRequest request,
        CancellationToken cancellationToken) =>
        (await updateSetting.ExecuteAsync(key, request, cancellationToken)).ToActionResult(SettingResponse.From);
}
