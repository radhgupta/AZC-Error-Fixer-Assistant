using System.Threading.Tasks;
using AzcAnalyzerFixer.Core.Models;

namespace AzcAnalyzerFixer.Core.Interfaces
{
    public interface ITypeSpecBuildService
    {
        Task CompileTypeSpecAsync();
        Task PrepareSdkFilesAsync();
        Task BuildSdkAsync();
        Task CreateBackupAsync(string prefix = "");
        List<AzcError> GetAzcErrorsDetails();
        int GetAzcErrorCount();
    }
}