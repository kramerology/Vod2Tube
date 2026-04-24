using Microsoft.AspNetCore.Mvc;
using Vod2Tube.Application;
using Vod2Tube.Application.Services;

namespace Vod2Tube.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController(SettingsService settingsSvc, ExecutableReadinessMonitor executableReadinessMonitor) : ControllerBase
{
    [HttpGet]
    public async Task<IResult> Get()
        => Results.Ok(await settingsSvc.GetSettingsAsync());

    [HttpGet("executables")]
    public IResult GetExecutableStatus()
        => Results.Ok(executableReadinessMonitor.CurrentStatus);

    [HttpPut]
    public async Task<IResult> Update(AppSettings incoming)
    {
        await settingsSvc.SaveSettingsAsync(incoming);
        await executableReadinessMonitor.RefreshAsync(HttpContext.RequestAborted);
        return Results.Ok(await settingsSvc.GetSettingsAsync());
    }
}
