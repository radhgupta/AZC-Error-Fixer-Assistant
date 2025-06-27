using System;
using System.Threading;
using System.Threading.Tasks;
using AzcAnalyzerFixer.Core.Interfaces;
using AzcAnalyzerFixer.Infrastructure.Services;
using AzcAnalyzerFixer.Configuration;
using AzcAnalyzerFixer.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace AzcAnalyzerFixer
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            int maxIterations = AppSettings.maxIterations;
            string projectEndpoint = AppSettings.ProjectEndpoint;
            string model = AppSettings.Model;
            var serviceProvider = Startup.Configure();

            var logger = serviceProvider.GetRequiredService<ILoggerService>();
            var agentService = serviceProvider.GetRequiredService<IAzcAgentService>();
            var buildService = serviceProvider.GetRequiredService<ITypeSpecBuildService>();

            try
            {
                logger.LogInfo("🔧 Starting AZC Analyzer Fixer...\n");
                await agentService.TestConnectionAsync(CancellationToken.None).ConfigureAwait(false);
                await agentService.DeleteAgentsAsync(CancellationToken.None).ConfigureAwait(false);
                logger.LogInfo("✅ Connection successful.");

                int iteration = 0;
                bool errorsFixed = false;
                while (iteration < maxIterations && !errorsFixed)
                {
                    logger.LogInfo($"\n🔄 -----Iteration {iteration + 1}/{maxIterations}-----\n");
                    try
                    {
                        await buildService.CompileTypeSpecAsync().ConfigureAwait(false);
                        await buildService.PrepareSdkFilesAsync().ConfigureAwait(false);
                        await buildService.BuildSdkAsync().ConfigureAwait(false);

                        errorsFixed = buildService.GetAzcErrorCount() == 0;
                        if (errorsFixed)
                        {
                            logger.LogInfo("✅ All AZC errors have been fixed.");
                            break;
                        }

                        await buildService.CreateBackupAsync().ConfigureAwait(false);
                        await agentService.FixAzcErrorsAsync(Configuration.AppSettings.TypeSpecSrcPath,Configuration.AppSettings.LogPath);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError($"❌ Error during iteration {iteration + 1}: {ex.Message}");
                    }

                    iteration++;
                }
                if (!errorsFixed)
                {
                    logger.LogInfo("⚠️ Reached maximum iterations. Some AZC errors remain.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"❌ Connection failed: {ex.Message}");
                if (ex.InnerException != null)
                {
                    logger.LogError($"Details: {ex.InnerException.Message}");
                }
            }
            logger.LogInfo("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
