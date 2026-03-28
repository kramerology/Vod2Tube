using Microsoft.AspNetCore.Mvc;
using Vod2Tube.Application;
using Vod2Tube.Application.Services;

namespace Vod2Tube.Api.Controllers;

[ApiController]
[Route("api/vods")]
public class VodsController(PipelineService pipelineSvc, TwitchGraphQLService twitchSvc) : ControllerBase
{
    [HttpGet]
    public async Task<IResult> GetAll()
        => Results.Ok(await pipelineSvc.GetAllJobsAsync());

    [HttpGet("active")]
    public async Task<IResult> GetActive()
        => Results.Ok(await pipelineSvc.GetActiveJobsAsync());

    [HttpGet("completed")]
    public async Task<IResult> GetCompleted()
        => Results.Ok(await pipelineSvc.GetCompletedJobsAsync());

    [HttpPost("{vodId}/pause")]
    public async Task<IResult> Pause(string vodId)
        => await pipelineSvc.PauseJobAsync(vodId) ? Results.Ok() : Results.NotFound();

    [HttpPost("{vodId}/resume")]
    public async Task<IResult> Resume(string vodId)
        => await pipelineSvc.ResumeJobAsync(vodId) ? Results.Ok() : Results.NotFound();

    [HttpPost("{vodId}/cancel")]
    public async Task<IResult> Cancel(string vodId)
        => await pipelineSvc.CancelJobAsync(vodId) ? Results.Ok() : Results.NotFound();

    [HttpPost("{vodId}/retry")]
    public async Task<IResult> Retry(string vodId)
        => await pipelineSvc.RetryJobAsync(vodId) ? Results.Ok() : Results.NotFound();

    [HttpPost("{vodId}/retry/{stage}")]
    public async Task<IResult> RetryFromStage(string vodId, string stage)
        => await pipelineSvc.RetryFromStageAsync(vodId, stage) ? Results.Ok() : Results.NotFound();

    [HttpGet("thumbnails")]
    public async Task<IResult> GetThumbnails([FromQuery] string ids)
    {
        var vodIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var urls = await twitchSvc.GetVodThumbnailUrlsAsync(vodIds);
        return Results.Ok(urls);
    }
}
