// Path: Core/Domain/DTOs/RconConfig.cs
// File: RconConfig.cs
// Purpose: RCON-Verbindungsdaten. Json-Mapping.

using System.Text.Json.Serialization;

namespace Core.Domain.DTOs;

public class RconConfig
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "127.0.0.1";

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}
