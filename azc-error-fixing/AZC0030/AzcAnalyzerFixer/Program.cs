using AzcAnalyzerFixer.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AzcAnalyzerFixer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string projectEndpoint = "https://dotnet-sdk-analyzer-fix-resource.services.ai.azure.com/api/projects/dotnet-sdk-analyzer-fixer";
            string model = "gpt-4o";
            string mainTsp = @"C:\Users\radhgupta\Desktop\typespec\typesc-sdk-sample\src\main.tsp";
            string logPath = @"C:\Users\radhgupta\Desktop\typespec\typesc-sdk-sample\log\azc-errors.txt";
            string workspacePath = @"C:\Users\radhgupta\Desktop\typespec\typesc-sdk-sample";


            var agentService = new AzcAgentService(projectEndpoint, model);
            var buildService = new TypeSpecBuildService(workspacePath);
            try
            {
                // Step 0: Test connection and delete existing agents
                await agentService.TestConnectionAsync(CancellationToken.None).ConfigureAwait(false);
                await agentService.DeleteAgents(CancellationToken.None).ConfigureAwait(false);

                int iteration = 0;
                const int maxIterations = 5;
                bool errorsFixed = false;

                while (iteration < maxIterations && !errorsFixed)
                {
                    iteration++;
                    Console.WriteLine($"\n--- Iteration {iteration} ---");

                    // Step 1: Compile TypeSpec and generate SDK
                    await buildService.CompileTypeSpecAndGenerateSDKAsync().ConfigureAwait(false);
                    // Step 2: Prepare SDK files
                    await buildService.PrepareSDKFilesAsync().ConfigureAwait(false);
                    // Step 3: Build generated SDK
                    await buildService.BuildSDKAsync().ConfigureAwait(false);

                    //Step 4: check if AZC errors are fixed
                    int errorCount = buildService.GetAzcErrorCount();
                    errorsFixed = (errorCount == 0);
                    if (errorsFixed)
                    {
                        Console.WriteLine("✅  All AZC errors have been fixed. \n");
                        break;
                    }
                    else
                    {
                        Console.WriteLine("⚙️ Some AZC errors remain. Proceeding to the next iteration.");
                    }
                    // Step 4: Create Backup
                    await buildService.CreateTimestampedBackup().ConfigureAwait(false);
                    // Step 5: Fix AZC Errors
                    await agentService.fixAzcErrorsAsync(mainTsp, logPath).ConfigureAwait(false); 
                }
                if (!errorsFixed)
                {
                    Console.WriteLine("Reached maximum iterations. Some AZC errors could not be fixed.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Details: {ex.InnerException.Message}");
                }
                return;
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}