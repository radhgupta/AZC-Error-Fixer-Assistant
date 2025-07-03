using System.Text.Json.Serialization;

namespace AzcAnalyzerFixer.Core.Models
{
    public class AzcFixResult
    {
        
        [JsonPropertyName("UpdatedClientTsp")]
        public string? UpdatedClientTsp { get; set; }
    }
}
