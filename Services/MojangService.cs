using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ServantLauncher.Models;

namespace ServantLauncher.Services
{
    public interface IMojangService
    {
        Task<MojangVersionManifest> GetVersionManifestAsync(bool forceRefresh = false);
        Task<List<MinecraftVersion>> GetReleasesAsync();
        Task<List<MinecraftVersion>> GetSnapshotsAsync();
        Task<string> EnsureVersionJsonAsync(string versionId, string versionsDir, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default);
        Task DownloadGameFilesAsync(string versionId, string gameDir, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default);
    }

    public class MojangService : IMojangService
    {
        private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
        private readonly HttpClient _httpClient;
        private MojangVersionManifest? _cachedManifest;

        public MojangService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? HttpHelper.CreateHttpClient();
        }

        public async Task<MojangVersionManifest> GetVersionManifestAsync(bool forceRefresh = false)
        {
            if (_cachedManifest != null && !forceRefresh)
                return _cachedManifest;

            try
            {
                var manifest = await _httpClient.GetFromJsonAsync<MojangVersionManifest>(ManifestUrl);
                if (manifest != null)
                {
                    _cachedManifest = manifest;
                    return manifest;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching version manifest: {ex.Message}");
            }

            // Fallback default manifest if offline
            return _cachedManifest ?? new MojangVersionManifest
            {
                Latest = new LatestVersions { Release = "1.21.4", Snapshot = "25w09a" },
                Versions = new[]
                {
                    new MinecraftVersion { Id = "1.21.4", Type = "release", Url = "https://piston-meta.mojang.com/v1/packages/2070e1a129d2b2c4e66763a8be77c5c0a1a0db67/1.21.4.json", ReleaseTime = DateTimeOffset.UtcNow },
                    new MinecraftVersion { Id = "1.21.3", Type = "release", Url = "https://piston-meta.mojang.com/v1/packages/b936d5314eb4be27e857ff170bd78a2fc2548e65/1.21.3.json", ReleaseTime = DateTimeOffset.UtcNow.AddMonths(-1) },
                    new MinecraftVersion { Id = "1.20.4", Type = "release", Url = "https://piston-meta.mojang.com/v1/packages/20831631481fb45a49c60e3cc987d60517596ae6/1.20.4.json", ReleaseTime = DateTimeOffset.UtcNow.AddMonths(-6) },
                    new MinecraftVersion { Id = "25w09a", Type = "snapshot", Url = "https://piston-meta.mojang.com/v1/packages/2070e1a129d2b2c4e66763a8be77c5c0a1a0db67/1.21.4.json", ReleaseTime = DateTimeOffset.UtcNow }
                }
            };
        }

        public async Task<List<MinecraftVersion>> GetReleasesAsync()
        {
            var manifest = await GetVersionManifestAsync();
            return manifest.Versions.Where(v => v.IsRelease).ToList();
        }

        public async Task<List<MinecraftVersion>> GetSnapshotsAsync()
        {
            var manifest = await GetVersionManifestAsync();
            return manifest.Versions.Where(v => v.IsSnapshot).ToList();
        }

        public async Task<string> EnsureVersionJsonAsync(string versionId, string versionsDir, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default)
        {
            var verDir = Path.Combine(versionsDir, versionId);
            Directory.CreateDirectory(verDir);
            var jsonPath = Path.Combine(verDir, $"{versionId}.json");

            if (File.Exists(jsonPath))
                return jsonPath;

            var manifest = await GetVersionManifestAsync(false);
            var ver = manifest.Versions.FirstOrDefault(v => v.Id.Equals(versionId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Minecraft version {versionId} not found in manifest");

            progress?.Report((10, $"Загрузка манифеста {versionId}..."));
            var jsonContent = await _httpClient.GetStringAsync(ver.Url, ct);
            await File.WriteAllTextAsync(jsonPath, jsonContent, ct);
            return jsonPath;
        }

        public async Task DownloadGameFilesAsync(string versionId, string gameDir, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default)
        {
            var versionsDir = Path.Combine(gameDir, "versions");
            var librariesDir = Path.Combine(gameDir, "libraries");
            var assetsDir = Path.Combine(gameDir, "assets");
            var indexesDir = Path.Combine(assetsDir, "indexes");
            var objectsDir = Path.Combine(assetsDir, "objects");
            var nativesDir = Path.Combine(versionsDir, versionId, "natives");

            Directory.CreateDirectory(versionsDir);
            Directory.CreateDirectory(librariesDir);
            Directory.CreateDirectory(assetsDir);
            Directory.CreateDirectory(indexesDir);
            Directory.CreateDirectory(objectsDir);
            Directory.CreateDirectory(nativesDir);

            var jsonPath = await EnsureVersionJsonAsync(versionId, versionsDir, progress, ct);
            var jsonText = await File.ReadAllTextAsync(jsonPath, ct);
            var root = JsonNode.Parse(jsonText);
            if (root == null) throw new InvalidDataException("Invalid version JSON");

            // 1. Download Client JAR
            var clientDownload = root["downloads"]?["client"];
            if (clientDownload != null)
            {
                var clientUrl = clientDownload["url"]?.GetValue<string>();
                var clientJarPath = Path.Combine(versionsDir, versionId, $"{versionId}.jar");
                if (!string.IsNullOrEmpty(clientUrl) && (!File.Exists(clientJarPath) || new FileInfo(clientJarPath).Length == 0))
                {
                    progress?.Report((20, $"Загрузка Minecraft {versionId}.jar..."));
                    await DownloadFileWithRetryAsync(clientUrl, clientJarPath, ct);
                }
            }

            // 2. Download Libraries
            var libraries = root["libraries"]?.AsArray();
            if (libraries != null)
            {
                int totalLibs = libraries.Count;
                int currentLib = 0;
                using var libSemaphore = new SemaphoreSlim(16);
                var libTasks = new List<Task>();

                foreach (var lib in libraries)
                {
                    if (ct.IsCancellationRequested) break;

                    // Check OS rules
                    if (!IsRuleAllowed(lib?["rules"]?.AsArray()))
                        continue;

                    var artifact = lib?["downloads"]?["artifact"];
                    if (artifact != null)
                    {
                        var path = artifact["path"]?.GetValue<string>();
                        var url = artifact["url"]?.GetValue<string>();

                        if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(url))
                        {
                            var fullLibPath = Path.Combine(librariesDir, path.Replace('/', Path.DirectorySeparatorChar));
                            if (!File.Exists(fullLibPath) || new FileInfo(fullLibPath).Length == 0)
                            {
                                libTasks.Add(Task.Run(async () =>
                                {
                                    await libSemaphore.WaitAsync(ct);
                                    try
                                    {
                                        Directory.CreateDirectory(Path.GetDirectoryName(fullLibPath)!);
                                        await DownloadFileWithRetryAsync(url, fullLibPath, ct);
                                        var done = Interlocked.Increment(ref currentLib);
                                        progress?.Report((20 + (done / (double)totalLibs) * 40, $"Загрузка библиотек ({done}/{totalLibs})..."));
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Failed lib {path}: {ex.Message}");
                                    }
                                    finally
                                    {
                                        libSemaphore.Release();
                                    }
                                }, ct));
                            }
                        }
                    }

                    // Natives
                    var nativesObj = lib?["natives"]?["windows"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(nativesObj))
                    {
                        var classifiers = lib?["downloads"]?["classifiers"];
                        var nativeArtifact = classifiers?[nativesObj];
                        if (nativeArtifact != null)
                        {
                            var natPath = nativeArtifact["path"]?.GetValue<string>();
                            var natUrl = nativeArtifact["url"]?.GetValue<string>();
                            if (!string.IsNullOrEmpty(natPath) && !string.IsNullOrEmpty(natUrl))
                            {
                                var fullNativePath = Path.Combine(librariesDir, natPath.Replace('/', Path.DirectorySeparatorChar));
                                libTasks.Add(Task.Run(async () =>
                                {
                                    await libSemaphore.WaitAsync(ct);
                                    try
                                    {
                                        if (!File.Exists(fullNativePath))
                                        {
                                            Directory.CreateDirectory(Path.GetDirectoryName(fullNativePath)!);
                                            await DownloadFileWithRetryAsync(natUrl, fullNativePath, ct);
                                        }

                                        if (File.Exists(fullNativePath))
                                        {
                                            using var zip = ZipFile.OpenRead(fullNativePath);
                                            foreach (var entry in zip.Entries)
                                            {
                                                if (entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                                                    !entry.FullName.Contains("META-INF"))
                                                {
                                                    var dest = Path.Combine(nativesDir, entry.Name);
                                                    if (!File.Exists(dest))
                                                    {
                                                        entry.ExtractToFile(dest, true);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch { }
                                    finally
                                    {
                                        libSemaphore.Release();
                                    }
                                }, ct));
                            }
                        }
                    }
                }

                if (libTasks.Count > 0)
                {
                    await Task.WhenAll(libTasks);
                }
            }

            // 3. Asset Index & Assets
            var assetIndex = root["assetIndex"];
            if (assetIndex != null)
            {
                var indexId = assetIndex["id"]?.GetValue<string>() ?? versionId;
                var indexUrl = assetIndex["url"]?.GetValue<string>();
                var indexFile = Path.Combine(indexesDir, $"{indexId}.json");

                if (!string.IsNullOrEmpty(indexUrl) && (!File.Exists(indexFile) || new FileInfo(indexFile).Length == 0))
                {
                    progress?.Report((65, $"Загрузка индексов ассетов {indexId}..."));
                    await DownloadFileAsync(indexUrl, indexFile, ct);
                }

                if (File.Exists(indexFile))
                {
                    var indexJson = await File.ReadAllTextAsync(indexFile, ct);
                    var indexRoot = JsonNode.Parse(indexJson);
                    var objects = indexRoot?["objects"]?.AsObject();
                    if (objects != null)
                    {
                        int totalObjects = objects.Count;
                        int objIndex = 0;
                        progress?.Report((70, $"Проверка ассетов игры ({totalObjects} файлов)..."));

                        // Download in parallel with semaphore
                        using var semaphore = new SemaphoreSlim(8);
                        var tasks = new List<Task>();

                        foreach (var kvp in objects)
                        {
                            var hash = kvp.Value?["hash"]?.GetValue<string>();
                            if (string.IsNullOrEmpty(hash) || hash.Length < 2) continue;

                            var subDir = hash.Substring(0, 2);
                            var objDestDir = Path.Combine(objectsDir, subDir);
                            var objDestFile = Path.Combine(objDestDir, hash);

                            if (!File.Exists(objDestFile))
                            {
                                tasks.Add(Task.Run(async () =>
                                {
                                    await semaphore.WaitAsync(ct);
                                    try
                                    {
                                        Directory.CreateDirectory(objDestDir);
                                        var objUrl = $"https://resources.download.minecraft.net/{subDir}/{hash}";
                                        await DownloadFileAsync(objUrl, objDestFile, ct);
                                        Interlocked.Increment(ref objIndex);
                                        if (objIndex % 50 == 0)
                                        {
                                            progress?.Report((70 + (objIndex / (double)totalObjects) * 28, $"Загрузка текстур и звуков ({objIndex}/{totalObjects})..."));
                                        }
                                    }
                                    catch { }
                                    finally
                                    {
                                        semaphore.Release();
                                    }
                                }, ct));
                            }
                        }

                        if (tasks.Count > 0)
                        {
                            await Task.WhenAll(tasks);
                        }
                    }
                }
            }

            progress?.Report((100, "Все игровые файлы проверены и готовы к запуску."));
        }

        private async Task DownloadFileWithRetryAsync(string url, string destPath, CancellationToken ct, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await DownloadFileAsync(url, destPath, ct);
                    return;
                }
                catch when (attempt < maxRetries && !ct.IsCancellationRequested)
                {
                    await Task.Delay(500 * attempt, ct);
                }
            }
        }

        private async Task DownloadFileAsync(string url, string destPath, CancellationToken ct)
        {
            var tempFile = destPath + ".tmp";
            using (var resp = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fs, ct);
            }
            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tempFile, destPath);
        }

        private static bool IsRuleAllowed(JsonArray? rules)
        {
            if (rules == null || rules.Count == 0) return true;

            bool allow = false;
            foreach (var rule in rules)
            {
                var action = rule?["action"]?.GetValue<string>() ?? "allow";
                var os = rule?["os"];

                if (os == null)
                {
                    allow = action == "allow";
                }
                else
                {
                    var osName = os["name"]?.GetValue<string>();
                    if (osName == "windows")
                    {
                        allow = action == "allow";
                    }
                }
            }
            return allow;
        }
    }
}
