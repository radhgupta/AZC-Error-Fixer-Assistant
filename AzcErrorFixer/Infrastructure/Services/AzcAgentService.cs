using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using System.Text.Json;
using Azure;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using AzcAnalyzerFixer.Core.Interfaces;
using AzcAnalyzerFixer.Core.Models;
using AzcAnalyzerFixer.Infrastructure.Helpers;
using AzcAnalyzerFixer.Logging;

namespace AzcAnalyzerFixer.Infrastructure.Services
{
    public class AzcAgentService : IAzcAgentService
    {
        private readonly PersistentAgentsClient client;
        private readonly ILoggerService logger;
        private readonly FileHelper fileHelper;
        private readonly string model;
        private PersistentAgent? agent;

        private const string AgentPrompt = Configuration.AppSettings.initialPrompt;

        public AzcAgentService(string projectEndpoint, string model, ILoggerService loggerService, FileHelper fileHelper)
        {
            client = new PersistentAgentsClient(projectEndpoint, new DefaultAzureCredential());
            this.fileHelper = fileHelper;
            this.logger = loggerService;
            this.model = model ?? throw new ArgumentNullException(nameof(model), "Model must be provided.");
            agent = null;
        }

        public async Task TestConnectionAsync(CancellationToken ct)
        {
            var thread = await client.Threads.CreateThreadAsync(cancellationToken: ct).ConfigureAwait(false);

            if (thread?.Value?.Id != null)
            {
                Console.WriteLine("✅ Successfully connected to Azure AI Foundry and created a thread.");
            }
            else
            {
                throw new Exception("Failed to create a thread. Connection unsuccessful.");
            }
        }

        public async Task DeleteAgentsAsync(CancellationToken ct)
        {
            Console.WriteLine("🧹 Cleaning up agents, threads, vector stores, and files...");

            // Delete all agents
            await foreach (var agent in client.Administration.GetAgentsAsync(cancellationToken: ct))
            {
                Console.WriteLine($"🗑️ Deleting agent: {agent.Name} ({agent.Id})");
                await client.Administration.DeleteAgentAsync(agent.Id, ct);
            }

            // Delete all threads
            await foreach (var thread in client.Threads.GetThreadsAsync(cancellationToken: ct))
            {
                Console.WriteLine($"🗑️ Deleting thread: {thread.Id}");
                await client.Threads.DeleteThreadAsync(thread.Id, ct);
            }

            // Delete all vector stores
            await foreach (var store in client.VectorStores.GetVectorStoresAsync(cancellationToken: ct))
            {
                Console.WriteLine($"🗑️ Deleting vector store: {store.Name} ({store.Id})");
                await client.VectorStores.DeleteVectorStoreAsync(store.Id, ct);
            }

            // Delete all uploaded files
            var files = await client.Files.GetFilesAsync(cancellationToken: ct);
            foreach (var file in files.Value)
            {
                Console.WriteLine($"🗑️ Deleting file: {file.Filename} ({file.Id})");
                await client.Files.DeleteFileAsync(file.Id, ct);
            }

            Console.WriteLine("✅ Cleanup complete.\n");
        }

        public async Task CreateAgentAsync(CancellationToken ct)
        {
            if (agent != null)
            {
                logger.LogInfo($"Agent already exists: {agent.Name} ({agent.Id})");
                return;
            }

            logger.LogInfo("Creating AZC Fixer agent...");
            agent = await client.Administration.CreateAgentAsync(
                model: model,
                name: "AZC Fixer",
                instructions: AgentPrompt,
                tools: new[] { new FileSearchToolDefinition() },
                cancellationToken: ct);

            if (agent == null || string.IsNullOrEmpty(agent.Id))
            {
                throw new InvalidOperationException("Failed to create AZC Fixer agent");
            }

            logger.LogInfo($"✅ Agent created successfully: {agent.Name} ({agent.Id})");
        }

        private async Task UpdateAgentVectorStoreAsync(string vectorStoreId, CancellationToken ct)
        {
            if (agent == null)
                throw new InvalidOperationException("Agent must be created before updating its vector store.");

            // Replace the FileSearchToolResource with a new one containing only the latest store
            var updated = await client.Administration.UpdateAgentAsync(
                agent.Id,
                toolResources: new ToolResources
                {
                    FileSearch = new FileSearchToolResource
                    {
                        VectorStoreIds = { vectorStoreId }
                    }
                },
                cancellationToken: ct
            );

            logger.LogInfo($"🔄 Agent vector store updated to: {vectorStoreId}");
        }

