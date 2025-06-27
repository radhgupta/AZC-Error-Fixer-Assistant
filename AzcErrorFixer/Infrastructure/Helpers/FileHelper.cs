using System.IO;

namespace AzcAnalyzerFixer.Infrastructure.Helpers
{
    public class FileHelper
    {
        public string MainTspContent { get; private set; }
        public string ClientTspContent { get; private set; }
        public string ErrorLogContent { get; private set; }

        public FileHelper(string mainTspPath, string logPath)
        {
            MainTspContent = File.ReadAllText(mainTspPath);
            string clientTspPath = Path.Combine(Path.GetDirectoryName(mainTspPath)!, "client.tsp");
            ClientTspContent = File.Exists(clientTspPath) ? File.ReadAllText(clientTspPath) : "";
            ErrorLogContent = File.Exists(logPath) ? File.ReadAllText(logPath) : "";
        }

        public void WriteClientTsp(string TspPath, string content)
        {
            string clientTspPath = Path.Combine(TspPath, "client.tsp");
            File.WriteAllText(clientTspPath, content);
        }
    }
}
