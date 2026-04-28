using Microsoft.AspNetCore.Mvc;
using Vod2Tube.Application.Services;

namespace Vod2Tube.Api.Controllers;

[ApiController]
[Route("api/accounts")]
public class AccountsController(YouTubeAccountService accountSvc) : ControllerBase
{
    [HttpGet]
    public async Task<IResult> GetAll()
    {
        var all = await accountSvc.GetAllAsync();
        var result = all.Select(a => new
        {
            a.Id,
            a.Name,
            a.AddedAtUTC,
            a.ChannelTitle,
            IsAuthorized = accountSvc.IsAuthorized(a.Id),
        });
        return Results.Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IResult> GetById(int id)
    {
        var account = await accountSvc.GetByIdAsync(id);
        if (account == null) return Results.NotFound();
        return Results.Ok(new
        {
            account.Id,
            account.Name,
            account.AddedAtUTC,
            account.ChannelTitle,
            IsAuthorized = accountSvc.IsAuthorized(account.Id),
        });
    }

    [HttpPost]
    public async Task<IResult> Create(CreateAccountRequest req)
    {
        try
        {
            var account = await accountSvc.CreateAsync(req.ClientSecretsJson);
            return Results.Created($"/api/accounts/{account.Id}", new
            {
                account.Id,
                account.Name,
                account.AddedAtUTC,
                account.ChannelTitle,
                IsAuthorized = false,
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IResult> Update(int id, UpdateAccountRequest req)
        => await accountSvc.UpdateAsync(id, req.Name) ? Results.Ok() : Results.NotFound();

    [HttpDelete("{id:int}")]
    public async Task<IResult> Delete(int id)
        => await accountSvc.DeleteAsync(id) ? Results.NoContent() : Results.NotFound();

    [HttpPost("{id:int}/authorize")]
    public async Task<IResult> Authorize(int id)
    {
        var scheme = Request.Scheme;
        var host = Request.Host;
        var redirectUri = $"{scheme}://{host}/api/accounts/oauth-callback";

        var url = await accountSvc.GetAuthorizationUrlAsync(id, redirectUri);
        if (url == null) return Results.NotFound();
        return Results.Ok(new { authorizationUrl = url });
    }

    [HttpPost("{id:int}/revoke")]
    public async Task<IResult> Revoke(int id)
        => await accountSvc.RevokeAsync(id) ? Results.Ok() : Results.NotFound();

    [HttpGet("oauth-callback")]
    public async Task<IResult> OAuthCallback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        if (!string.IsNullOrEmpty(error))
        {
            return Results.Content(BuildOAuthResultPage(false, $"Google denied access: {error}"), "text/html");
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return Results.Content(BuildOAuthResultPage(false, "Missing authorization code or state."), "text/html");
        }

        var scheme = Request.Scheme;
        var host = Request.Host;
        var redirectUri = $"{scheme}://{host}/api/accounts/oauth-callback";

        var (success, _, err) = await accountSvc.HandleOAuthCallbackAsync(code, state, redirectUri);
        return Results.Content(BuildOAuthResultPage(success, success ? "Authorization successful! You can close this tab." : err ?? "Unknown error"), "text/html");
    }

    private static string BuildOAuthResultPage(bool success, string message)
    {
        var icon = success ? "&#10003;" : "&#10007;";
        var color = success ? "#22c55e" : "#ef4444";
        var heading = success ? "Authorization Complete" : "Authorization Failed";
        var encodedMessage = System.Net.WebUtility.HtmlEncode(message);
        var successJs = success ? "true" : "false";

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <title>Vod2Tube - YouTube Authorization</title>
                <style>
                    body { font-family: 'Inter', -apple-system, system-ui, sans-serif; background: #0b1326; color: #dae2fd; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; }
                    .card { background: #171f33; border-radius: 16px; padding: 48px; text-align: center; max-width: 440px; box-shadow: 0 10px 30px rgba(6,14,32,0.5); border: 1px solid rgba(255,255,255,0.05); }
                    .icon { font-size: 64px; color: {{color}}; margin-bottom: 16px; }
                    h1 { font-size: 22px; margin: 0 0 12px; font-weight: 800; }
                    p { color: #c2c6d6; font-size: 14px; line-height: 1.6; margin: 0; }
                    .close-hint { margin-top: 24px; font-size: 12px; color: #6b7280; }
                </style>
            </head>
            <body>
                <div class="card">
                    <div class="icon">{{icon}}</div>
                    <h1>{{heading}}</h1>
                    <p>{{encodedMessage}}</p>
                    <p class="close-hint">You can safely close this tab and return to Vod2Tube.</p>
                </div>
                <script>
                    if (window.opener) {
                        window.opener.postMessage({ type: 'vod2tube-oauth-complete', success: {{successJs}} }, window.location.origin);
                    }
                </script>
            </body>
            </html>
            """;
    }
}

public record CreateAccountRequest(string ClientSecretsJson);
public record UpdateAccountRequest(string Name);
