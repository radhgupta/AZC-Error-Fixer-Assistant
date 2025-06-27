using System.Text.Json.Serialization;

namespace AzcAnalyzerFixer.Core.Models
{
    public class AzcFixResult
    {
        [JsonPropertyName("analysis")]
        public AnalysisDetails? Analysis { get; set; }

        [JsonPropertyName("fixes")]
        public Fixes? Fixes { get; set; }

        [JsonPropertyName("UpdatedClientTsp")]
        public string? UpdatedClientTsp { get; set; }
    }
}
