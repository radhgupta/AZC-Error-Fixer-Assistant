using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Azure;
using System;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AzcAnalyzerFixer.Services
{
    public class AzcAgentService
    {
        private readonly PersistentAgentsClient client;
        private PersistentAgent agent;
        private readonly string model;
        private readonly string projectEndpoint;
        private const string AzcQueryPrompt = @"You are an expert on Azure SDK naming conventions and the AZC0030 analyzer rule (model names ending in disallowed suffixes Request, Response, Options).
                                        I will give you:
                                        1) A TypeSpec file.
                                        2) A list of AZC0030 errors, each referring to a model name that ends with one of the forbidden suffixes.

                                        For each error, produce exactly one JSON object with these three properties:
                                        - OriginalName: the name as it appears in the TSP (including the forbidden suffix).
                                        - SuggestedName: a new PascalCase name without the forbidden suffix, following Azure SDK guidelines.
                                        - Reason: a brief explanation why the new name better represents the model’s purpose.

                                        Return the complete result as a single JSON array. No extra text.

                                        Example output:

                                        [
                                        {
                                            'OriginalName': 'VirtualMachineOptions',
                                            'SuggestedName': 'VirtualMachineProperties',
                                            'Reason': 'This model represents VM properties/state, not input options.'
                                        },
                                        {
                                            'OriginalName': 'AssetChainResponse',
                                            'SuggestedName': 'AssetChainResult',
                                            'Reason': 'Response suffix is reserved for raw HTTP responses; Result better describes the payload.'
                                        }
                                        ]";

        public AzcAgentService(string projectEndpoint, string model = "gpt-35-turbo")
        {
            if (string.IsNullOrEmpty(model))
            {
                throw new ArgumentException("Model name must be provided. Check your Azure AI Studio project under 'Agents' section for available models.", nameof(model));
            }

            this.projectEndpoint = projectEndpoint;
            this.model = model;
            client = new PersistentAgentsClient(projectEndpoint, new DefaultAzureCredential());
        }

        private async Task TestConnectionAsync(CancellationToken ct)
        {
            var thread = await client.Threads.CreateThreadAsync(cancellationToken: ct).ConfigureAwait(false);
            if (thread?.Value?.Id != null)
            {
                System.Console.WriteLine("Successfully connected to AI Foundry!");
            }
        }

        private async Task DeleteAgents(CancellationToken ct = default)
        {
            System.Console.WriteLine($"Deleting agents in project '{projectEndpoint}'");
            AsyncPageable<PersistentAgent> agents = client.Administration.GetAgentsAsync(cancellationToken: ct);
            await foreach (var agent in agents)
            {
                System.Console.WriteLine($"Deleting agent {agent.Id} ({agent.Name})");
                await client.Administration.DeleteAgentAsync(agent.Id, ct);
            }

            AsyncPageable<PersistentAgentThread> threads = client.Threads.GetThreadsAsync(cancellationToken: ct);
            await foreach (var thread in threads)
            {
                System.Console.WriteLine($"Deleting thread {thread.Id}");
                await client.Threads.DeleteThreadAsync(thread.Id, ct);
            }

            AsyncPageable<PersistentAgentsVectorStore> vectorStores = client.VectorStores.GetVectorStoresAsync(cancellationToken: ct);
            await foreach (var vectorStore in vectorStores)
            {
                System.Console.WriteLine($"Deleting vector store {vectorStore.Id} ({vectorStore.Name})");
                await client.VectorStores.DeleteVectorStoreAsync(vectorStore.Id, ct);
            }

            var files = await client.Files.GetFilesAsync(cancellationToken: ct);
            foreach (var file in files.Value)
            {
                System.Console.WriteLine($"Deleting file {file.Id} ({file.Filename})");
                await client.Files.DeleteFileAsync(file.Id, ct);
            }
        }

        private async Task<List<string>> TestFileUploadAsync(string logPath, string mainTspPath, CancellationToken ct)
        {
            Console.WriteLine("Starting file upload test...");

            // Create a temporary .txt copy of the .tsp file
            var mainTspTxtPath = CreateTempTextCopy(mainTspPath);
            var files = new List<string> { logPath, mainTspTxtPath };
            List<string> uploadedFileIds = new();

            foreach (var file in files)
            {
                try
                {
                    Console.WriteLine($"Uploading file: {Path.GetFileName(file)}");
                    var uploadResult = await client.Files.UploadFileAsync(file, PersistentAgentFilePurpose.Agents, ct);
                    if (uploadResult?.Value?.Id != null)
                    {
                        uploadedFileIds.Add(uploadResult.Value.Id);
                        Console.WriteLine($"Successfully uploaded {Path.GetFileName(file)}. File ID: {uploadResult.Value.Id}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error uploading {Path.GetFileName(file)}: {ex.Message}");
                    throw;
                }
            }

            // Clean up the temporary file
            try
            {
                if (File.Exists(mainTspTxtPath))
                {
                    File.Delete(mainTspTxtPath);
                    Console.WriteLine("Cleaned up temporary TSP text file");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not delete temporary file: {ex.Message}");
            }

            return uploadedFileIds;
        }

        private async Task<string> CreateVectorStoreAsync(List<string> uploadedFileIds, CancellationToken ct)
        {
            try
            {
                Console.WriteLine("Creating vector store from uploaded files...");
                var vectorStore = await client.VectorStores.CreateVectorStoreAsync(
                    uploadedFileIds,
                    name: "azc-session",
                    cancellationToken: ct);

                if (vectorStore?.Value?.Id != null)
                {
                    Console.WriteLine($"Successfully created vector store. ID: {vectorStore.Value.Id}");
                    return vectorStore.Value.Id;
                }
                else
                {
                    throw new Exception("Failed to create vector store - no ID returned");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating vector store: {ex.Message}");
                throw;
            }
        }

        private async Task<string> GetAgentSuggestionsAsync(string vectorStoreId, string mainTspPath, CancellationToken ct)
        {
            try
            {
                Console.WriteLine("Creating a thread for agent analysis...");

                // Set up the tools for the agent
                var fileSearchTool = new FileSearchToolResource();
                fileSearchTool.VectorStoreIds.Add(vectorStoreId);

                // Create or update the agent with file search capability
                Console.WriteLine($"Creating agent with file search capability using model: {model}...");
                agent = await client.Administration.CreateAgentAsync(
                    model: model,
                    name: "AZC0030 fixer Agent",
                    instructions: AzcQueryPrompt,
                    tools: new[] { new FileSearchToolDefinition() },
                    toolResources: new ToolResources { FileSearch = fileSearchTool },
                    cancellationToken: ct
                );

                if (agent == null || string.IsNullOrEmpty(agent.Id))
                {
                    throw new Exception("Failed to create agent - no agent ID returned");
                }

                // Create a thread and send initial message
                Console.WriteLine("Creating thread...");
                PersistentAgentThread thread = await client.Threads.CreateThreadAsync(cancellationToken: ct);

                if (thread == null || string.IsNullOrEmpty(thread.Id))
                {
                    throw new Exception("Failed to create thread - no thread ID returned");
                }

                //Create a message in the thread to start the conversation
                Console.WriteLine("Creating initial message...");
                var message = await client.Messages.CreateMessageAsync(
                    thread.Id,
                    MessageRole.User,
                    "Please analyze the files for AZC0030 errors and suggest appropriate fixes following Azure SDK naming conventions.",
                    cancellationToken: ct);

                if (message == null)
                {
                    throw new Exception("Failed to create message in thread");
                }

                // Start the analysis
                Console.WriteLine("Starting agent analysis...");
                ThreadRun run = await client.Runs.CreateRunAsync(thread.Id, agent.Id);

                if (run == null || string.IsNullOrEmpty(run.Id))
                {
                    throw new Exception("Failed to create run - no run ID returned");
                }

                // Wait for the analysis to complete
                int attempts = 0;
                const int maxAttempts = 60; // 5 minutes max wait time

                do
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    run = await client.Runs.GetRunAsync(thread.Id, run.Id);
                    Console.WriteLine($"Run status: {run.Status} (Attempt {attempts + 1}/{maxAttempts})");
                    attempts++;

                    if (attempts >= maxAttempts)
                    {
                        throw new TimeoutException("Analysis timed out after 5 minutes");
                    }
                }
                while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress);

                Console.WriteLine($"Analysis completed with status: {run.Status}");

                if (run.Status == RunStatus.Failed)
                {
                    Console.WriteLine($"Run failed with error: {run.LastError?.Message ?? "Unknown error"}");
                    throw new Exception($"Agent analysis failed: {run.LastError?.Message ?? "Unknown error"}");
                }

                // Get the messages
                Console.WriteLine("Retrieving analysis results...");
                AsyncPageable<PersistentThreadMessage> messages = client.Messages.GetMessagesAsync(
                    threadId: thread.Id,
                    order: ListSortOrder.Ascending);

                var response = new List<string>();

                Console.WriteLine("\nRaw AI Response:");
                await foreach (var textmessage in messages)
                {
                    foreach (var content in textmessage.ContentItems)
                    {
                        if (content is MessageTextContent textContent)
                        {
                            Console.WriteLine("---Response Start---");
                            Console.WriteLine(textContent.Text);
                            Console.WriteLine("---Response End---");
                            response.Add(textContent.Text);
                        }
                    }
                }

                return string.Join("\n", response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during agent analysis: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private string CreateTempTextCopy(string tspPath)
        {
            var tempPath = Path.ChangeExtension(tspPath, ".txt");
            File.Copy(tspPath, tempPath, true); // Overwrite if exists
            Console.WriteLine($"Created temporary copy of TSP file at: {tempPath}");
            return tempPath;
        }

        public async Task fixAzcErrorsAsync(string mainTsp, string logPath)
        {
            await TestConnectionAsync(CancellationToken.None);
            await DeleteAgents(CancellationToken.None);
            var uploadedFiles = await TestFileUploadAsync(logPath, mainTsp, CancellationToken.None);
            var vectorStoreId = await CreateVectorStoreAsync(uploadedFiles, CancellationToken.None);
            string suggestion = await GetAgentSuggestionsAsync(vectorStoreId, mainTsp, CancellationToken.None);
            if (string.IsNullOrEmpty(suggestion))
            {
                Console.WriteLine("No suggestions found for AZC0030 errors.");
                return;
            }
            Console.WriteLine($"Suggestions found: {suggestion}");


        }

    }
}
