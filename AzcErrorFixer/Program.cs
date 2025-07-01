using System;
using System.Threading;
using System.Threading.Tasks;
using AzcAnalyzerFixer.Core.Interfaces;
using AzcAnalyzerFixer.Infrastructure.Services;
using AzcAnalyzerFixer.Configuration;
using AzcAnalyzerFixer.Logging;
using AzcAnalyzerFixer.Composition;
using AzcAnalyzerFixer.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AzcAnalyzerFixer
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var serviceProvider = AzcErrorFixerStartup.Configure();
            var logger = serviceProvider.GetRequiredService<ILoggerService>();
            var agentService = serviceProvider.GetRequiredService<IAzcAgentService>();
            var buildService = serviceProvider.GetRequiredService<ITypeSpecBuildService>();
            var fixerTools = serviceProvider.GetServices<IErrorFixerTool>();
            var promptBuilder = serviceProvider.GetRequiredService<IPromptBuilder>();

            string TypeSpecSrcPath = AppSettings.TypeSpecSrcPath;


            // Step 1: Initialize the Azure Foundary Agent
            logger.LogInfo("🔧 Starting AZC Analyzer Fixer...\n");
            await agentService.TestConnectionAsync(CancellationToken.None).ConfigureAwait(false);
            await agentService.DeleteAgentsAsync(CancellationToken.None).ConfigureAwait(false);
            await agentService.CreateAgentAsync(CancellationToken.None).ConfigureAwait(false);
            logger.LogInfo("✅ Connection successful.");

            int iteration = 0;
            bool errorsFixed = false;
            while (iteration < AppSettings.maxIterations && !errorsFixed)
            {
                logger.LogInfo($"\n🔄 -----Iteration {iteration + 1}/{AppSettings.maxIterations}-----\n");
                // Step 2: Compile TypeSpec and prepare SDK files
                string compilationErrors = await buildService.CompileTypeSpecAsync().ConfigureAwait(false);
                if (!string.IsNullOrEmpty(compilationErrors))
                {
                    logger.LogError($"❌ Compilation failed. Please provide valid TypeSpec files.\n{compilationErrors}");
                    break;
                }
                await buildService.PrepareSdkFilesAsync().ConfigureAwait(false);
                //Step 3: Capture AZC errors
                List<AzcError> analyzerErrors = await buildService.BuildSdkAsync().ConfigureAwait(false);
                if (analyzerErrors.Count == 0)
                {
                    logger.LogInfo("✅ No AZC errors found. Exiting.");
                    break;
                }
                else
                {
                    logger.LogInfo($"⚠️ Found {analyzerErrors.Count} AZC errors. Proceeding to fix them.");
                    foreach (var error in analyzerErrors)
                    {
                        logger.LogError($"- {error.Code}: {error.Message}");
                    }
                }
                //Step 4: Create backup of typespec files
                await buildService.CreateBackupAsync($"iteration-{iteration + 1}-").ConfigureAwait(false);

                //Step 5: Generate AZC fix suggestions
                string azcSuggestions = promptBuilder.BuildAzcFixPrompt(analyzerErrors, fixerTools);
                logger.LogInfo($"🔍 AZC suggestions to agent: {azcSuggestions}");

                //Step 6: Fix analyzer errors using AZC agent
                string threadId = await agentService.InitializeAgentEnvironmentAsync(TypeSpecSrcPath);
                await agentService.FixAzcErrorsAsync(TypeSpecSrcPath, azcSuggestions, threadId).ConfigureAwait(false);

                //Step 7: Compile TypeSpec again to check if errors are fixed
                bool isCompilationSuccessful = false;
                while (isCompilationSuccessful == false)
                {
                    string updatedCompilationErrors = await buildService.CompileTypeSpecAsync().ConfigureAwait(false);
                    string compilationError = promptBuilder.BuildCompileFixPrompt(updatedCompilationErrors);
                    isCompilationSuccessful = string.IsNullOrEmpty(updatedCompilationErrors);
                    if (!isCompilationSuccessful)
                    {
                        await agentService.FixAzcErrorsAsync(TypeSpecSrcPath, compilationError, threadId).ConfigureAwait(false);
                    }
                    else
                    {
                        logger.LogInfo("✅ All Compilation errors fixed successfully!");
                        break;
                    }
                }
                //Step 8: Delete the agent environment
                await agentService.CleanupAsync(CancellationToken.None).ConfigureAwait(false);

                iteration++;
            }
            logger.LogInfo("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
