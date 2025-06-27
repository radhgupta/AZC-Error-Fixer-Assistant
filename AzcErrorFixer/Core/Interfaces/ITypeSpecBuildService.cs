using System.Threading.Tasks;

namespace AzcAnalyzerFixer.Core.Interfaces
{
    public interface ITypeSpecBuildService
    {
        Task CompileTypeSpecAsync();
        Task PrepareSdkFilesAsync();
        Task BuildSdkAsync();
        Task CreateBackupAsync(string prefix = "");
        int GetAzcErrorCount();
    }
}