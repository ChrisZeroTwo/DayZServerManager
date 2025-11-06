// Path: Core/Domain/DTOs/ManagerConfig.cs
// File: ManagerConfig.cs
// Purpose: manager.json – Json-Mapping, damit auch hier alles eindeutig ist.

using System.Text.Json.Serialization;

namespace Core.Domain.DTOs;

public class ManagerConfig
{
    [JsonPropertyName("instancesPath")]
    public string InstancesPath { get; set; } = string.Empty;

    [JsonPropertyName("modCachePath")]
    public string ModCachePath { get; set; } = string.Empty;

    [JsonPropertyName("steamCmdPath")]
    public string SteamCmdPath { get; set; } = string.Empty;

    [JsonPropertyName("checkIntervalSeconds")]
    public int CheckIntervalSeconds { get; set; } = 10;

    [JsonPropertyName("modCheckIntervalSeconds")]
    public int ModCheckIntervalSeconds { get; set; } = 300;

    [JsonPropertyName("discordWebhook")]
    public string? DiscordWebhook { get; set; }

    [JsonPropertyName("webApi")]
    public WebApiOptions? WebApi { get; set; }
}

public class WebApiOptions
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; } = 8080;
}
