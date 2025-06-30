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
        private readonly string logPath;
        private readonly string sdkOutputPath = "final-output";
        private readonly ILoggerService logger;

        public TypeSpecBuildService(string workspacePath, ILoggerService logger)
        {
            this.workspacePath = workspacePath;
            this.helperPath = Path.Combine(workspacePath, "helper");
            this.logPath = Path.Combine(workspacePath, "log");
            this.logger = logger;
        }

        public async Task CompileTypeSpecAsync()
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

            if (process.ExitCode != 0)
            {
                throw new Exception($"❌ TypeSpec compilation failed with exit code {process.ExitCode}");
            }

            logger.LogInfo("✅ TypeSpec compilation completed successfully.\n");
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

        public async Task BuildSdkAsync()
        {
            logger.LogInfo("⏳ Building SDK...\n");

            var outputPath = Path.Combine(workspacePath, sdkOutputPath);
            var csprojPath = FindGeneratedCsprojFile(outputPath);

            if (string.IsNullOrEmpty(csprojPath))
                throw new Exception("Could not find generated .csproj file for building.");

            var buildDir = Path.GetDirectoryName(csprojPath);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "build --no-incremental",
                    WorkingDirectory = buildDir,
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

            Directory.CreateDirectory(logPath);

            var fullBuildOutput = $"{output}\n{error}";
            var azcErrors = ExtractAzcErrors(fullBuildOutput);

            var azcErrorPath = Path.Combine(logPath, "azc-errors.txt");
            var azcBackupPath = Path.Combine(logPath, $"azc-errors-{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            await File.WriteAllTextAsync(azcErrorPath, azcErrors);
            await File.WriteAllTextAsync(azcBackupPath, azcErrors);

            logger.LogInfo($"The generator found following AZC errors:\n{azcErrors}\n");

            var logFile = Path.Combine(logPath, "build-output.log");
            await File.WriteAllTextAsync(logFile, fullBuildOutput);

            if (process.ExitCode != 0)
            {
                throw new Exception($"SDK build failed with exit code {process.ExitCode}. Check log: {logFile}");
            }

            logger.LogInfo("✅ SDK build completed.\n");
        }

        public List<AzcError> GetAzcErrorsDetails()
        {
            var path = Path.Combine(logPath, "azc-errors.txt");
            if (!File.Exists(path)) return new List<AzcError>();

            var rx = new Regex(@"(?<code>AZC\d{4}):\s*(?<msg>.*)", RegexOptions.Compiled);
            var list = new List<AzcError>();
            foreach (var line in File.ReadAllLines(path))
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

            // Use ZipFile to compress the src folder
            await Task.Run(() => ZipFile.CreateFromDirectory(srcFolder, backupZipPath, CompressionLevel.Optimal, includeBaseDirectory: false));

            logger.LogInfo($"📦 Backup created at: {backupZipPath}");
        }

        public int GetAzcErrorCount()
        {
            var path = Path.Combine(logPath, "azc-errors.txt");
            if (!File.Exists(path)) return 0;

            return File.ReadLines(path)
                       .Count(line => !string.IsNullOrWhiteSpace(line));
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