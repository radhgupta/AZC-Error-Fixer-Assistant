using System.Collections.Generic;
using System.Text;
using AzcAnalyzerFixer.Core.Interfaces;
using AzcAnalyzerFixer.Core.Models;

namespace AzcAnalyzerFixer.Core.Prompting
{
    public class AzcPromptBuilder : IPromptBuilder
    {
        public string BuildAzcFixPrompt(IEnumerable<AzcError> errors, IEnumerable<IErrorFixerTool> fixerTools)
        {
            // 1) Map errors to snippets
            var snippets = errors
                .SelectMany(err => fixerTools
                    .Where(t => t.CanHandle(err.Code))
                    .Select(t => t.BuildPromptSnippet(err.Message)))
                .ToList();

            // 2) Build the batched prompt
            var sb = new StringBuilder();
            sb.AppendLine("Please fix all AZC violations using FileSearchTool:")
              .AppendLine();

            foreach (var s in snippets)
            {
                sb.AppendLine(s).AppendLine();
            }

            sb.AppendLine(@"
Return **only** this JSON:
{
  ""UpdatedClientTsp"": ""<complete client.tsp content here>""

}
Do not include any additional text or explanations outside of this JSON structure.");
            return sb.ToString();
        }

        public string BuildCompileFixPrompt(string compileError)
        {
            var sb = new StringBuilder();
            sb.AppendLine("The updated client.tsp you provided generated the following compilation error:")
            .AppendLine()
            .AppendLine(compileError)
            .AppendLine()
            .AppendLine(@"Please fix all the issues in client.tsp so that it compiles cleanly, and return **only** this JSON:
        {
            ""UpdatedClientTsp"": ""<complete client.tsp content here>""
        }
        Do not include any additional text or explanations outside of this JSON structure.");
            
            return sb.ToString();
        }
    }
}