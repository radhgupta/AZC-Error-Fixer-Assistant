using AzcAnalyzerFixer.Core.Models;

namespace AzcAnalyzerFixer.Core.Interfaces
{
    public interface IPromptBuilder
    {
        /// <summary>
        /// Builds a single prompt that batches all the AZC‐fix snippets.
        /// </summary>
        string BuildAzcFixPrompt(IEnumerable<AzcError> errors, IEnumerable<IErrorFixerTool> fixerTools);

        /// <summary>
        /// Builds a prompt to fix compile‐time errors.
        /// </summary>
        string BuildCompileFixPrompt(string compileError);
    }
}