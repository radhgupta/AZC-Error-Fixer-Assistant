using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using System.Text.Json;
using Azure;
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
        private const string AgentPrompt = @"You are an expert Azure SDK developer and TypeSpec author, responsible for ensuring complete compliance with Azure SDK Design Guidelines and AZC analyzer standards.

                                                ### OBJECTIVE:
                                                Given a TypeSpec source files (main.tsp and client.tsp) and an AZC error log, your task is to automatically resolve **all** AZC analyzer violations by updating client.tsp file with proper customization and return a fully corrected, syntactically valid client.tsp file.

                                                ### REQUIREMENTS:
                                                - Fix **ALL** AZC violations (e.g., AZC0008, AZC0012, AZC0030, AZC0015, AZC0020, etc.)
                                                - If there is an existing client.tsp file, ensure it is updated correctly
                                                - Do not modify main.tsp file
                                                - Ensure **TypeSpec 1.0+ syntax** is used throughout
                                                - Use https://azure.github.io/typespec-azure/docs/libraries/typespec-client-generator-core/reference/decorators/#@Azure.ClientGenerator.Core.clientName for  proper customization for csharp
                                                - Your output TypeSpec file must pass compilation in TypeSpec 1.0+ without syntax errors
                                                - Ensure all client.tsp references match main.tsp types 

                                                ### example fixes:
                                                - AZC0030: @@clientName(ExisitingModelName, ""NewModelName"", ""csharp"");
                                                Here the first parameter is the existing model from main.tsp, the second parameter is the new name for the client library, and the third parameter is always ""csharp"".
                                             
                                                ### OUTPUT FORMAT:
                                                Return ONLY a JSON object structured exactly as follows:
                                                {
                                                ""analysis"": {
                                                    ""total_azc_errors"": <number>,
                                                    ""error_types_found"": [""AZC0008"", ""AZC0012"", ...],
                                                    ""models_requiring_fixes"": [""ModelA"", ""ModelB"", ...]
                                                },
                                                ""fixes"": {
                                                    ""model_renames"": [
                                                    { ""original"": ""Disk"", ""fixed"": ""ComputeDisk"", ""reason"": ""AZC0012: Added service prefix"" }
                                                    ],
                                                    ""reference_updates"": [
                                                    { ""location"": ""line 42"", ""original"": ""DiskOptions"", ""fixed"": ""ComputeDiskOptions"", ""reason"": ""Updated reference to renamed model"" }
                                                    ],
                                                },
                                                ""UpdatedClientTsp"": ""<complete client.tsp content here>""
                                                }";

        public AzcAgentService(string projectEndpoint, string model, ILoggerService loggerService, FileHelper fileHelper)
        {
            if (string.IsNullOrWhiteSpace(projectEndpoint))
            {
                throw new ArgumentException("Project endpoint must be provided.", nameof(projectEndpoint));
            }

            if (loggerService == null)
            {
                throw new ArgumentNullException(nameof(loggerService));
            }

            if (fileHelper == null)
            {
                throw new ArgumentNullException(nameof(fileHelper));
            }

            client = new PersistentAgentsClient(projectEndpoint, new DefaultAzureCredential());
            this.fileHelper = fileHelper;
            this.logger = loggerService;
            this.model = model ?? throw new ArgumentNullException(nameof(model), "Model must be provided.");

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

        public async Task FixAzcErrorsAsync(string tspPath, string logPath)
        {
            try
            {
                logger.LogInfo("Starting file upload...");
                var uploadedFiles = await UploadFilesAsync(tspPath, logPath);
                logger.LogInfo($"Files uploaded successfully: {string.Join(", ", uploadedFiles)}");

                logger.LogInfo("Creating vector store...");
                var vectorStoreId = await CreateVectorStoreAsync(uploadedFiles);
                logger.LogInfo($"Vector store created with ID: {vectorStoreId}");

                logger.LogInfo("Starting analysis...");
                var rawResponse = await AnalyzeAndFixAsync(vectorStoreId, tspPath);
                logger.LogInfo($"Raw response received: {rawResponse?.Substring(0, Math.Min(100, rawResponse?.Length ?? 0))}...");

                logger.LogInfo("Extracting JSON...");
                var json = JsonHelper.ExtractJsonPayload(rawResponse);
                logger.LogInfo($"Extracted JSON: {json?.Substring(0, Math.Min(100, json?.Length ?? 0))}...");

                var result = JsonSerializer.Deserialize<AzcFixResult>(json);
                if (result == null)
                {
                    throw new InvalidOperationException("Failed to deserialize the response JSON");
                }

                if (string.IsNullOrWhiteSpace(result.UpdatedClientTsp))
                {
                    throw new InvalidOperationException("No updated TypeSpec content was provided in the response");
                }

                fileHelper.WriteClientTsp(tspPath, result.UpdatedClientTsp);
                logger.LogInfo("✅ Successfully updated client.tsp");
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to fix AZC errors. Exception: {ex.Message}", ex);
                logger.LogError($"Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    logger.LogError($"Inner exception: {ex.InnerException.Message}");
                    logger.LogError($"Inner exception stack trace: {ex.InnerException.StackTrace}");
                }
                throw;
            }
        }

        private async Task<List<string>> UploadFilesAsync(string tspFolderPath, string logFilePath)
        {
            var uploadedFileIds = new List<string>();
            var tempTspFiles = new List<string>();

            // 1. Handle .tsp → .txt conversion
            if (Directory.Exists(tspFolderPath))
            {
                var tspFiles = Directory.GetFiles(tspFolderPath, "*.tsp", SearchOption.TopDirectoryOnly);

                foreach (var tspFile in tspFiles)
                {
                    var txtTempFile = Path.ChangeExtension(Path.GetTempFileName(), ".txt");
                    File.Copy(tspFile, txtTempFile, true);
                    tempTspFiles.Add(txtTempFile);
                }
            }
            else
            {
                throw new DirectoryNotFoundException($"TSP folder not found: {tspFolderPath}");
            }

            // 2. Add log file to upload list
            if (!File.Exists(logFilePath))
            {
                throw new FileNotFoundException($"Log file not found: {logFilePath}");
            }

            var filesToUpload = tempTspFiles.Append(logFilePath);

            // 3. Upload all files
            foreach (var file in filesToUpload)
            {
                try
                {
                    var result = await client.Files.UploadFileAsync(file, PersistentAgentFilePurpose.Agents);
                    if (result?.Value?.Id != null)
                    {
                        uploadedFileIds.Add(result.Value.Id);
                        logger.LogInfo($"📤 Uploaded file: {Path.GetFileName(file)}");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError($"Failed to upload file: {file}", ex);
                    throw;
                }
            }

            return uploadedFileIds;
        }

        private async Task<string> CreateVectorStoreAsync(List<string> fileIds)
        {
            var store = await client.VectorStores.CreateVectorStoreAsync(fileIds, name: $"azc-{DateTime.Now:yyyyMMddHHmmss}");
            await Task.Delay(10000); // 10s indexing wait
            return store.Value.Id;
        }

        private async Task<string> AnalyzeAndFixAsync(string vectorStoreId, string mainTspPath)
        {
            if (string.IsNullOrEmpty(vectorStoreId))
            {
                throw new ArgumentException("Vector store ID cannot be null or empty", nameof(vectorStoreId));
            }

            logger.LogInfo("Creating agent...");
            PersistentAgent agent = await client.Administration.CreateAgentAsync(
                model: model,
                name: "AZC Fixer",
                instructions: AgentPrompt);
            if (agent == null || string.IsNullOrEmpty(agent.Id))
            {
                throw new InvalidOperationException("Failed to create agent");
            }

            logger.LogInfo("Creating thread...");
            PersistentAgentThread thread = await client.Threads.CreateThreadAsync();
            if (thread == null || string.IsNullOrEmpty(thread.Id))
            {
                throw new InvalidOperationException("Failed to create thread");
            }

            if (fileHelper.MainTspContent == null)
            {
                throw new InvalidOperationException("Main TypeSpec content is null");
            }

            logger.LogInfo("Creating message...");
            var msg = await client.Messages.CreateMessageAsync(
                thread.Id,
                MessageRole.User,
                $@"
                        Please analyze and correct all AZC analyzer violations using the following inputs:

                        ### TypeSpec File
                        ===FILE_CONTENT_START===
                        {fileHelper.MainTspContent}
                        ===FILE_CONTENT_END===
                        ### Client.TSP File
                        ===CLIENT_TSP_START===
                        {fileHelper.ClientTspContent}
                        ===CLIENT_TSP_END===

                        ### AZC Error Log
                        ===ERROR_LOG_START===
                        {fileHelper.ErrorLogContent}
                        ===ERROR_LOG_END===

                        ### TASKS:
                        1. Parse the TypeSpec file and AZC error log
                        2. Fix **all** AZC violations found (e.g., AZC0008, AZC0012, AZC0030, AZC0015, AZC0020)
                        3. Ensure consistent model naming and valid references
                        4. Use TypeSpec 1.0+ syntax only. Your output TypeSpec file must pass compilation in TypeSpec 1.0+ without syntax errors

                        ### RETURN:
                        - Return **only** a well-formed JSON object (no extra text)
                        - Follow the schema provided in your instructions
                        - Include:
                        - Total AZC errors and types
                        - All renames, updates, and additions performed
                        - Full updated TypeSpec in the UpdatedClientTsp field
                        - Ensure that the returned TypeSpec is fully compilable and has zero AZC violations",
                cancellationToken: default);

            logger.LogInfo("Creating run...");
            ThreadRun run = await client.Runs.CreateRunAsync(thread.Id, agent.Id);
            if (run == null || string.IsNullOrEmpty(run.Id))
            {
                throw new InvalidOperationException("Failed to create run");
            }

            RunStatus status;
            do
            {
                await Task.Delay(5000);
                run = await client.Runs.GetRunAsync(thread.Id, run.Id);
                if (run == null)
                {
                    throw new InvalidOperationException("Run became null during status check");
                }
                status = run.Status;
                logger.LogInfo($"Current run status: {status}");
            } while (status == RunStatus.InProgress || status == RunStatus.Queued);

            logger.LogInfo("Retrieving messages...");
            AsyncPageable<PersistentThreadMessage> messages = client.Messages.GetMessagesAsync(thread.Id, order: ListSortOrder.Ascending);
            var response = new List<string>();

            await foreach (var msgItem in messages)
            {
                if (msgItem?.ContentItems == null) continue;
                
                foreach (var content in msgItem.ContentItems)
                {
                    if (content is MessageTextContent textContent)
                    {
                        response.Add(textContent.Text);
                    }
                }
            }

            var result = string.Join("\n", response);
            if (string.IsNullOrWhiteSpace(result))
            {
                throw new InvalidOperationException("No response content was retrieved from the messages");
            }

            return result;
        }
    }
}
