using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using AzcAnalyzerFixer.Core.Interfaces;
using AzcAnalyzerFixer.Logging;
using AzcAnalyzerFixer.Core.Models;
using System.Text.RegularExpressions;

namespace AzcAnalyzerFixer.Infrastructure.Services
{
    public class TypeSpecBuildService : ITypeSpecBuildService
    {
        private readonly string workspacePath;
        private readonly string helperPath;
        private readonly string sdkOutputPath = "final-output";
        private readonly ILoggerService logger;

        public TypeSpecBuildService(string workspacePath, ILoggerService logger)
        {
            this.workspacePath = workspacePath;
            this.helperPath = Path.Combine(workspacePath, "helper");
            this.logger = logger;
        }

        public async Task<string> CompileTypeSpecAsync()
        {
            logger.LogInfo("⏳ Compiling TypeSpec and generating SDK...\n");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c npx tsp compile ./src --output-dir {sdkOutputPath}",
                    WorkingDirectory = workspacePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var compileLog = $"{output}\n{error}";

            if (process.ExitCode != 0)
            {
                return compileLog;
            }

            logger.LogInfo("✅ TypeSpec compilation completed successfully.\n");
            return string.Empty;
        }

        public async Task PrepareSdkFilesAsync()
        {
            logger.LogInfo("⏳ Preparing SDK files...\n");

            var outputPath = Path.Combine(workspacePath, sdkOutputPath);
            var csprojPath = FindGeneratedCsprojFile(outputPath);

            if (string.IsNullOrEmpty(csprojPath))
                throw new Exception("Could not find generated .csproj file in output directory.");

            var generatedDir = Path.GetDirectoryName(csprojPath)!;

            // Copy NuGet config
            var nugetSrc = Path.Combine(helperPath, "Nuget.config");
            var nugetDest = Path.Combine(generatedDir, "Nuget.config");
            File.Copy(nugetSrc, nugetDest, overwrite: true);

            // Replace .csproj with helper template
            var csprojTemplate = Path.Combine(helperPath, "Azure.ResourceManager.csproj");
            var content = await File.ReadAllTextAsync(csprojTemplate);
            await File.WriteAllTextAsync(csprojPath, content);
        }

        public async Task<List<AzcError>> BuildSdkAsync()
        {
            logger.LogInfo("⏳ Building SDK...\n");

            var csprojPath = FindGeneratedCsprojFile(Path.Combine(workspacePath, sdkOutputPath));

            if (string.IsNullOrEmpty(csprojPath))
                throw new Exception("Could not find generated .csproj file for building.");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "build --no-incremental",
                    WorkingDirectory = Path.GetDirectoryName(csprojPath),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var fullBuildOutput = $"{output}\n{error}";
            var azcErrors = ExtractAzcErrors(fullBuildOutput);

            if (process.ExitCode != 0)
            {
                throw new Exception($"SDK build failed with exit code {process.ExitCode}.\n{fullBuildOutput}");
            }

            logger.LogInfo("✅ SDK build completed.\n");
            return GetAzcErrorsDetails(azcErrors);
        }

        private List<AzcError> GetAzcErrorsDetails(string buildLog)
        {
            var rx = new Regex(@"(?<code>AZC\d{4}):\s*(?<msg>.*)", RegexOptions.Compiled);
            var list = new List<AzcError>();
            
            foreach (var line in buildLog.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = rx.Match(line);
                if (m.Success)
                    list.Add(new AzcError {
                        Code    = m.Groups["code"].Value,
                        Message = m.Groups["msg"].Value
                    });
            }
            
            return list;
        }

        public async Task CreateBackupAsync(string prefix = "")
        {
            string srcFolder = Path.Combine(workspacePath, "src");
            string backupRoot = Path.Combine(workspacePath, "backups");
            Directory.CreateDirectory(backupRoot);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupName = $"{prefix}src-backup-{timestamp}.zip";
            string backupZipPath = Path.Combine(backupRoot, backupName);

            await Task.Run(() => ZipFile.CreateFromDirectory(srcFolder, backupZipPath, CompressionLevel.Optimal, includeBaseDirectory: false));

            logger.LogInfo($"📦 Backup created at: {backupZipPath}");
        }

        private string FindGeneratedCsprojFile(string searchPath)
        {
            if (!Directory.Exists(searchPath))
            {
                return null;
            }

            return Directory.GetFiles(searchPath, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
        }

        private string ExtractAzcErrors(string buildLog)
        {
            var lines = buildLog
                .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                // only pick AZC lines
                .Where(line => line.Contains("AZC", StringComparison.OrdinalIgnoreCase))
                // remove exact duplicates
                .Distinct();

            return string.Join(Environment.NewLine, lines);
        }
    }
}