using System.Threading;
using System.Threading.Tasks;

namespace AzcAnalyzerFixer.Core.Interfaces
{
    public interface IAzcAgentService
    {
        Task TestConnectionAsync(CancellationToken ct);
        Task DeleteAgentsAsync(CancellationToken ct);
        Task FixAzcErrorsAsync(string mainTspPath, string logPath);
    }
}
