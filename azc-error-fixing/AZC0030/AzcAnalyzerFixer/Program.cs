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
            string model = "gpt-35-turbo";
            string mainTsp = @"C:\Users\radhgupta\Desktop\typespec\typesc-sdk-sample\src\main.tsp";
            string logPath = @"C:\Users\radhgupta\Desktop\typespec\typesc-sdk-sample\log\azc-errors.txt";

            var agentService = new AzcAgentService(projectEndpoint, model);
            try
            {
                await agentService.fixAzcErrorsAsync(mainTsp, logPath).ConfigureAwait(false);
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
