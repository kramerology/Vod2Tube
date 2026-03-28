using Microsoft.AspNetCore.Mvc;

namespace Vod2Tube.Api.Controllers;

[ApiController]
[Route("api/filesystem")]
public class FilesystemController : ControllerBase
{
    [HttpGet("browse")]
    public IResult Browse([FromQuery] string? path)
    {
        if (HttpContext.Connection.RemoteIpAddress is not { } remoteIp || !System.Net.IPAddress.IsLoopback(remoteIp))
            return Results.Json(new { error = "This endpoint is only accessible from localhost" }, statusCode: 403);

        try
        {
            string current = string.IsNullOrWhiteSpace(path)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(path);

            while (!Directory.Exists(current))
            {
                string? parent = Path.GetDirectoryName(current);
                if (parent == null || parent == current)
                {
                    current = Directory.GetCurrentDirectory();
                    break;
                }
                current = parent;
            }

            var dirs = Directory.EnumerateDirectories(current)
                .Select(d => new { name = Path.GetFileName(d), fullPath = d })
                .OrderBy(d => d.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var files = Directory.EnumerateFiles(current)
                .Select(f => new { name = Path.GetFileName(f), fullPath = f })
                .OrderBy(f => f.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            string? parentPath = Path.GetDirectoryName(current);

            string[]? drives = OperatingSystem.IsWindows()
                ? DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName).ToArray()
                : null;

            return Results.Ok(new { currentPath = current, parentPath, directories = dirs, files, drives });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Json(new { error = "Access denied" }, statusCode: 403);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("reveal")]
    public IResult Reveal(RevealRequest req)
    {
        if (HttpContext.Connection.RemoteIpAddress is not { } remoteIp || !System.Net.IPAddress.IsLoopback(remoteIp))
            return Results.Json(new { error = "This endpoint is only accessible from localhost" }, statusCode: 403);

        if (string.IsNullOrWhiteSpace(req.Path))
            return Results.BadRequest(new { error = "Path is required" });

        try
        {
            string fullPath = Path.GetFullPath(req.Path);
            System.Diagnostics.ProcessStartInfo psi;

            if (OperatingSystem.IsWindows())
            {
                if (!System.IO.File.Exists(fullPath) && !Directory.Exists(fullPath))
                    return Results.NotFound(new { error = "Path does not exist" });

                psi = new System.Diagnostics.ProcessStartInfo("explorer.exe")
                {
                    UseShellExecute = false,
                    Arguments = System.IO.File.Exists(fullPath) ? $"/select,\"{fullPath}\"" : $"\"{fullPath}\"",
                };
            }
            else if (OperatingSystem.IsMacOS())
            {
                if (!System.IO.File.Exists(fullPath) && !Directory.Exists(fullPath))
                    return Results.NotFound(new { error = "Path does not exist" });

                psi = new System.Diagnostics.ProcessStartInfo("open") { UseShellExecute = false };
                if (System.IO.File.Exists(fullPath))
                    psi.ArgumentList.Add("-R");
                psi.ArgumentList.Add(fullPath);
            }
            else
            {
                string folder = System.IO.File.Exists(fullPath) ? (Path.GetDirectoryName(fullPath) ?? fullPath) : fullPath;
                if (!Directory.Exists(folder))
                    return Results.NotFound(new { error = "Path does not exist" });

                psi = new System.Diagnostics.ProcessStartInfo("xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(folder);
            }

            System.Diagnostics.Process.Start(psi);
            return Results.Ok();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public record RevealRequest(string Path);
