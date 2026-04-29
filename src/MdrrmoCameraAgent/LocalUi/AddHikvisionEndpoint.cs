using MdrrmoCameraAgent.Hikvision;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace MdrrmoCameraAgent.LocalUi;

public sealed class AddHikvisionEndpoint
{
    public static async Task HandleListAsync(HttpContext ctx)
    {
        ListHikvisionRequest? req;
        try
        {
            req = await JsonSerializer.DeserializeAsync<ListHikvisionRequest>(
                ctx.Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                ctx.RequestAborted);
        }
        catch
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "Invalid JSON body" });
            return;
        }

        if (req is null || string.IsNullOrWhiteSpace(req.NvrIp))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "nvr_ip is required" });
            return;
        }

        var baseUrl = $"http://{req.NvrIp}";
        var user = req.User ?? "admin";
        var pass = req.Pass ?? string.Empty;

        // Warn if using known defaults (non-blocking).
        bool isDefault = pass is "12345" or "Admin12345" or "admin";

        var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var isapi = new IsapiClient(http, user, pass);

        IReadOnlyList<HikvisionChannel> channels;
        try
        {
            channels = await isapi.ListChannelsAsync(ctx.RequestAborted);
        }
        catch (HttpRequestException ex)
        {
            ctx.Response.StatusCode = 502;
            await ctx.Response.WriteAsJsonAsync(new { error = $"ISAPI request failed: {ex.Message}" });
            return;
        }

        await ctx.Response.WriteAsJsonAsync(new
        {
            channels = channels.Select(c => new { id = c.Id, name = c.Name }),
            default_creds_warning = isDefault
                ? "Using factory-default credentials — change the NVR password immediately."
                : null as string,
        });
    }
}

public sealed record ListHikvisionRequest(string? NvrIp, string? User, string? Pass);
