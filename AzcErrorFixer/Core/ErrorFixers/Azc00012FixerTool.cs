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

**Rule**: Single-word model names are too generic and risk colliding with BCL or other libraries.
**Action**: For each such model, prefix the name with the main resource or service name defined in main.tsp. This prefix should be based on the primary model or the last segment of the namespace in main.tsp.

**Agent Task**:
- Use FileSearchTool to load main.tsp and extract the namespace or main model name.
- Use the last segment of the namespace or the main model as the prefix.
- For each generic single-word model (e.g., 'Wrapper'), update its clientName decorator in client.tsp to use the prefix, making the name more descriptive and unique.

**Example**:
If main.tsp defines:
```ts
namespace Azure.ResourceManager.Compute;
model ComputeDisk ...
```
Then:
```ts
// BAD:
@@clientName(Wrapper, ""Wrapper"", ""csharp"");

// GOOD:
@@clientName(Wrapper, ""ComputeWrapper"", ""csharp"");
```

- Ensure the final client.tsp remains valid TypeSpec 1.0+ syntax.
";

        }
    }
}