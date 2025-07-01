using System.Threading;
using System.Threading.Tasks;

namespace AzcAnalyzerFixer.Core.Interfaces
{
    public interface IAzcAgentService
    {
        Task TestConnectionAsync(CancellationToken ct);
        Task DeleteAgentsAsync(CancellationToken ct);
        Task CreateAgentAsync(CancellationToken ct);
        Task<string> InitializeAgentEnvironmentAsync(string tspFolderPath);
        Task FixAzcErrorsAsync(string tspFolderPath, string azcSuggestions, string threadId);
        Task CleanupAsync(CancellationToken ct);
    }
}
