// Path: Modules/WebApi/WebApiService.cs
// File: WebApiService.cs
// Purpose: Eingebettete ASP.NET Core Minimal API (Kestrel). Endpunkte für Status, Start/Stop, Restart, Discord-Ping.

using System.Text.Json.Serialization;
using Core.Domain;
using Core.Domain.DTOs;
using Core.Domain.Events;
using Core.Domain.Services;
using Core.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Modules.WebApi;

public class WebApiService : IWebApiService, IDisposable
{
    private readonly IServiceProvider _root;
    private readonly IConfigService _config;
    private readonly ILogService _log;

    private CancellationTokenSource? _cts;
    private Task? _runner;
    private bool _enabled;
    private int _port;

    public bool IsRunning { get; private set; }

    public WebApiService(IServiceProvider root, IConfigService config, ILogService log)
    {
        _root = root;
        _config = config;
        _log = log;
    }

    public void Start()
    {
        if (IsRunning) return;

        var w = _config.GetManagerConfig().WebApi ?? new WebApiOptions();
        _enabled = w.Enabled;
        _port = w.Port <= 0 ? 8080 : w.Port;

        if (!_enabled)
        {
            _log.Info("[WebApi] deaktiviert (manager.json: webApi.enabled=false).");
            return;
        }

        _cts = new CancellationTokenSource();
        _runner = Task.Run(() => RunAsync(_cts.Token));
        IsRunning = true;
        _log.Info($"[WebApi] startet auf http://0.0.0.0:{_port} …");
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _runner?.Wait(TimeSpan.FromSeconds(3));
        }
        catch { /* ignore */ }
        finally
        {
            IsRunning = false;
            _log.Info("[WebApi] gestoppt.");
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });

        // Kestrel URL festlegen
        builder.WebHost.UseUrls($"http://0.0.0.0:{_port}");

        // Services aus dem Root-Container verfügbar machen
        builder.Services.AddSingleton(_ => _root.GetRequiredService<IEventBus>());
        builder.Services.AddSingleton(_ => _root.GetRequiredService<IConfigService>());
        builder.Services.AddSingleton(_ => _root.GetRequiredService<IInstanceRegistry>());
        builder.Services.AddSingleton(_ => _root.GetRequiredService<IProcessController>());
        builder.Services.AddSingleton(_ => _root.GetRequiredService<IRestartOrchestrator>());
        builder.Services.AddSingleton(_ => _root.GetRequiredService<ILogService>());

        var app = builder.Build();

        app.UseRouting();

        // Health
        app.MapGet("/api/health", () => Results.Ok(new { ok = true, ts = DateTime.UtcNow }));

        // Instances: Liste
        app.MapGet("/api/instances", (IInstanceRegistry reg, IProcessController proc) =>
        {
            var items = reg.GetAll().Select(i => new InstanceDto(
                i.Name,
                proc.IsRunning(i.Name),
                proc.GetPid(i.Name) ?? 0,
                i.AutoStart
            ));
            return Results.Ok(items);
        });

        // Instance: Details
        app.MapGet("/api/instances/{name}", (string name, IInstanceRegistry reg, IProcessController proc) =>
        {
            var i = reg.GetByName(name);
            if (i is null) return Results.NotFound();
            return Results.Ok(new InstanceDetailDto(
                i.Name, i.ServerRoot, i.ProfilesPath, i.AutoStart,
                proc.IsRunning(i.Name), proc.GetPid(i.Name) ?? 0
            ));
        });

        // Start
        app.MapPost("/api/instances/{name}/start", (string name, IProcessController proc) =>
        {
            var ok = proc.Start(name);
            return ok ? Results.Accepted($"/api/instances/{name}") : Results.BadRequest(new { error = "start failed" });
        });

        // Stop (optional kill)
        app.MapPost("/api/instances/{name}/stop", (string name, bool? kill, IProcessController proc) =>
        {
            var ok = proc.Stop(name, kill == true);
            return ok ? Results.Accepted($"/api/instances/{name}") : Results.BadRequest(new { error = "stop failed" });
        });

        // Restart (Countdown)
        app.MapPost("/api/instances/{name}/restart", (string name, RestartRequest body, IRestartOrchestrator rst) =>
        {
            var secs = body?.Seconds is > 0 ? body!.Seconds : 60;
            var reason = string.IsNullOrWhiteSpace(body?.Reason) ? "Scheduled via API" : body!.Reason!;
            var startAfter = body?.AutoStartAfter ?? true;

            var ok = rst.ScheduleRestart(name, visibleSeconds: secs, totalSeconds: secs, reason: reason, autoStartAfter: startAfter);
            return ok ? Results.Accepted($"/api/instances/{name}") : Results.BadRequest(new { error = "schedule failed" });
        });

        // Discord Ping
        app.MapPost("/api/discord/ping", (PingRequest body, IEventBus bus) =>
        {
            var msg = string.IsNullOrWhiteSpace(body?.Message) ? "Ping via WebAPI" : body!.Message!;
            bus.Publish(new DiscordNotifyEvent("Ping", msg, "info"));
            return Results.Accepted("/api/discord/ping");
        });

        await app.StartAsync(ct);

        try
        {
            await app.WaitForShutdownAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // graceful
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync(); // <- richtig: DisposeAsync statt Dispose
        }
    }

    public void Dispose() => Stop();

    // --- DTOs (nur WebAPI) ---
    private record InstanceDto(string Name, bool Running, int Pid, bool AutoStart);
    private record InstanceDetailDto(string Name, string ServerRoot, string ProfilesPath, bool AutoStart, bool Running, int Pid);

    public class RestartRequest
    {
        [JsonPropertyName("seconds")] public int Seconds { get; set; } = 60;
        [JsonPropertyName("reason")] public string? Reason { get; set; } = "Scheduled via API";
        [JsonPropertyName("autoStartAfter")] public bool AutoStartAfter { get; set; } = true;
    }

    public class PingRequest
    {
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}