        public async Task FixAzcErrorsAsync(string tspFolderPath, string logFilePath, string suggestions)
        {
            var uploadedFiles = await UploadTspAndLogAsync(tspFolderPath, logFilePath);
            if (uploadedFiles.Count == 0)
                throw new InvalidOperationException("No files were uploaded. Cannot proceed with AZC error fixing.");
            logger.LogInfo($"Uploaded {uploadedFiles} files to the agent vector store.");
            await WaitForIndexingAsync(uploadedFiles);
            var vectorStoreId = await CreateVectorStoreAsync(uploadedFiles);
            await UpdateAgentVectorStoreAsync(vectorStoreId, CancellationToken.None);

            PersistentAgentThread thread = await client.Threads.CreateThreadAsync();
            string agentMessage = $@"Please apply the following AZC suggestions to the client.tsp file. Apply the changes one by one and return the updated file in a JSON object, Here are the suggestions: {suggestions}";
            await client.Messages.CreateMessageAsync(thread.Id, MessageRole.User, agentMessage);

            ThreadRun run = await client.Runs.CreateRunAsync(thread.Id, agent.Id);
            RunStatus status;

            do
            {
                await Task.Delay(5000);
                run = await client.Runs.GetRunAsync(thread.Id, run.Id);
                status = run.Status;
                logger.LogInfo($"Run status: {status}");
            }
            while (status == RunStatus.Queued || status == RunStatus.InProgress);

            var response = await ReadResponseAsync(thread.Id);
            var json = JsonHelper.ExtractJsonPayload(response);
            var result = JsonSerializer.Deserialize<AzcFixResult>(json);

            if (string.IsNullOrWhiteSpace(result?.UpdatedClientTsp))
                throw new Exception("No updated client.tsp provided by agent.");

            fileHelper.WriteClientTsp(tspFolderPath, result.UpdatedClientTsp);
            logger.LogInfo("✅ client.tsp updated.");
        }

        private async Task<List<string>> UploadTspAndLogAsync(string folderPath, string logPath)
        {
            var uploadedIds = new List<string>();

            var tspFiles = Directory.GetFiles(folderPath, "*.tsp");
            foreach (var file in tspFiles)
            {
                var txtTempPath = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(file)}.txt");
                var content = await File.ReadAllTextAsync(file);
                await File.WriteAllTextAsync(txtTempPath, content, Encoding.UTF8);
                var finalContent = await File.ReadAllTextAsync(txtTempPath);

                var uploaded = await client.Files.UploadFileAsync(txtTempPath, PersistentAgentFilePurpose.Agents);
                logger.LogInfo($"Uploaded {file}");
                if (uploaded?.Value?.Id != null)
                    uploadedIds.Add(uploaded.Value.Id);
            }

            if (File.Exists(logPath))
            {

                var logTempPath = Path.Combine(Path.GetTempPath(), $"azc-errors.txt");
                var logContent = await File.ReadAllTextAsync(logPath);
                await File.WriteAllTextAsync(logTempPath, logContent, Encoding.UTF8);
                var logUploaded = await client.Files.UploadFileAsync(logTempPath, PersistentAgentFilePurpose.Agents);
                if (logUploaded?.Value?.Id != null)
                    uploadedIds.Add(logUploaded.Value.Id);
                logger.LogInfo($"Uploaded log file {logPath}");
            }

            return uploadedIds;
        }

        private async Task WaitForIndexingAsync(List<string> fileIds)
        {
            logger.LogInfo("⏳ Waiting for file indexing to complete...");

            var maxWaitTime = TimeSpan.FromSeconds(60);
            var pollingInterval = TimeSpan.FromSeconds(5);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (stopwatch.Elapsed < maxWaitTime)
            {
                bool allIndexed = true;
                foreach (var fileId in fileIds)
                {
                    PersistentAgentFileInfo file = await client.Files.GetFileAsync(fileId);
                    logger.LogInfo($"📄 File {file.Filename} index status: {file.Status}");
                }

                if (allIndexed)
                {
                    logger.LogInfo("✅ All files indexed successfully.");
                    return;
                }

                await Task.Delay(pollingInterval);
            }

            throw new TimeoutException("❌ Timeout while waiting for file indexing to complete.");
        }

        private async Task<string> CreateVectorStoreAsync(List<string> fileIds)
        {
            var store = await client.VectorStores.CreateVectorStoreAsync(fileIds, name: $"azc-{DateTime.Now:yyyyMMddHHmmss}");
            logger.LogInfo($"Created vector store: {store.Value.Name} ({store.Value.Id})");
            await Task.Delay(10000);
            return store.Value.Id;
        }

        private async Task<string> ReadResponseAsync(string threadId)
        {
            var messages = client.Messages.GetMessagesAsync(threadId, order: ListSortOrder.Ascending);
            var allText = new List<string>();

            await foreach (var message in messages)
            {
                foreach (var content in message.ContentItems.OfType<MessageTextContent>())
                {
                    logger.LogInfo($"Message content: {content.Text}");
                    allText.Add(content.Text);
                }
            }

            return string.Join("\n", allText);
        }
    }
}
