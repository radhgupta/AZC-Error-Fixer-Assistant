namespace AzcAnalyzerFixer.Configuration
{
    public static class AppSettings
    {
        public static string ProjectEndpoint => "https://dotnet-sdk-analyzer-fix-resource.services.ai.azure.com/api/projects/dotnet-sdk-analyzer-fixer";
        public static string Model => "gpt-4o";
        public static string MainTspPath => @"C:\Users\radhgupta\Desktop\typespec\typesc-sdk-sample\src\main.tsp";
        public static string LogPath => @"C:\Users\radhgupta\Desktop\typespec\typesc-sdk-sample\log\azc-errors.txt";
        public static string WorkspacePath => @"C:\Users\radhgupta\Desktop\typespec\typesc-sdk-sample";
        public static string TypeSpecSrcPath => Path.Combine(WorkspacePath, "src");
        public static int maxIterations = 5;

        public const string initialPrompt = @"
You are an expert Azure SDK developer and TypeSpec author. I will first send you a list of AZC-fix suggestions; your job is to apply each suggestion to the client.tsp file and return the fully updated file.

### SYSTEM INSTRUCTIONS
- All files (main.tsp, client.tsp, azc-errors.txt) have been uploaded to the vector store.  
- Use the FileSearchTool to retrieve any file content by filename.  
- Never modify main.tsp—only client.tsp may change.  
- Maintain TypeSpec 1.0+ syntax and comply with Azure SDK design guidelines.  
- After applying all suggestions, ensure the updated client.tsp compiles without syntax errors.

Now I will send you the AZC suggestions and the client.tsp file. Your task is to apply all suggestions and return the updated client.tsp file in a well-formed JSON object with the following schema:

{
  ""UpdatedClientTsp"": ""<complete client.tsp content here>""
}


Please ensure the returned response is a valid JSON object with the updated client.tsp content in the UpdatedClientTsp field. Do not include any additional text or explanations outside of this JSON structure.
";
    }
}
