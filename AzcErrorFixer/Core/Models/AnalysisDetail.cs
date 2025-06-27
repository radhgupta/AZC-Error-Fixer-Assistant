using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AzcAnalyzerFixer.Core.Models
{
    public class AnalysisDetails
    {
        [JsonPropertyName("total_azc_errors")]
        public int TotalAzcErrors { get; set; }

        [JsonPropertyName("error_types_found")]
        public List<string> ErrorTypesFound { get; set; } = new();

        [JsonPropertyName("models_requiring_fixes")]
        public List<string> ModelsRequiringFixes { get; set; } = new();
    }

    public class Fixes
    {
        [JsonPropertyName("model_renames")]
        public List<ModelRename> ModelRenames { get; set; } = new();

        [JsonPropertyName("reference_updates")]
        public List<ReferenceUpdate> ReferenceUpdates { get; set; } = new();

        [JsonPropertyName("structural_additions")]
        public List<StructuralAddition> StructuralAdditions { get; set; } = new();
    }

    public class ModelRename
    {
        [JsonPropertyName("original")]
        public string? Original { get; set; }

        [JsonPropertyName("fixed")]
        public string? Fixed { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }

    public class ReferenceUpdate
    {
        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("original")]
        public string? Original { get; set; }

        [JsonPropertyName("fixed")]
        public string? Fixed { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }

    public class StructuralAddition
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }
}
