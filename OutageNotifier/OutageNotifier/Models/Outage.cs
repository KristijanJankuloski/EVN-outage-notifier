using System.Text.Json.Serialization;

namespace OutageNotifier.Models;

public sealed class Outage
{
    [JsonPropertyName("prekinID")]
    public int PrekinId { get; set; }

    [JsonPropertyName("kecId")]
    public string? KecId { get; set; }

    [JsonPropertyName("tipPrekin")]
    public string? TipPrekin { get; set; }

    [JsonPropertyName("nasMesto")]
    public string? NasMesto { get; set; }

    [JsonPropertyName("adresa")]
    public string? Adresa { get; set; }

    [JsonPropertyName("pocetok")]
    public DateTimeOffset? Pocetok { get; set; }

    [JsonPropertyName("kraj")]
    public DateTimeOffset? Kraj { get; set; }

    [JsonPropertyName("napNivo")]
    public string? NapNivo { get; set; }
}
