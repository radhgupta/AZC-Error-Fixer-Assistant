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
Requirement: Add a clientName decorator.
Example:
```ts
@@clientName(Disk, \""ComputeDisk\"", \""csharp\"");```";
        }
    }
}