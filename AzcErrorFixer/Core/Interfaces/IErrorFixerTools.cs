namespace AzcAnalyzerFixer.Core.Interfaces
{
    public interface IErrorFixerTool
    {
        /// <summary>
        /// Can this tool handle the given AZC error code (e.g. "AZC0030").
        /// </summary>
        string Code { get; }

        bool CanHandle(string azcCode);

        /// <summary>
        /// Build the agent prompt snippet for this error message.
        /// </summary>
        string BuildPromptSnippet(string errorMessage);
    }
}