using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Azure;
using System;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AzcAnalyzerFixer.Services
{
    public class AzcAgentService
    {
        private readonly PersistentAgentsClient client;
        private PersistentAgent? agent;
        private readonly string model;
        private readonly string projectEndpoint;

        private const string AzcQueryPrompt = @"You are an expert on Azure SDK naming conventions and the AZC0030 analyzer rule (model names ending in disallowed suffixes Request, Response, Options).
                                                I will give you:
                                                1) A TypeSpec file.
                                                2) A list of AZC0030 errors, each referring to a model name that ends with one of the forbidden suffixes.
         
                                                You have access to one tool:
                                                • file_search(path: string) → returns the *full contents* of that file.

                                                When you need the files, emit exactly:

                                                CALL_TOOL: file_search(\'main.tsp.txt\')  
                                                CALL_TOOL: file_search(\'azc-errors.txt\')

                                                Once you have both files, output *only* this JSON schema, with no extra characters, no markdown fences, no commentary:

                                                {
                                                ""suggestions"": [
                                                    { ""OriginalName"": string, ""SuggestedName"": string, ""Reason"": string }
                                                ],
                                                ""updatedTsp"": string
                                                }

                                                Make sure the entire response is a single JSON object matching that schema and nothing else.
                                                ";

        public AzcAgentService(string projectEndpoint, string model = "gpt-35-turbo")
        {
            if (string.IsNullOrEmpty(model))
            {
                throw new ArgumentException("Model name must be provided. Check your Azure AI Studio project under 'Agents' section for available models.", nameof(model));
            }

            this.projectEndpoint = projectEndpoint;
            this.model = model;
            client = new PersistentAgentsClient(projectEndpoint, new DefaultAzureCredential());
        }        public async Task fixAzcErrorsAsync(string mainTsp, string logPath)
        {
            await TestConnectionAsync(CancellationToken.None).ConfigureAwait(false);
            await DeleteAgents(CancellationToken.None).ConfigureAwait(false);
            var uploadedFiles = await TestFileUploadAsync(logPath, mainTsp, CancellationToken.None).ConfigureAwait(false);
            var vectorStoreId = await CreateVectorStoreAsync(uploadedFiles, CancellationToken.None).ConfigureAwait(false);
            string suggestion = await GetAgentSuggestionsAsync(vectorStoreId, mainTsp, CancellationToken.None).ConfigureAwait(false);
            await CreateUpdatedFileAsync(suggestion, mainTsp).ConfigureAwait(false);
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
            // System.Console.WriteLine($"Deleting agents in project '{projectEndpoint}'"); 
            AsyncPageable<PersistentAgent> agents = client.Administration.GetAgentsAsync(cancellationToken: ct);
            await foreach (var agent in agents.ConfigureAwait(false))
            {
                // System.Console.WriteLine($"Deleting agent {agent.Id} ({agent.Name})");
                await client.Administration.DeleteAgentAsync(agent.Id, ct).ConfigureAwait(false);
            }

            AsyncPageable<PersistentAgentThread> threads = client.Threads.GetThreadsAsync(cancellationToken: ct);
            await foreach (var thread in threads.ConfigureAwait(false))
            {
                // System.Console.WriteLine($"Deleting thread {thread.Id}");
                await client.Threads.DeleteThreadAsync(thread.Id, ct).ConfigureAwait(false);
            }

            AsyncPageable<PersistentAgentsVectorStore> vectorStores = client.VectorStores.GetVectorStoresAsync(cancellationToken: ct);
            await foreach (var vectorStore in vectorStores.ConfigureAwait(false))
            {
                // System.Console.WriteLine($"Deleting vector store {vectorStore.Id} ({vectorStore.Name})");
                await client.VectorStores.DeleteVectorStoreAsync(vectorStore.Id, ct).ConfigureAwait(false);
            }

            var files = await client.Files.GetFilesAsync(cancellationToken: ct).ConfigureAwait(false);
            foreach (var file in files.Value)
            {
                // System.Console.WriteLine($"Deleting file {file.Id} ({file.Filename})");
                await client.Files.DeleteFileAsync(file.Id, ct).ConfigureAwait(false);
            }
        }

        private async Task<List<string>> TestFileUploadAsync(string logPath, string mainTspPath, CancellationToken ct)
        {
            // Console.WriteLine("Starting file upload test...");

            // Create a temporary .txt copy of the .tsp file
            var mainTspTxtPath = CreateTempTextCopy(mainTspPath);
            var files = new List<string> { logPath, mainTspTxtPath };
            List<string> uploadedFileIds = new();

            foreach (var file in files)
            {
                try
                {
                    // Console.WriteLine($"Uploading file: {Path.GetFileName(file)}");
                    var uploadResult = await client.Files.UploadFileAsync(file, PersistentAgentFilePurpose.Agents, ct).ConfigureAwait(false);
                    if (uploadResult?.Value?.Id != null)
                    {
                        uploadedFileIds.Add(uploadResult.Value.Id);
                        // Console.WriteLine($"Successfully uploaded {Path.GetFileName(file)}. File ID: {uploadResult.Value.Id}");
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
                    // Console.WriteLine("Cleaned up temporary TSP text file");
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
                // Console.WriteLine("Creating vector store from uploaded files...");
                var vectorStore = await client.VectorStores.CreateVectorStoreAsync(
                    uploadedFileIds,
                    name: "azc-session",
                    cancellationToken: ct).ConfigureAwait(false);

                if (vectorStore?.Value?.Id != null)
                {
                    // Console.WriteLine($"Successfully created vector store. ID: {vectorStore.Value.Id}");
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
                // Console.WriteLine("Creating a thread for agent analysis...");

                // Set up the tools for the agent
                var fileSearchTool = new FileSearchToolResource();
                fileSearchTool.VectorStoreIds.Add(vectorStoreId); 
                // Create or update the agent with file search capability
                // Console.WriteLine($"Creating agent with file search capability using model: {model}...");
                agent = await client.Administration.CreateAgentAsync(
                    model: model,
                    name: "AZC0030 fixer Agent",
                    instructions: AzcQueryPrompt,
                    tools: new[] { new FileSearchToolDefinition() },
                    toolResources: new ToolResources { FileSearch = fileSearchTool },
                    cancellationToken: ct
                ).ConfigureAwait(false);

                if (agent == null || string.IsNullOrEmpty(agent.Id))
                {
                    throw new Exception("Failed to create agent - no agent ID returned");
                }
                // Create a thread and send initial message
                // Console.WriteLine("Creating thread...");
                PersistentAgentThread thread = await client.Threads.CreateThreadAsync(cancellationToken: ct).ConfigureAwait(false);

                if (thread == null || string.IsNullOrEmpty(thread.Id))
                {
                    throw new Exception("Failed to create thread - no thread ID returned");
                }
                //Create a message in the thread to start the conversation
                // Console.WriteLine("Creating initial message...");
                var message = await client.Messages.CreateMessageAsync(
                    thread.Id,
                    MessageRole.User,
                    "Please analyze the files for AZC0030 errors and suggest appropriate fixes following Azure SDK naming conventions.",
                    cancellationToken: ct).ConfigureAwait(false);

                if (message == null)
                {
                    throw new Exception("Failed to create message in thread");
                }

                // Start the analysis
                Console.WriteLine("Starting agent analysis...");
                ThreadRun run = await client.Runs.CreateRunAsync(thread.Id, agent.Id).ConfigureAwait(false);

                if (run == null || string.IsNullOrEmpty(run.Id))
                {
                    throw new Exception("Failed to create run - no run ID returned");
                }

                // Wait for the analysis to complete
                int attempts = 0;
                const int maxAttempts = 60; // 5 minutes max wait time 

                do
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                    run = await client.Runs.GetRunAsync(thread.Id, run.Id).ConfigureAwait(false);
                    // Console.WriteLine($"Run status: {run.Status} (Attempt {attempts + 1}/{maxAttempts})");
                    attempts++;

                    if (attempts >= maxAttempts)
                    {
                        throw new TimeoutException("Analysis timed out after 5 minutes");
                    }
                }
                while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress);

                // Console.WriteLine($"Analysis completed with status: {run.Status}");

                if (run.Status == RunStatus.Failed)
                {
                    Console.WriteLine($"Run failed with error: {run.LastError?.Message ?? "Unknown error"}");
                    throw new Exception($"Agent analysis failed: {run.LastError?.Message ?? "Unknown error"}");
                }

                // Get the messages
                // Console.WriteLine("Retrieving analysis results...");
                AsyncPageable<PersistentThreadMessage> messages = client.Messages.GetMessagesAsync(
                    threadId: thread.Id,
                    order: ListSortOrder.Ascending);

                var response = new List<string>();
                // Console.WriteLine("\nRaw AI Response:");
                await foreach (var textmessage in messages.ConfigureAwait(false))
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
            // Console.WriteLine($"Created temporary copy of TSP file at: {tempPath}");
            return tempPath;
        }

        private Task CreateUpdatedFileAsync(string suggestion, string mainTsp)
        {
            var start = suggestion.IndexOf('{');
            var end = suggestion.LastIndexOf('}');
            if (start < 0 || end < 0 || end <= start)
                throw new Exception("No JSON object found in agent response:\n" + suggestion);

            var jsonPayload = suggestion.Substring(start, end - start + 1);
            var result = JsonSerializer.Deserialize<AzcFixResult>(jsonPayload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result?.UpdatedTsp == null)
            {
                Console.WriteLine("No suggestions found for AZC0030 errors.");
                return Task.CompletedTask;
            }

            // 1) Backup the original
            var backupPath = mainTsp + ".bak";
            File.Copy(mainTsp, backupPath, overwrite: true);
            Console.WriteLine($"Backup of original created at {backupPath}");

            // 2) Overwrite main.tsp
            File.WriteAllText(mainTsp, result.UpdatedTsp);
            Console.WriteLine($"main.tsp has been updated in place.");

            return Task.CompletedTask;
        }
        
        private class AzcFixResult
        {
            public List<AzcFixSuggestion>? Suggestions { get; set; }
            public string? UpdatedTsp { get; set; }
        }

        private class AzcFixSuggestion
        {
            public string? OriginalName   { get; set; }
            public string? SuggestedName  { get; set; }
            public string? Reason         { get; set; }
        }

    }
}
