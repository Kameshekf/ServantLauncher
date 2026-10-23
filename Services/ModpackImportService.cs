using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ServantLauncher.Models;

namespace ServantLauncher.Services
{
    public interface IModpackImportService
    {
        Task<GameProfile> ImportMrpackAsync(string mrpackFilePath, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default);
        Task UpdateProfileFromMrpackAsync(GameProfile profile, string mrpackFilePath, string? newPackVersion = null, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default);
        Task<string> ExportMrpackAsync(GameProfile profile, string exportFilePath, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default);
    }

    public class ModpackImportService : IModpackImportService
    {
        private readonly IProfileManager _profileManager;
        private readonly HttpClient _httpClient;

        public ModpackImportService(IProfileManager profileManager, HttpClient? httpClient = null)
        {
            _profileManager = profileManager;
            _httpClient = httpClient ?? HttpHelper.CreateHttpClient();
        }

        public async Task<GameProfile> ImportMrpackAsync(string mrpackFilePath, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default)
        {
            if (!File.Exists(mrpackFilePath))
                throw new FileNotFoundException($"Файл модпака не найден: {mrpackFilePath}");

            return await Task.Run(async () =>
            {
                progress?.Report((5, "Чтение структуры модпака .mrpack..."));

                using var zip = ZipFile.OpenRead(mrpackFilePath);
                var indexEntry = zip.GetEntry("modrinth.index.json")
                    ?? throw new InvalidOperationException("Файл modrinth.index.json не найден внутри архива mrpack.");

                using (var indexStream = indexEntry.Open())
                using (var reader = new StreamReader(indexStream))
                {
                    var jsonText = await reader.ReadToEndAsync(ct);
                    var indexNode = JsonNode.Parse(jsonText)
                        ?? throw new InvalidOperationException("Не удалось прочитать манифест модпака.");

                    var packName = indexNode["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(mrpackFilePath);
                    var dependencies = indexNode["dependencies"]?.AsObject();
                    var mcVersion = dependencies?["minecraft"]?.GetValue<string>() ?? "1.21.4";
                    
                    var loaderType = LoaderType.Vanilla;
                    string loaderVersion = string.Empty;

                    if (dependencies != null)
                    {
                        if (dependencies.ContainsKey("fabric-loader"))
                        {
                            loaderType = LoaderType.Fabric;
                            loaderVersion = dependencies["fabric-loader"]?.GetValue<string>() ?? "0.16.10";
                        }
                        else if (dependencies.ContainsKey("quilt-loader"))
                        {
                            loaderType = LoaderType.Fabric;
                            loaderVersion = dependencies["quilt-loader"]?.GetValue<string>() ?? "0.16.10";
                        }
                    }

                    var newProfile = new GameProfile
                    {
                        Name = packName,
                        GameVersion = mcVersion,
                        LoaderType = loaderType,
                        LoaderVersion = loaderVersion,
                        AllocatedRamMb = 4096,
                        ServerPackVersion = indexNode["versionId"]?.GetValue<string>() ?? "1.0.0"
                    };

                    _profileManager.AddProfile(newProfile);
                    var instanceGameDir = _profileManager.GetProfileGameDir(newProfile);
                    Directory.CreateDirectory(instanceGameDir);

                    var managedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // 1. Collect and extract override entries with buffered async I/O
                    var overrideEntries = zip.Entries.Where(e =>
                        (e.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase) ||
                         e.FullName.StartsWith("client-overrides/", StringComparison.OrdinalIgnoreCase)) &&
                        !string.IsNullOrEmpty(e.Name)).ToList();

                    int totalOverrides = overrideEntries.Count;
                    progress?.Report((12, $"Распаковка конфигураций и ресурсов (0/{totalOverrides})..."));

                    int extractedCount = 0;
                    foreach (var entry in overrideEntries)
                    {
                        ct.ThrowIfCancellationRequested();
                        string relative;
                        if (entry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
                            relative = entry.FullName.Substring("overrides/".Length);
                        else
                            relative = entry.FullName.Substring("client-overrides/".Length);

                        managedFiles.Add(relative.Replace('\\', '/').TrimStart('/'));
                        var targetPath = Path.Combine(instanceGameDir, relative);

                        await ExtractEntryAsync(entry, targetPath, ct);
                        extractedCount++;

                        if (extractedCount % 10 == 0 || extractedCount == totalOverrides)
                        {
                            double p = 12.0 + ((double)extractedCount / Math.Max(1, totalOverrides)) * 13.0;
                            progress?.Report((p, $"Распаковка ресурсов ({extractedCount}/{totalOverrides}): {entry.Name}"));
                        }
                    }

                    // 2. Extract pack icon if present
                    try
                    {
                        var iconEntry = zip.Entries.FirstOrDefault(e =>
                            e.FullName.Equals("icon.png", StringComparison.OrdinalIgnoreCase) ||
                            e.FullName.EndsWith("/icon.png", StringComparison.OrdinalIgnoreCase) ||
                            e.FullName.Equals("logo.png", StringComparison.OrdinalIgnoreCase));
                        if (iconEntry != null)
                        {
                            var targetIcon = Path.Combine(instanceGameDir, "icon.png");
                            await ExtractEntryAsync(iconEntry, targetIcon, ct);
                            newProfile.CustomIconPath = targetIcon;
                            _profileManager.SaveProfiles();
                        }
                    }
                    catch { }

                    // 3. Download indexed mod files in parallel
                    var files = indexNode["files"]?.AsArray();
                    if (files != null && files.Count > 0)
                    {
                        var fileItems = new List<(string relativePath, string downloadUrl, string fileName)>();
                        foreach (var f in files)
                        {
                            var rel = f?["path"]?.GetValue<string>();
                            var url = f?["downloads"]?.AsArray()?.FirstOrDefault()?.GetValue<string>();
                            if (!string.IsNullOrEmpty(rel) && !string.IsNullOrEmpty(url))
                            {
                                fileItems.Add((rel, url, Path.GetFileName(rel)));
                                managedFiles.Add(rel.Replace('\\', '/').TrimStart('/'));
                            }
                        }

                        int totalMods = fileItems.Count;
                        int completedMods = 0;

                        var parallelOptions = new ParallelOptions
                        {
                            MaxDegreeOfParallelism = 6,
                            CancellationToken = ct
                        };

                        await Parallel.ForEachAsync(fileItems, parallelOptions, async (item, token) =>
                        {
                            var targetFile = Path.Combine(instanceGameDir, item.relativePath);
                            var dir = Path.GetDirectoryName(targetFile);
                            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                            if (!File.Exists(targetFile))
                            {
                                var tempFile = targetFile + ".tmp";
                                try
                                {
                                    using var resp = await _httpClient.GetAsync(item.downloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
                                    if (resp.IsSuccessStatusCode)
                                    {
                                        await using var s = await resp.Content.ReadAsStreamAsync(token);
                                        await using var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
                                        await s.CopyToAsync(fs, 65536, token);
                                        await fs.FlushAsync(token);
                                        fs.Close();

                                        if (File.Exists(targetFile)) File.Delete(targetFile);
                                        File.Move(tempFile, targetFile, true);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[ModpackImport] Failed to download {item.fileName}: {ex.Message}");
                                    try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                                }
                            }

                            int done = Interlocked.Increment(ref completedMods);
                            double pct = 25.0 + ((double)done / totalMods) * 70.0;
                            progress?.Report((pct, $"Загрузка модов ({done}/{totalMods}): {item.fileName}"));
                        });
                    }

                    // 4. Save initial server manifest for future safe updates
                    try
                    {
                        var manifestFile = Path.Combine(instanceGameDir, ".server_manifest_files.json");
                        var manifestJson = System.Text.Json.JsonSerializer.Serialize(managedFiles.ToList(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(manifestFile, manifestJson, ct);
                    }
                    catch { }

                    progress?.Report((100, "Импорт модпака успешно завершен!"));
                    return newProfile;
                }
            }, ct);
        }

        public async Task UpdateProfileFromMrpackAsync(GameProfile profile, string mrpackFilePath, string? newPackVersion = null, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default)
        {
            if (!File.Exists(mrpackFilePath))
                throw new FileNotFoundException($"Файл обновления сборки не найден: {mrpackFilePath}");

            await Task.Run(async () =>
            {
                progress?.Report((5, "Чтение структуры обновления сборки (.mrpack)..."));

                var instanceGameDir = _profileManager.GetProfileGameDir(profile);
                Directory.CreateDirectory(instanceGameDir);

                using var zip = ZipFile.OpenRead(mrpackFilePath);
                var indexEntry = zip.GetEntry("modrinth.index.json")
                    ?? throw new InvalidOperationException("Файл modrinth.index.json не найден внутри архива mrpack.");

                using var indexStream = indexEntry.Open();
                using var reader = new StreamReader(indexStream);
                var jsonText = await reader.ReadToEndAsync(ct);
                var indexNode = JsonNode.Parse(jsonText)
                    ?? throw new InvalidOperationException("Не удалось прочитать манифест модпака.");

                var dependencies = indexNode["dependencies"]?.AsObject();
                var mcVersion = dependencies?["minecraft"]?.GetValue<string>();

                var loaderType = profile.LoaderType;
                string loaderVersion = profile.LoaderVersion;

                if (dependencies != null)
                {
                    if (dependencies.ContainsKey("fabric-loader"))
                    {
                        loaderType = LoaderType.Fabric;
                        loaderVersion = dependencies["fabric-loader"]?.GetValue<string>() ?? "0.16.10";
                    }
                    else if (dependencies.ContainsKey("quilt-loader"))
                    {
                        loaderType = LoaderType.Fabric;
                        loaderVersion = dependencies["quilt-loader"]?.GetValue<string>() ?? "0.16.10";
                    }
                }

                // 1. Read previously managed server files to safely remove only files that belong to the server pack
                var manifestFile = Path.Combine(instanceGameDir, ".server_manifest_files.json");
                var oldManagedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(manifestFile))
                {
                    try
                    {
                        var text = await File.ReadAllTextAsync(manifestFile, ct);
                        var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(text);
                        if (list != null)
                        {
                            foreach (var item in list)
                            {
                                var normalized = item.Replace('\\', '/').TrimStart('/');
                                if (!string.IsNullOrEmpty(normalized))
                                    oldManagedFiles.Add(normalized);
                            }
                        }
                    }
                    catch { }
                }

                var newManagedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 2. Identify new overrides
                var overrideEntries = zip.Entries.Where(e =>
                    (e.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase) ||
                     e.FullName.StartsWith("client-overrides/", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrEmpty(e.Name)).ToList();

                foreach (var entry in overrideEntries)
                {
                    string relative;
                    if (entry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
                        relative = entry.FullName.Substring("overrides/".Length).Replace('\\', '/').TrimStart('/');
                    else
                        relative = entry.FullName.Substring("client-overrides/".Length).Replace('\\', '/').TrimStart('/');

                    if (!string.IsNullOrEmpty(relative))
                        newManagedFiles.Add(relative);
                }

                // 3. Identify new mods/files from index
                var files = indexNode["files"]?.AsArray();
                var fileItems = new List<(string relativePath, string downloadUrl, string fileName)>();
                if (files != null)
                {
                    foreach (var f in files)
                    {
                        var rel = f?["path"]?.GetValue<string>();
                        var url = f?["downloads"]?.AsArray()?.FirstOrDefault()?.GetValue<string>();
                        var p = rel?.Replace('\\', '/')?.TrimStart('/');
                        if (!string.IsNullOrEmpty(p))
                        {
                            newManagedFiles.Add(p);
                        }
                        if (!string.IsNullOrEmpty(rel) && !string.IsNullOrEmpty(url))
                        {
                            fileItems.Add((rel, url, Path.GetFileName(rel)));
                        }
                    }
                }

                // 4. Safe Removal: Only delete files that WERE managed by previous server pack, but ARE NOT in the new pack.
                // USER's custom added mods and files are NOT in oldManagedFiles, so they are NEVER touched!
                progress?.Report((15, "Очистка устаревших серверных файлов (пользовательские моды сохраняются)..."));
                foreach (var oldRel in oldManagedFiles)
                {
                    if (!newManagedFiles.Contains(oldRel))
                    {
                        var fullOldPath = Path.Combine(instanceGameDir, oldRel.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(fullOldPath))
                        {
                            try { File.Delete(fullOldPath); } catch { }
                        }
                    }
                }

                // 5. Extract new overrides using buffered async I/O
                int totalOverrides = overrideEntries.Count;
                progress?.Report((22, $"Применение конфигураций и ресурсов обновления (0/{totalOverrides})..."));
                int extractedCount = 0;
                foreach (var entry in overrideEntries)
                {
                    ct.ThrowIfCancellationRequested();
                    string relative;
                    if (entry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
                        relative = entry.FullName.Substring("overrides/".Length);
                    else
                        relative = entry.FullName.Substring("client-overrides/".Length);

                    var targetPath = Path.Combine(instanceGameDir, relative);
                    await ExtractEntryAsync(entry, targetPath, ct);
                    extractedCount++;

                    if (extractedCount % 10 == 0 || extractedCount == totalOverrides)
                    {
                        double p = 22.0 + ((double)extractedCount / Math.Max(1, totalOverrides)) * 13.0;
                        progress?.Report((p, $"Применение ресурсов ({extractedCount}/{totalOverrides}): {entry.Name}"));
                    }
                }

                // 6. Download new/updated mods in parallel
                if (fileItems.Count > 0)
                {
                    int totalMods = fileItems.Count;
                    int completedMods = 0;

                    var parallelOptions = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 6,
                        CancellationToken = ct
                    };

                    await Parallel.ForEachAsync(fileItems, parallelOptions, async (item, token) =>
                    {
                        var targetFile = Path.Combine(instanceGameDir, item.relativePath);
                        var dir = Path.GetDirectoryName(targetFile);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                        if (!File.Exists(targetFile))
                        {
                            var tempFile = targetFile + ".tmp";
                            try
                            {
                                using var resp = await _httpClient.GetAsync(item.downloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
                                if (resp.IsSuccessStatusCode)
                                {
                                    await using var s = await resp.Content.ReadAsStreamAsync(token);
                                    await using var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
                                    await s.CopyToAsync(fs, 65536, token);
                                    await fs.FlushAsync(token);
                                    fs.Close();

                                    if (File.Exists(targetFile)) File.Delete(targetFile);
                                    File.Move(tempFile, targetFile, true);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ModpackUpdate] Failed to download {item.fileName}: {ex.Message}");
                                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                            }
                        }

                        int done = Interlocked.Increment(ref completedMods);
                        double pct = 35.0 + ((double)done / totalMods) * 60.0;
                        progress?.Report((pct, $"Обновление модов ({done}/{totalMods}): {item.fileName}"));
                    });
                }

                // 7. Update profile properties
                if (!string.IsNullOrEmpty(mcVersion)) profile.GameVersion = mcVersion;
                profile.LoaderType = loaderType;
                if (!string.IsNullOrEmpty(loaderVersion)) profile.LoaderVersion = loaderVersion;

                var detectedVer = newPackVersion ?? indexNode["versionId"]?.GetValue<string>() ?? DateTime.UtcNow.ToString("yyyy.MM.dd");
                profile.ServerPackVersion = detectedVer;

                // 8. Save updated server manifest
                try
                {
                    var manifestJson = System.Text.Json.JsonSerializer.Serialize(newManagedFiles.ToList(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(manifestFile, manifestJson, ct);
                }
                catch { }

                _profileManager.UpdateProfile(profile);
                progress?.Report((100, "Сборка сервера успешно обновлена!"));
            }, ct);
        }

        private static async Task ExtractEntryAsync(ZipArchiveEntry entry, string destinationPath, CancellationToken ct)
        {
            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            await using var entryStream = entry.Open();
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
            await entryStream.CopyToAsync(fileStream, 65536, ct);
        }

        public async Task<string> ExportMrpackAsync(GameProfile profile, string exportFilePath, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default)
        {
            var instanceGameDir = _profileManager.GetProfileGameDir(profile);
            if (!Directory.Exists(instanceGameDir))
            {
                throw new DirectoryNotFoundException($"Папка сборки не найдена: {instanceGameDir}");
            }

            progress?.Report((5, "Анализ модов и вычисление контрольных сумм..."));

            var modsDir = _profileManager.GetProfileModsDir(profile);
            var modFiles = Directory.Exists(modsDir)
                ? Directory.GetFiles(modsDir, "*.jar", SearchOption.TopDirectoryOnly).Concat(Directory.GetFiles(modsDir, "*.jar.disabled", SearchOption.TopDirectoryOnly)).ToList()
                : new List<string>();

            // Calculate hashes for all mods
            var localModsInfo = new List<(string filePath, string fileName, string sha1, string sha512, long size)>();
            for (int i = 0; i < modFiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var file = modFiles[i];
                var (sha1, sha512) = ModrinthService.ComputeFileHashes(file);
                var fi = new FileInfo(file);
                localModsInfo.Add((file, fi.Name, sha1, sha512, fi.Length));
            }

            // Query Modrinth API for mod files
            progress?.Report((20, "Проверка файлов в базе Modrinth..."));
            var modrinthIndexFiles = new JsonArray();
            var unindexedModFiles = new List<string>();

            if (localModsInfo.Count > 0)
            {
                try
                {
                    var payload = new
                    {
                        hashes = localModsInfo.Select(m => m.sha512).ToList(),
                        algorithm = "sha512"
                    };

                    var response = await _httpClient.PostAsJsonAsync("https://api.modrinth.com/v2/version_files", payload, ct);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(ct);
                        var rootNode = JsonNode.Parse(json)?.AsObject();

                        foreach (var m in localModsInfo)
                        {
                            if (rootNode != null && rootNode.TryGetPropertyValue(m.sha512, out var verNode) && verNode != null)
                            {
                                var filesArr = verNode["files"]?.AsArray();
                                var matchingFile = filesArr?.FirstOrDefault(f =>
                                    string.Equals(f?["hashes"]?["sha512"]?.GetValue<string>(), m.sha512, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(f?["filename"]?.GetValue<string>(), m.fileName, StringComparison.OrdinalIgnoreCase));

                                var downloadUrl = matchingFile?["url"]?.GetValue<string>()
                                    ?? filesArr?.FirstOrDefault()?["url"]?.GetValue<string>();

                                if (!string.IsNullOrEmpty(downloadUrl))
                                {
                                    var fileEntry = new JsonObject
                                    {
                                        ["path"] = $"mods/{m.fileName}",
                                        ["hashes"] = new JsonObject
                                        {
                                            ["sha1"] = m.sha1,
                                            ["sha512"] = m.sha512
                                        },
                                        ["env"] = new JsonObject
                                        {
                                            ["client"] = "required",
                                            ["server"] = "required"
                                        },
                                        ["downloads"] = new JsonArray { downloadUrl },
                                        ["fileSize"] = m.size
                                    };
                                    modrinthIndexFiles.Add(fileEntry);
                                    continue;
                                }
                            }

                            // Mod not on Modrinth CDN
                            unindexedModFiles.Add(m.filePath);
                        }
                    }
                    else
                    {
                        unindexedModFiles.AddRange(localModsInfo.Select(m => m.filePath));
                    }
                }
                catch
                {
                    unindexedModFiles.AddRange(localModsInfo.Select(m => m.filePath));
                }
            }

            // Create modrinth.index.json
            progress?.Report((40, "Формирование индекса модпака..."));
            var dependenciesObj = new JsonObject
            {
                ["minecraft"] = profile.GameVersion
            };

            if (profile.LoaderType == LoaderType.Fabric)
            {
                dependenciesObj["fabric-loader"] = string.IsNullOrEmpty(profile.LoaderVersion) ? "0.16.10" : profile.LoaderVersion;
            }

            var rootIndex = new JsonObject
            {
                ["formatVersion"] = 1,
                ["game"] = "minecraft",
                ["versionId"] = "1.0.0",
                ["name"] = profile.Name,
                ["summary"] = $"Экспортировано из Servant Launcher ({DateTime.Now:yyyy-MM-dd})",
                ["files"] = modrinthIndexFiles,
                ["dependencies"] = dependenciesObj
            };

            progress?.Report((60, "Упаковка архива .mrpack..."));

            var dir = Path.GetDirectoryName(exportFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(exportFilePath)) File.Delete(exportFilePath);

            using (var zip = ZipFile.Open(exportFilePath, ZipArchiveMode.Create))
            {
                // Write modrinth.index.json
                var indexEntry = zip.CreateEntry("modrinth.index.json", CompressionLevel.Optimal);
                using (var writer = new StreamWriter(indexEntry.Open()))
                {
                    await writer.WriteAsync(rootIndex.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }

                // Add unindexed mods to overrides/mods/
                foreach (var unindexedMod in unindexedModFiles)
                {
                    if (File.Exists(unindexedMod))
                    {
                        var rel = Path.Combine("overrides", "mods", Path.GetFileName(unindexedMod)).Replace('\\', '/');
                        zip.CreateEntryFromFile(unindexedMod, rel, CompressionLevel.Optimal);
                    }
                }

                // Add configs, resourcepacks, shaderpacks to overrides/
                var overrideDirs = new[] { "config", "resourcepacks", "shaderpacks", "defaultconfigs" };
                foreach (var folder in overrideDirs)
                {
                    var sourceFolder = Path.Combine(instanceGameDir, folder);
                    if (Directory.Exists(sourceFolder))
                    {
                        var allFiles = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories);
                        foreach (var file in allFiles)
                        {
                            var relative = Path.GetRelativePath(instanceGameDir, file).Replace('\\', '/');
                            var entryPath = $"overrides/{relative}";
                            zip.CreateEntryFromFile(file, entryPath, CompressionLevel.Optimal);
                        }
                    }
                }

                // Add options.txt if exists
                var optionsTxt = Path.Combine(instanceGameDir, "options.txt");
                if (File.Exists(optionsTxt))
                {
                    zip.CreateEntryFromFile(optionsTxt, "overrides/options.txt", CompressionLevel.Optimal);
                }
            }

            progress?.Report((100, $"Сборка успешно экспортирована в {Path.GetFileName(exportFilePath)}!"));
            return exportFilePath;
        }
    }
}