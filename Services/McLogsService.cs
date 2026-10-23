using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ServantLauncher.Services
{
    public interface IMcLogsService
    {
        Task<string?> UploadLogAsync(string logContent);
    }

    public class McLogsService : IMcLogsService
    {
        private const string McLogsApiUrl = "https://api.mclo.gs/1/log";
        private readonly HttpClient _httpClient;

        public McLogsService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? HttpHelper.CreateHttpClient();
        }

        public async Task<string?> UploadLogAsync(string logContent)
        {
            if (string.IsNullOrWhiteSpace(logContent))
                return null;

            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("content", logContent)
                });

                var response = await _httpClient.PostAsync(McLogsApiUrl, content);
                var jsonStr = await response.Content.ReadAsStringAsync();

                var node = JsonNode.Parse(jsonStr);
                if (node != null && node["success"]?.GetValue<bool>() == true)
                {
                    return node["url"]?.GetValue<string>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[McLogsService] Upload error: {ex.Message}");
            }

            return null;
        }
    }
}
