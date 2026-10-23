using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ServantLauncher.Models;

namespace ServantLauncher.Services
{
    public interface IFabricService
    {
        Task<List<FabricLoaderInfo>> GetLoaderVersionsAsync(string gameVersion);
        Task<string> EnsureFabricProfileJsonAsync(string gameVersion, string loaderVersion, string versionsDir, CancellationToken ct = default);
        Task<List<string>> DownloadFabricLibrariesAsync(string gameVersion, string loaderVersion, string gameDir, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default);
    }

    public class FabricService : IFabricService
    {
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, List<FabricLoaderInfo>> _cachedLoaders = new();

        public FabricService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? HttpHelper.CreateHttpClient();
        }

        public async Task<List<FabricLoaderInfo>> GetLoaderVersionsAsync(string gameVersion)
        {
            if (_cachedLoaders.TryGetValue(gameVersion, out var cached))
                return cached;

            try
            {
                var url = $"https://meta.fabricmc.net/v2/versions/loader/{gameVersion}";
                var list = await _httpClient.GetFromJsonAsync<List<FabricLoaderInfo>>(url);
                if (list != null && list.Count > 0)
                {
                    _cachedLoaders[gameVersion] = list;
                    return list;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching Fabric loaders: {ex.Message}");
            }

            // Fallback default loaders
            var fallback = new List<FabricLoaderInfo>
            {
                new() { Loader = new FabricLoaderDetail { Version = "0.16.10", Stable = true } },
                new() { Loader = new FabricLoaderDetail { Version = "0.16.9", Stable = true } },
                new() { Loader = new FabricLoaderDetail { Version = "0.16.5", Stable = true } },
                new() { Loader = new FabricLoaderDetail { Version = "0.15.11", Stable = true } }
            };
            return fallback;
        }

        public async Task<string> EnsureFabricProfileJsonAsync(string gameVersion, string loaderVersion, string versionsDir, CancellationToken ct = default)
        {
            var profileId = $"fabric-loader-{loaderVersion}-{gameVersion}";
            var verDir = Path.Combine(versionsDir, profileId);
            Directory.CreateDirectory(verDir);
            var jsonPath = Path.Combine(verDir, $"{profileId}.json");

            if (File.Exists(jsonPath) && new FileInfo(jsonPath).Length > 0)
                return jsonPath;

            var url = $"https://meta.fabricmc.net/v2/versions/loader/{gameVersion}/{loaderVersion}/profile/json";
            var jsonContent = await _httpClient.GetStringAsync(url, ct);
            await File.WriteAllTextAsync(jsonPath, jsonContent, ct);
            return jsonPath;
        }

        public async Task<List<string>> DownloadFabricLibrariesAsync(string gameVersion, string loaderVersion, string gameDir, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default)
        {
            var resultJars = new List<string>();
            var versionsDir = Path.Combine(gameDir, "versions");
            var librariesDir = Path.Combine(gameDir, "libraries");
            var jsonPath = await EnsureFabricProfileJsonAsync(gameVersion, loaderVersion, versionsDir, ct);

            var jsonText = await File.ReadAllTextAsync(jsonPath, ct);
            var root = JsonNode.Parse(jsonText);
            var libraries = root?["libraries"]?.AsArray();

            if (libraries != null)
            {
                int count = libraries.Count;
                int current = 0;

                foreach (var lib in libraries)
                {
                    current++;
                    var name = lib?["name"]?.GetValue<string>();
                    var urlBase = lib?["url"]?.GetValue<string>() ?? "https://maven.fabricmc.net/";

                    if (!string.IsNullOrEmpty(name))
                    {
                        var relPath = MavenNameToPath(name);
                        var fullLibPath = Path.Combine(librariesDir, relPath);

                        if (!File.Exists(fullLibPath) || new FileInfo(fullLibPath).Length == 0)
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(fullLibPath)!);
                            var downloadUrl = urlBase.TrimEnd('/') + "/" + relPath.Replace('\\', '/');
                            progress?.Report((80 + (current / (double)count) * 15, $"Загрузка Fabric библиотек ({current}/{count})..."));

                            try
                            {
                                var temp = fullLibPath + ".tmp";
                                using (var resp = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
                                {
                                    if (resp.IsSuccessStatusCode)
                                    {
                                        using (var stream = await resp.Content.ReadAsStreamAsync(ct))
                                        using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                                        {
                                            await stream.CopyToAsync(fs, ct);
                                        }
                                        if (File.Exists(fullLibPath)) File.Delete(fullLibPath);
                                        File.Move(temp, fullLibPath, true);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to download {name}: {ex.Message}");
                                System.Diagnostics.Debug.WriteLine($"Failed to download {name}: {ex.Message}");
                            }
                        }

                        if (File.Exists(fullLibPath))
                        {
                            resultJars.Add(fullLibPath);
                        }
                    }
                }
            }

            return resultJars;
        }

        public static string MavenNameToPath(string mavenName)
        {
            var parts = mavenName.Split(':');
            if (parts.Length < 3) return mavenName.Replace(':', Path.DirectorySeparatorChar) + ".jar";

            var group = parts[0].Replace('.', Path.DirectorySeparatorChar);
            var artifact = parts[1];
            var version = parts[2];
            var classifier = parts.Length > 3 ? "-" + parts[3] : "";

            return Path.Combine(group, artifact, version, $"{artifact}-{version}{classifier}.jar");
        }
    }
}
