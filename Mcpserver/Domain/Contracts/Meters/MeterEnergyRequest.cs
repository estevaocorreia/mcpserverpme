using System.Text.Json.Serialization;

namespace Mcpserver.Domain.Contracts.Meters;

public sealed class MeterEnergyRequest
{
    [JsonPropertyName("inicio")]
    public string Inicio { get; set; } = "";

    [JsonPropertyName("fim")]
    public string Fim { get; set; } = "";

    [JsonPropertyName("displayNames")]
    public List<string> DisplayNames { get; set; } = new();

    [JsonPropertyName("take")]
    public int Take { get; set; } = 1000;
}