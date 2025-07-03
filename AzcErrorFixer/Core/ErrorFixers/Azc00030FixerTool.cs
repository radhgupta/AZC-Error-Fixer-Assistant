using AzcAnalyzerFixer.Core.Interfaces;

namespace AzcAnalyzerFixer.Core.ErrorFixers
{
    public class Azc0030FixerTool : IErrorFixerTool
    {

        public string Code => "AZC0030";
        public bool CanHandle(string azcCode)
            => string.Equals(azcCode, "AZC0030", StringComparison.OrdinalIgnoreCase);

        public string BuildPromptSnippet(string errorMessage)
        {
            return $@"#### Fix AZC0030
Error: {errorMessage.Trim()}
Requirement: Model names should not end with reserved or discouraged suffixes (like 'Options', 'Data', etc.).
Action: Rename the model to avoid the suffix, following Azure SDK guidelines. Use the analyzer's suggested name or another appropriate name.

How to choose a suggested name:
- Remove the discouraged suffix (e.g., 'Options').
- Use a more descriptive or specific suffix such as 'Config', 'Details', or another meaningful term.
- Ensure the new name matches a model defined in main.tsp.

Example:
```ts
// BAD (do NOT do this):
model DiskConfig {{ ... }} // Do not rename or redefine the model here

// GOOD (do this):
@@clientName(DiskOptions, ""DiskConfig"", ""csharp"");
```

- If a clientName decorator is present, update its argument to match the new model name.
- Only update the @@clientName decorator. Do not rename or redefine the model in client.tsp.
- Ensure the final client.tsp remains valid TypeSpec 1.0+ syntax.
";
        }
    }
}