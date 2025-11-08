// Path: Core/Domain/DTOs/ManagerConfig.cs
// File: ManagerConfig.cs
// Purpose: DTO für manager.json – inkl. Steam, Discord, WebAPI, Monitoring und Update-Intervall.

using System.Text.Json.Serialization;

namespace Core.Domain.DTOs;

public class ManagerConfig
{
    [JsonPropertyName("instancesPath")] public string InstancesPath { get; set; } = string.Empty;
    [JsonPropertyName("modCachePath")] public string ModCachePath { get; set; } = string.Empty;
    [JsonPropertyName("steamCmdPath")] public string SteamCmdPath { get; set; } = string.Empty;

    // NEU: Intervall für Auto-Update-Check (Minuten)
    [JsonPropertyName("updateCheckIntervalMinutes")] public int UpdateCheckIntervalMinutes { get; set; } = 15;

    [JsonPropertyName("steam")] public SteamCredentials? Steam { get; set; }

    [JsonPropertyName("checkIntervalSeconds")] public int CheckIntervalSeconds { get; set; } = 10;
    [JsonPropertyName("modCheckIntervalSeconds")] public int ModCheckIntervalSeconds { get; set; } = 300;

    [JsonPropertyName("freezeThresholdSeconds")] public int FreezeThresholdSeconds { get; set; } = 120;
    [JsonPropertyName("monitorDebounceSeconds")] public int MonitorDebounceSeconds { get; set; } = 300;
    [JsonPropertyName("monitorOnlyWhenRunning")] public bool MonitorOnlyWhenRunning { get; set; } = true;

    [JsonPropertyName("discordWebhook")] public string? DiscordWebhook { get; set; }
    [JsonPropertyName("discord")] public DiscordOptions? Discord { get; set; }

    [JsonPropertyName("webApi")] public WebApiOptions? WebApi { get; set; }
}

public class WebApiOptions
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("port")] public int Port { get; set; } = 8080;
}

public class DiscordOptions
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("webhook")] public string? Webhook { get; set; }
    [JsonPropertyName("notify")] public DiscordNotifyFlags Notify { get; set; } = new();
}

public class DiscordNotifyFlags
{
    [JsonPropertyName("generic")] public bool Generic { get; set; } = true;
    [JsonPropertyName("instanceStarted")] public bool InstanceStarted { get; set; } = true;
    [JsonPropertyName("instanceStopped")] public bool InstanceStopped { get; set; } = true;
    [JsonPropertyName("restartScheduled")] public bool RestartScheduled { get; set; } = true;
    [JsonPropertyName("restartCompleted")] public bool RestartCompleted { get; set; } = true;
    [JsonPropertyName("monitorFreeze")] public bool MonitorFreeze { get; set; } = true;
    [JsonPropertyName("modUpdateAvailable")] public bool ModUpdateAvailable { get; set; } = true;
    [JsonPropertyName("instanceNeedsRestart")] public bool InstanceNeedsRestart { get; set; } = true;
    [JsonPropertyName("instanceUpdateCompleted")] public bool InstanceUpdateCompleted { get; set; } = true;
    [JsonPropertyName("rconCommands")] public bool RconCommands { get; set; } = false;
}

public class SteamCredentials
{
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
    [JsonPropertyName("guardCode")] public string? GuardCode { get; set; }
}
