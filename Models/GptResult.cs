using System.Text.Json.Serialization;

public class GptResult
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = null!;
    [JsonPropertyName("extraTags")]
    public List<string> ExtraTags { get; set; } = null!;
}