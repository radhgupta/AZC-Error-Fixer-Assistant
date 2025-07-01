using System.Threading.Tasks;
using AzcAnalyzerFixer.Core.Models;

namespace AzcAnalyzerFixer.Core.Interfaces
{
    public interface ITypeSpecBuildService
    {
        Task<string> CompileTypeSpecAsync();
        Task PrepareSdkFilesAsync();
        Task<List<AzcError>> BuildSdkAsync();
        Task CreateBackupAsync(string prefix = "");
    }
}