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

        private const string AzcQueryPrompt = @"You are an expert Azure SDK developer specializing in TypeSpec and Azure SDK Design Guidelines compliance.

MISSION: Fix ALL AZC analyzer violations in the provided TypeSpec file to ensure full compliance with Azure SDK standards.

CRITICAL REQUIREMENTS:
1. When renaming models, update ALL references to those models throughout the file
2. Ensure the generated TypeSpec file is syntactically valid
3. Maintain consistent naming across all model references
4. Verify that all referenced models are properly defined

CONTEXT: You will analyze:
1) A TypeSpec file (main.tsp.txt) - the source TypeSpec code
2) AZC error log (azc-errors.txt) - containing ALL AZC violations

COMMON AZC VIOLATIONS AND FIXES:
• AZC0008: Missing ServiceVersion enum → Add ServiceVersion enum to client options
• AZC0012: Generic single-word names → Use specific, prefixed names (e.g., 'Disk' → 'ComputeDisk')
• AZC0030: Forbidden suffixes → Remove Request/Response/Options suffixes
• AZC0015: Model property names → Use proper casing and naming conventions
• AZC0020: Invalid return types → Use proper Azure SDK return patterns

VALIDATION CHECKLIST:
✓ All model names follow Azure SDK naming conventions
✓ All model references are updated consistently throughout the file
✓ No undefined model references exist
✓ TypeSpec syntax is valid (proper decorators, proper service declaration)
✓ All AZC violations are resolved

OUTPUT REQUIREMENTS:
Provide ONLY a JSON response with complete fixes for ALL identified AZC violations:

{
  ""analysis"": {
    ""total_azc_errors"": number,
    ""error_types_found"": [""AZC0008"", ""AZC0012"", etc.],
    ""models_requiring_fixes"": [""list of model names""]
  },
  ""fixes"": {
    ""model_renames"": [
      { ""original"": ""Disk"", ""fixed"": ""ComputeDisk"", ""reason"": ""AZC0012: Added service prefix"" }
    ],
    ""reference_updates"": [
      { ""location"": ""line 13"", ""original"": ""DiskOptions"", ""fixed"": ""ComputeDiskOptions"", ""reason"": ""Updated reference to match renamed model"" }
    ],
    ""structural_additions"": [
      { ""type"": ""ServiceVersion"", ""location"": ""client options"", ""reason"": ""AZC0008: Required enum"" }
    ]
  },
  ""UpdatedTsp"": ""complete updated TypeSpec content with all AZC fixes applied and all references updated consistently""
}

