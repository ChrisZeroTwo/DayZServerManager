// Path: Core/Domain/DTOs/LaunchConfig.cs
// File: LaunchConfig.cs
// Purpose: Startparameter. Json-Mapping.

using System.Text.Json.Serialization;

namespace Core.Domain.DTOs;

public class LaunchConfig
{
    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("queryPort")]
    public int QueryPort { get; set; }

    [JsonPropertyName("steamQueryPort")]
    public int SteamQueryPort { get; set; }

    [JsonPropertyName("additionalArgs")]
    public string AdditionalArgs { get; set; } = string.Empty;
}
