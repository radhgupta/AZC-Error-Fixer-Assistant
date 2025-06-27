using System;
using System.Text.Json;

namespace AzcAnalyzerFixer.Infrastructure.Helpers
{
    public static class JsonHelper
    {
        /// <summary>
        /// Extracts the last valid JSON object from a raw text response string.
        /// Also validates that required properties are present.
        /// </summary>
        /// <param name="response">The raw response text that includes JSON.</param>
        /// <returns>Extracted JSON string.</returns>
        /// <exception cref="Exception">If no valid JSON is found or required properties are missing.</exception>
        public static string ExtractJsonPayload(string response)
        {
            int start = -1;
            int end = -1;
            int depth = 0;

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
                throw new Exception("Unable to extract valid JSON object from response.");
            }

            var json = response.Substring(start, end - start + 1);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("analysis", out _) || !root.TryGetProperty("UpdatedClientTsp", out _))
            {
                throw new Exception("Missing required JSON properties: 'analysis' or 'UpdatedClientTsp'");
            }

            return json;
        }
    }
}