CRITICAL: 
- Ensure the returned TypeSpec uses the TypeSpec 1.0+ syntax with object literals format: @service(#{title: ""Sample API""})  and enum format: enum SampleEnum { value1 : 1, value2 : 2 }
- Ensure the returned TypeSpec is syntactically valid so that when we compile it we don't get any errors, with all necessary decorators and a proper service declaration.
- Update ALL references when renaming models
- Verify that all referenced models exist in the updated file
- Test that the updated TypeSpec would compile successfully";

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

        public async Task fixAzcErrorsAsync(string mainTsp, string logPath)
        {
            var uploadedFiles = await TestFileUploadAsync(logPath, mainTsp, CancellationToken.None).ConfigureAwait(false);
            var vectorStoreId = await CreateVectorStoreAsync(uploadedFiles, CancellationToken.None).ConfigureAwait(false);
            string suggestion = await GetAgentSuggestionsAsync(vectorStoreId, mainTsp, CancellationToken.None).ConfigureAwait(false);
            await CreateUpdatedFileAsync(suggestion, mainTsp).ConfigureAwait(false);
        }

        public async Task TestConnectionAsync(CancellationToken ct)
        {
            var thread = await client.Threads.CreateThreadAsync(cancellationToken: ct).ConfigureAwait(false);
            if (thread?.Value?.Id != null)
            {
                System.Console.WriteLine("Successfully connected to AI Foundry!");
            }
        }

        public async Task DeleteAgents(CancellationToken ct = default)
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

            // // Clean up the temporary file
            // try
            // {
            //     if (File.Exists(mainTspTxtPath))
            //     {
            //         File.Delete(mainTspTxtPath);
            //         // Console.WriteLine("Cleaned up temporary TSP text file");
            //     }
            // }
            // catch (Exception ex)
            // {
            //     Console.WriteLine($"Warning: Could not delete temporary file: {ex.Message}");
            // }

            return uploadedFileIds;
        }

        private async Task<string> CreateVectorStoreAsync(List<string> uploadedFileIds, CancellationToken ct)
        {
            const int maxRetries = 3;
            const int indexingWaitTime = 10; // seconds
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Console.WriteLine($"Creating vector store attempt {attempt}/{maxRetries}...");
                    
                    // Create vector store
                    var vectorStore = await client.VectorStores.CreateVectorStoreAsync(
                        uploadedFileIds,
                        name: $"azc-session-{DateTime.Now:yyyyMMddHHmmss}",
                        cancellationToken: ct).ConfigureAwait(false);

                    if (vectorStore?.Value?.Id == null)
                    {
                        throw new Exception("No vector store ID returned");
                    }

                    // Wait for indexing
                    // Console.WriteLine($"Waiting {indexingWaitTime} seconds for indexing...");
                    await Task.Delay(TimeSpan.FromSeconds(indexingWaitTime), ct);

                    // Verify vector store is accessible
                    var testStore = await client.VectorStores.GetVectorStoreAsync(
                        vectorStore.Value.Id, 
                        ct).ConfigureAwait(false);

                    if (testStore?.Value != null)
                    {
                        // Console.WriteLine($"✅ Vector store created and verified. ID: {vectorStore.Value.Id}");
                        return vectorStore.Value.Id;
                    }
                }
                catch (Exception ex)
                {
                    // Console.WriteLine($"Attempt {attempt} failed: {ex.Message}");
                    
                    if (attempt < maxRetries)
                    {
                        var delay = attempt * 5; // Progressive delay
                        Console.WriteLine($"Retrying in {delay} seconds...");
                        await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                    }
                    else throw;
                }
            }
            
            throw new Exception("Failed to create and verify vector store after retries");
        }
        private async Task<string> GetAgentSuggestionsAsync(string vectorStoreId, string mainTspPath, CancellationToken ct)
        {
            try
            {
                // Console.WriteLine("Creating a thread for agent analysis...");
                var fileHelper = new FileHelper(mainTspPath, 
                Path.Combine(Path.GetDirectoryName(mainTspPath)!, "..", "log", "azc-errors.txt"));

                // Set up the tools for the agent
                // var fileSearchTool = new FileSearchToolResource();
                // fileSearchTool.VectorStoreIds.Add(vectorStoreId); 
                // Create or update the agent with file search capability
                // Console.WriteLine($"Creating agent with file search capability using model: {model}...");
                agent = await client.Administration.CreateAgentAsync(
                    model: model,
                    name: "AZC0030 fixer Agent",
                    instructions: AzcQueryPrompt,
                    // tools: new[] { new FileSearchToolDefinition() },
                    // toolResources: new ToolResources { FileSearch = fileSearchTool },
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
                    $@"Please analyze the uploaded files for ALL AZC analyzer violations and provide comprehensive fixes:
                    1. Examine the TypeSpec file content
                    ===FILE_CONTENT_START===
                    {fileHelper.MainTspContent}
                    ===FILE_CONTENT_END===
                    2. Review the AZC error log
                    ===ERROR_LOG_START===
                    {fileHelper.ErrorLogContent}
                    ===ERROR_LOG_END===
                    3. Fix ALL error types found (AZC0008, AZC0012, AZC0030, AZC0015, AZC0020, etc.)
                    4. Follow Azure SDK Design Guidelines for naming conventions
                    5. Provide the complete updated TypeSpec code with all violations resolved
                    
                    Return ONLY a JSON response as specified in your instructions with:
                    - Do not include any text outside the JSON object
                    - Make sure the JSON is well-formed so that it can be parsed programmatically to retrieve the updated TypeSpec content from UpdatedTsp field
                    - Complete analysis of all error types found
                    - All model renames and structural additions needed  
                    - The full updated TypeSpec content that resolves every AZC violation",
                    cancellationToken: ct).ConfigureAwait(false);

                if (message == null)
                {
                    throw new Exception("Failed to create message in thread");
                }

                // Start the analysis
                Console.WriteLine("⏳ Starting agent analysis...\n");
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
                            // Console.WriteLine("---Response Start---");
                            // Console.WriteLine(textContent.Text);
                            // Console.WriteLine("---Response End---");
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


        private async Task CreateUpdatedFileAsync(string suggestion, string mainTsp)
        {
            try
            {
                Console.WriteLine("⏳ Processing agent response...\n");
                
                // Extract JSON from response
                var jsonPayload = ExtractJsonFromResponse(suggestion);
                
                // Parse with enhanced options
                var options = new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var result = JsonSerializer.Deserialize<AzcFixResult>(jsonPayload, options);
                
                if (result?.UpdatedTsp == null)
                {
                    throw new Exception("No updated TypeSpec content found in response");
                }

                // Create backup
                // var backupPath = mainTsp + $".backup.{DateTime.Now:yyyyMMdd_HHmmss}";
                // File.Copy(mainTsp, backupPath, true);
                // Console.WriteLine($"Created backup at: {backupPath}");

                // Update file
                await File.WriteAllTextAsync(mainTsp, result.UpdatedTsp);
                Console.WriteLine($"✅ Successfully updated {Path.GetFileName(mainTsp)}");

                // Log changes
                // if (result.Analysis != null)
                // {
                //     Console.WriteLine($"\nFixed {result.Analysis.TotalAzcErrors} AZC violations:");
                //     foreach (var error in result.Analysis.ErrorTypesFound)
                //     {
                //         Console.WriteLine($"- {error}");
                //     }
                // }
            }
            catch (JsonException ex)
            {
                Console.WriteLine("JSON parsing error. Raw response:");
                Console.WriteLine(suggestion);
                Console.WriteLine($"\nError details: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating file: {ex.Message}");
                throw;
            }
        }

        private string ExtractJsonFromResponse(string response)
        {
            try
            {
                // Find the last occurrence of '{' that has a matching '}'
                int start = -1;
                int end = -1;
                int depth = 0;
                
                // Find the last JSON object in the response
                for (int i = response.Length - 1; i >= 0; i--)
                {
                    char c = response[i];
                    if (c == '}')
                    {
                        depth++;
                        if (end == -1) end = i;
                    }
                    else if (c == '{')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            start = i;
                            break;
                        }
                    }
                }

                if (start == -1 || end == -1 || start >= end)
                {
                    Console.WriteLine("Raw response for debugging:");
                    Console.WriteLine(response);
                    throw new Exception("No valid JSON object found in response");
                }

                var jsonPayload = response.Substring(start, end - start + 1);
                
                // Verify the extracted JSON is valid
                using (var document = JsonDocument.Parse(jsonPayload))
                {
                    // Additional validation for required properties
                    var root = document.RootElement;
                    if (!root.TryGetProperty("analysis", out _))
                    {
                        throw new Exception("Missing required 'analysis' property in JSON");
                    }
                    if (!root.TryGetProperty("UpdatedTsp", out _))
                    {
                        throw new Exception("Missing required 'UpdatedTsp' property in JSON");
                    }

                    // Console.WriteLine("✅ Successfully extracted and validated JSON response");
                    return jsonPayload;
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"JSON parsing error: {ex.Message}");
                Console.WriteLine("Failed to parse response. Content:");
                Console.WriteLine(response);
                throw new Exception($"Invalid JSON structure: {ex.Message}");
            }
        }


        private class AzcFixResult
        {
            [JsonPropertyName("analysis")]
            public Analysis? Analysis { get; set; }
            
            [JsonPropertyName("fixes")]
            public Fixes? Fixes { get; set; }
            
            [JsonPropertyName("UpdatedTsp")]
            public string? UpdatedTsp { get; set; }
        }

        private class Analysis
        {
            [JsonPropertyName("total_azc_errors")]
            public int TotalAzcErrors { get; set; }
            
            [JsonPropertyName("error_types_found")]
            public List<string> ErrorTypesFound { get; set; } = new();
            
            [JsonPropertyName("models_requiring_fixes")]
            public List<string> ModelsRequiringFixes { get; set; } = new();
        }

        private class Fixes
        {
            [JsonPropertyName("model_renames")]
            public List<ModelRename> ModelRenames { get; set; } = new();
            
            [JsonPropertyName("reference_updates")]
            public List<ReferenceUpdate> ReferenceUpdates { get; set; } = new();
            
            [JsonPropertyName("structural_additions")]
            public List<StructuralAddition> StructuralAdditions { get; set; } = new();
        }

        private class ModelRename
        {
            [JsonPropertyName("original")]
            public string? Original { get; set; }
            
            [JsonPropertyName("fixed")]
            public string? Fixed { get; set; }
            
            [JsonPropertyName("reason")]
            public string? Reason { get; set; }
        }

        private class ReferenceUpdate
        {
            [JsonPropertyName("location")]
            public string? Location { get; set; }
            
            [JsonPropertyName("original")]
            public string? Original { get; set; }
            
            [JsonPropertyName("fixed")]
            public string? Fixed { get; set; }
            
            [JsonPropertyName("reason")]
            public string? Reason { get; set; }
        }

        private class StructuralAddition
        {
            [JsonPropertyName("type")]
            public string? Type { get; set; }
            
            [JsonPropertyName("location")]
            public string? Location { get; set; }
            
            [JsonPropertyName("reason")]
            public string? Reason { get; set; }
        }

        public class FileHelper
        {
            public string MainTspContent { get; private set; }
            public string ErrorLogContent { get; private set; }

            public FileHelper(string mainTspPath, string logPath)
            {
                MainTspContent = File.ReadAllText(mainTspPath);
                ErrorLogContent = File.Exists(logPath) ? File.ReadAllText(logPath) : string.Empty;
            }
        }

    }
}
