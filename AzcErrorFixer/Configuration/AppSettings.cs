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
    }
}
