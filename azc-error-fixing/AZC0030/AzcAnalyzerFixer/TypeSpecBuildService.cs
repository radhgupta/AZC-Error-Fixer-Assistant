using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;

namespace AzcAnalyzerFixer.Services
{
    public class TypeSpecBuildService
    {
        private readonly string workspacePath;
        private readonly string helperPath;
        private readonly string logPath;
        private readonly string sdkOutputPath;

        public TypeSpecBuildService(string workspacePath)
        {
            this.workspacePath = workspacePath;
            this.helperPath = Path.Combine(workspacePath, "helper");
            this.logPath = Path.Combine(workspacePath, "log");
            this.sdkOutputPath = "final-output";
        }

        public async Task CompileTypeSpecAndGenerateSDKAsync()
        {
            Console.WriteLine("⏳ Compiling TypeSpec and generating SDK...\n" );

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c npx tsp compile src/main.tsp --output-dir {sdkOutputPath}",
                    WorkingDirectory = workspacePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // Capture the output for debugging
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new Exception($"❌ TypeSpec compilation failed with exit code {process.ExitCode}");
            }

            Console.WriteLine("✅ TypeSpec compilation completed successfully.\n");
        }

        public async Task PrepareSDKFilesAsync()
        {
            Console.WriteLine("⏳ Preparing SDK files...\n");
            var tspOutputPath = Path.Combine(workspacePath, sdkOutputPath);
            var generatedCsprojPath = FindGeneratedCsprojFile(tspOutputPath);

            if (string.IsNullOrEmpty(generatedCsprojPath))
            {
                throw new Exception("Could not find generated .csproj file in tsp-output directory");
            }

            var generatedProjectDir = Path.GetDirectoryName(generatedCsprojPath);
            // Console.WriteLine($"Found generated project at: {generatedProjectDir}");

            // Copy Nuget.config to the same directory as the generated .csproj
            var sourceNugetConfig = Path.Combine(helperPath, "Nuget.config");
            var targetNugetConfig = Path.Combine(generatedProjectDir, "Nuget.config");
            File.Copy(sourceNugetConfig, targetNugetConfig, overwrite: true);
            // Console.WriteLine($"Copied Nuget.config from {sourceNugetConfig} to {targetNugetConfig}");

            //Copy .csproj contents from helper to generated project
            var sourceCsproj = Path.Combine(helperPath, "Azure.ResourceManager.csproj");
            var csprojContent = await File.ReadAllTextAsync(sourceCsproj);
            await File.WriteAllTextAsync(generatedCsprojPath, csprojContent);
            // Console.WriteLine($"Updated .csproj file at {generatedCsprojPath}");
        }

        public async Task BuildSDKAsync()
        {
            Console.WriteLine("⏳Building SDK...\n");

            // Find the generated .csproj file in the output directory
            var tspOutputPath = Path.Combine(workspacePath, sdkOutputPath);
            var generatedCsprojPath = FindGeneratedCsprojFile(tspOutputPath);

            if (string.IsNullOrEmpty(generatedCsprojPath))
            {
                throw new Exception("Could not find generated .csproj file for building");
            }

            var generatedProjectDir = Path.GetDirectoryName(generatedCsprojPath);
            // Console.WriteLine($"Building project at: {generatedProjectDir}");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "build --no-incremental",
                    WorkingDirectory = generatedProjectDir, // Use the directory containing the .csproj
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // Capture the output for error analysis
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            // Ensure log directory exists
            Directory.CreateDirectory(logPath);
            
            var fullBuildOutput = output + Environment.NewLine + error;

            // Save AZC errors to a log file
            var azcErrors = ExtractAzcErrors(fullBuildOutput);
            var azcErrorsPath = Path.Combine(logPath, "azc-errors.txt");
            var azctimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var azcErrorsBackupPath = Path.Combine(logPath, $"azc-errors-{azctimestamp}.txt");
            await File.WriteAllTextAsync(azcErrorsPath, azcErrors);
            await File.WriteAllTextAsync(azcErrorsBackupPath, azcErrors);

            Console.WriteLine($"The generator found following AZC errors:\n{azcErrors}\n");

            // Save full build log for reference
            var buildLogPath = Path.Combine(logPath, "build-output.log");
            await File.WriteAllTextAsync(buildLogPath, fullBuildOutput);

            if (process.ExitCode != 0)
            {
                throw new Exception($"SDK build failed with exit code {process.ExitCode}. Check {buildLogPath} for details.");
            }

            Console.WriteLine($"✅ SDK build completed.\n");
        }

        public async Task CreateTimestampedBackup(string prefix = "")
        {
            var mainTspPath = Path.Combine(workspacePath, "src", "main.tsp");
            var backupDir = Path.Combine(workspacePath, "backups");
            Directory.CreateDirectory(backupDir);
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"main.tsp.{timestamp}.bak";
            var backupPath = Path.Combine(backupDir, fileName);
        
            File.Copy(mainTspPath, backupPath, true);
            // Console.WriteLine($"Backup created at {backupPath}");
        }
        private string FindGeneratedCsprojFile(string searchPath)
        {
            if (!Directory.Exists(searchPath))
            {
                return null;
            }

            // Search for .csproj files recursively
            var csprojFiles = Directory.GetFiles(searchPath, "*.csproj", SearchOption.AllDirectories);

            // Return the first .csproj file found (typically there should be only one)
            return csprojFiles.FirstOrDefault();
        }

        private string ExtractAzcErrors(string buildLog)
        {
            // TODO: Implement proper error extraction logic
            // For now, just look for lines containing AZC0030
            var lines = buildLog.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(Environment.NewLine,
                lines.Where(line => line.Contains("AZC", StringComparison.OrdinalIgnoreCase)));
        }

        public int GetAzcErrorCount()
        {
            var azcErrorsPath = Path.Combine(logPath, "azc-errors.txt");
            if (!File.Exists(azcErrorsPath)) return 0;

            var errors = File.ReadAllLines(azcErrorsPath);
            return errors.Count(line => !string.IsNullOrWhiteSpace(line));
        }
    }
}
