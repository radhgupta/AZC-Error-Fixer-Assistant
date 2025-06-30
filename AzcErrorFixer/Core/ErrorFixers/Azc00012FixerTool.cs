using AzcAnalyzerFixer.Core.Interfaces;

namespace AzcAnalyzerFixer.Core.ErrorFixers
{
    public class  Azc0012FixerTool : IErrorFixerTool
    {
        public string Code => "AZC0012";
        public bool CanHandle(string azcCode)
            => string.Equals(azcCode, "AZC0012", StringComparison.OrdinalIgnoreCase);

        public string BuildPromptSnippet(string errorMessage)
        {
            return $@"
#### Fix AZC0012
Error: {errorMessage.Trim()}

**Rule**: Single-word model names are too generic and risk colliding with BCL or other libs.  
**Action**: For each such model, **infer** your service name from your top-level TypeSpec namespace (e.g. if your namespace is `Azure.ResourceManager`, use `ResourceManager`), then **prefix** the model name with that service identifier.

**Agent Task**:  
- Load `main.tsp` via FileSearchTool and extract the namespace.  
- Use its last segment as the service prefix.  
- Update each model reference in `client.tsp` using the decorator:
  ```ts
  @@clientName(OriginalModel, ""[ServicePrefix]OriginalModel"", ""csharp"");
  -Ensure the final client.tsp remains valid TypeSpec 1.0+ syntax.
  -Example:
  ```ts
  @@clientName(DiskOptions, ""<ServicePrefix>DiskOptions"", ""csharp"");```";

        }
    }
}