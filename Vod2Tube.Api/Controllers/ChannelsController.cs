using Microsoft.AspNetCore.Mvc;
using Vod2Tube.Application;
using Vod2Tube.Application.Services;
using Vod2Tube.Domain;

namespace Vod2Tube.Api.Controllers;

[ApiController]
[Route("api/channels")]
public class ChannelsController(ChannelService channelSvc, TwitchGraphQLService twitchSvc) : ControllerBase
{
    [HttpGet]
    public async Task<IResult> GetAll()
        => Results.Ok(await channelSvc.GetAllChannelsAsync());

    [HttpPost]
    public async Task<IResult> Create(Channel channel)
    {
        var created = await channelSvc.AddNewChannelAsync(channel);
        return Results.Created($"/api/channels/{created.Id}", created);
    }

    [HttpPut("{id:int}")]
    public async Task<IResult> Update(int id, Channel channel)
    {
        channel.Id = id;
        return await channelSvc.UpdateChannelAsync(channel) ? Results.Ok(channel) : Results.NotFound();
    }

    [HttpDelete("{id:int}")]
    public async Task<IResult> Delete(int id)
        => await channelSvc.DeleteChannelAsync(id) ? Results.NoContent() : Results.NotFound();

    [HttpGet("avatars")]
    public async Task<IResult> GetAvatars()
    {
        var all = await channelSvc.GetAllChannelsAsync();
        var logins = all.Select(c => c.ChannelName);
        var urls = await twitchSvc.GetProfileImageUrlsAsync(logins);
        return Results.Ok(urls);
    }
}
