using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using ServantLauncher.Models;

namespace ServantLauncher.Services
{
    public interface IModrinthService
    {
        Task<ModrinthSearchResult> SearchModsAsync(string query, string? gameVersion = null, string? loader = null, int limit = 20, int offset = 0, string projectType = "mod");
        Task<ModrinthProjectDetails?> GetProjectDetailsAsync(string projectIdOrSlug);
        Task<List<ModrinthVersion>> GetProjectVersionsAsync(string projectIdOrSlug, string? gameVersion = null, string? loader = null);
        Task<string> InstallModVersionAsync(ModrinthVersion version, string modsDirectory, CancellationToken ct = default);
        Task<string> InstallModWithDependenciesAsync(ModrinthVersion version, string modsDirectory, string? gameVersion = null, string? loader = null, CancellationToken ct = default);
        Task<Dictionary<string, ModrinthVersion>> CheckModUpdatesAsync(IEnumerable<InstalledMod> mods, string gameVersion, string? loader, CancellationToken ct = default);
        Task<string> UpdateModAsync(InstalledMod mod, ModrinthVersion newVersion, string modsDirectory, string? gameVersion = null, string? loader = null, CancellationToken ct = default);
        Task<int> UpdateAllModsAsync(IEnumerable<InstalledMod> modsWithUpdates, string modsDirectory, string gameVersion, string? loader, IProgress<(int current, int total, string modName)>? progress = null, CancellationToken ct = default);
        List<InstalledMod> GetInstalledMods(string modsDirectory);
        List<InstalledMod> GetInstalledContent(string profileGameDir);
        Task EnrichInstalledModsAsync(IEnumerable<InstalledMod> mods, CancellationToken ct = default);
        bool ToggleMod(string fullPath);
        bool DeleteMod(string fullPath);
    }

    public class ModrinthService : IModrinthService
    {
        private const string BaseApiUrl = "https://api.modrinth.com/v2";
        private readonly HttpClient _httpClient;
        private readonly IImageCacheService _imageCacheService;

        public ModrinthService(HttpClient? httpClient = null, IImageCacheService? imageCacheService = null)
        {
            _httpClient = httpClient ?? HttpHelper.CreateHttpClient();
            _imageCacheService = imageCacheService ?? new ImageCacheService(_httpClient);
        }

        public async Task<ModrinthSearchResult> SearchModsAsync(string query, string? gameVersion = null, string? loader = null, int limit = 30, int offset = 0, string projectType = "mod")
        {
            try
            {
                var facetGroups = new List<string>();
                facetGroups.Add($"[\"project_type:{projectType}\"]");

                if (projectType == "mod" && !string.IsNullOrEmpty(loader) && !string.Equals(loader, "Vanilla", StringComparison.OrdinalIgnoreCase))
                {
                    facetGroups.Add($"[\"categories:{loader.ToLowerInvariant()}\"]");
                }

                if (!string.IsNullOrEmpty(gameVersion))
                {
                    facetGroups.Add($"[\"versions:{gameVersion}\"]");
                }

                var facetsParam = Uri.EscapeDataString($"[{string.Join(",", facetGroups)}]");
                var queryParam = !string.IsNullOrWhiteSpace(query) ? $"&query={Uri.EscapeDataString(query.Trim())}" : "";
                var url = $"{BaseApiUrl}/search?facets={facetsParam}{queryParam}&limit={limit}&offset={offset}&index=downloads";

                var result = await _httpClient.GetFromJsonAsync<ModrinthSearchResult>(url);
                if (result != null && result.Hits.Count > 0)
                {
                    // Load icons in parallel
                    foreach (var hit in result.Hits)
                    {
                        if (!string.IsNullOrEmpty(hit.IconUrl))
                        {
                            _ = Task.Run(async () =>
                            {
                                hit.IconBitmap = await _imageCacheService.LoadImageAsync(hit.IconUrl);
                            });
                        }
                    }
                    return result;
                }

                // If 0 results were found and we filtered by a specific game version (e.g. snapshot 26.x), fallback to searching without version filter
                if (!string.IsNullOrEmpty(gameVersion))
                {
                    var fallbackGroups = new List<string>();
                    fallbackGroups.Add("[\"project_type:mod\"]");
                    if (!string.IsNullOrEmpty(loader) && !string.Equals(loader, "Vanilla", StringComparison.OrdinalIgnoreCase))
                    {
                        fallbackGroups.Add($"[\"categories:{loader.ToLowerInvariant()}\"]");
                    }
                    var fallbackFacets = Uri.EscapeDataString($"[{string.Join(",", fallbackGroups)}]");
                    var fallbackUrl = $"{BaseApiUrl}/search?facets={fallbackFacets}{queryParam}&limit={limit}&offset={offset}&index=downloads";
                    var fallbackRes = await _httpClient.GetFromJsonAsync<ModrinthSearchResult>(fallbackUrl);
                    if (fallbackRes != null && fallbackRes.Hits.Count > 0)
                    {
                        foreach (var hit in fallbackRes.Hits)
                        {
                            if (!string.IsNullOrEmpty(hit.IconUrl))
                            {
                                _ = Task.Run(async () =>
                                {
                                    hit.IconBitmap = await _imageCacheService.LoadImageAsync(hit.IconUrl);
                                });
                            }
                        }
                        return fallbackRes;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error searching Modrinth: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Error searching Modrinth: {ex.Message}");
            }
            return new ModrinthSearchResult();
        }

        public async Task<ModrinthProjectDetails?> GetProjectDetailsAsync(string projectIdOrSlug)
        {
            try
            {
                var url = $"{BaseApiUrl}/project/{projectIdOrSlug}";
                var details = await _httpClient.GetFromJsonAsync<ModrinthProjectDetails>(url);
                return details;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching project details: {ex.Message}");
                return null;
            }
        }

        public async Task<List<ModrinthVersion>> GetProjectVersionsAsync(string projectIdOrSlug, string? gameVersion = null, string? loader = null)
        {
            try
            {
                var queryParams = new List<string>();

                if (!string.IsNullOrEmpty(loader) && !string.Equals(loader, "Vanilla", StringComparison.OrdinalIgnoreCase))
                {
                    queryParams.Add($"loaders={Uri.EscapeDataString($"[\"{loader.ToLowerInvariant()}\"]")}");
                }

                if (!string.IsNullOrEmpty(gameVersion))
                {
                    queryParams.Add($"game_versions={Uri.EscapeDataString($"[\"{gameVersion}\"]")}");
                }

                var q = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
                var url = $"{BaseApiUrl}/project/{projectIdOrSlug}/version{q}";

                var versions = await _httpClient.GetFromJsonAsync<List<ModrinthVersion>>(url) ?? new List<ModrinthVersion>();

                // Strict in-memory post-filtering
                if (!string.IsNullOrEmpty(gameVersion) || !string.IsNullOrEmpty(loader))
                {
                    versions = versions.Where(v =>
                    {
                        bool matchesGv = string.IsNullOrEmpty(gameVersion) ||
                                         v.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase);

                        bool matchesLoader = string.IsNullOrEmpty(loader) ||
                                             string.Equals(loader, "Vanilla", StringComparison.OrdinalIgnoreCase) ||
                                             v.Loaders.Contains(loader.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);

                        return matchesGv && matchesLoader;
                    }).ToList();
                }

                return versions;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching project versions: {ex.Message}");
                return new List<ModrinthVersion>();
            }
        }

        public async Task<string> InstallModVersionAsync(ModrinthVersion version, string modsDirectory, CancellationToken ct = default)
        {
            return await InstallModWithDependenciesAsync(version, modsDirectory, null, null, ct);
        }

        public async Task<string> InstallModWithDependenciesAsync(ModrinthVersion version, string modsDirectory, string? gameVersion = null, string? loader = null, CancellationToken ct = default)
        {
            Directory.CreateDirectory(modsDirectory);
            var visitedVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return await InstallVersionInternalAsync(version, modsDirectory, gameVersion, loader, visitedVersions, ct);
        }

        private async Task<string> InstallVersionInternalAsync(ModrinthVersion version, string modsDirectory, string? gameVersion, string? loader, HashSet<string> visitedVersions, CancellationToken ct)
        {
            if (visitedVersions.Contains(version.Id)) return string.Empty;
            visitedVersions.Add(version.Id);

            var primaryFile = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault()
                ?? throw new InvalidOperationException($"В версии {version.Name} отсутствуют файлы для загрузки.");

            var targetPath = Path.Combine(modsDirectory, primaryFile.FileName);
            var tempPath = targetPath + ".download";

            // Remove any older / previous versions of this mod in the mods folder
            RemoveOldModVersions(modsDirectory, primaryFile.FileName);

            if (!File.Exists(targetPath))
            {
                using (var resp = await _httpClient.GetAsync(primaryFile.Url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    using var stream = await resp.Content.ReadAsStreamAsync(ct);
                    using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await stream.CopyToAsync(fs, ct);
                }

                if (File.Exists(targetPath)) File.Delete(targetPath);
                File.Move(tempPath, targetPath);
            }

            // Install required dependencies
            if (version.Dependencies != null && version.Dependencies.Count > 0)
            {
                foreach (var dep in version.Dependencies)
                {
                    if (ct.IsCancellationRequested) break;
                    if (!string.Equals(dep.DependencyType, "required", StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        if (!string.IsNullOrEmpty(dep.VersionId) && !visitedVersions.Contains(dep.VersionId))
                        {
                            var depVerUrl = $"{BaseApiUrl}/version/{dep.VersionId}";
                            var depVer = await _httpClient.GetFromJsonAsync<ModrinthVersion>(depVerUrl, ct);
                            if (depVer != null)
                            {
                                await InstallVersionInternalAsync(depVer, modsDirectory, gameVersion, loader, visitedVersions, ct);
                            }
                        }
                        else if (!string.IsNullOrEmpty(dep.ProjectId))
                        {
                            var targetGv = gameVersion ?? version.GameVersions.FirstOrDefault();
                            var targetLd = loader ?? (version.Loaders.Contains("fabric") ? "fabric" : null);
                            var depVersions = await GetProjectVersionsAsync(dep.ProjectId, targetGv, targetLd);
                            var bestDep = depVersions.FirstOrDefault(v => v.Files.Count > 0);
                            if (bestDep != null && !visitedVersions.Contains(bestDep.Id))
                            {
                                await InstallVersionInternalAsync(bestDep, modsDirectory, targetGv, targetLd, visitedVersions, ct);
                            }
                        }
                    }
                    catch { }
                }
            }

            return targetPath;
        }

        private static void RemoveOldModVersions(string modsDirectory, string newFileName)
        {
            try
            {
                if (!Directory.Exists(modsDirectory)) return;

                var nameWithoutExt = newFileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) 
                    ? newFileName[..^4] 
                    : newFileName;

                var basePrefix = GetModBasePrefix(nameWithoutExt);

                var dir = new DirectoryInfo(modsDirectory);
                foreach (var file in dir.GetFiles())
                {
                    if (string.Equals(file.Name, newFileName, StringComparison.OrdinalIgnoreCase)) continue;

                    if (file.Extension.Equals(".jar", StringComparison.OrdinalIgnoreCase) ||
                        file.Name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        var curName = file.Name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
                            ? file.Name[..^13]
                            : (file.Extension.Equals(".jar", StringComparison.OrdinalIgnoreCase) ? file.Name[..^4] : file.Name);

                        var curPrefix = GetModBasePrefix(curName);

                        if (!string.IsNullOrEmpty(basePrefix) && string.Equals(basePrefix, curPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            try { file.Delete(); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        private static string GetModBasePrefix(string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"^(.*?)(?:[-_+v]|[-_]mc|\d)+[\d\.\+].*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                var p = match.Groups[1].Value.Trim('-', '_', '+');
                if (p.Length >= 2) return p;
            }

            var parts = name.Split('-', '_');
            return parts.Length > 0 ? parts[0] : name;
        }

        public List<InstalledMod> GetInstalledMods(string modsDirectory)
        {
            var list = new List<InstalledMod>();
            if (!Directory.Exists(modsDirectory)) return list;

            var dir = new DirectoryInfo(modsDirectory);
            foreach (var file in dir.GetFiles())
            {
                if (file.Extension.Equals(".jar", StringComparison.OrdinalIgnoreCase) ||
                    file.Name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                {
                    var mod = new InstalledMod
                    {
                        FileName = file.Name,
                        FullPath = file.FullName,
                        FileSize = file.Length,
                        IsEnabled = !file.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                    };

                    // Extract internal metadata and icon from .jar
                    ExtractJarMetadataAndIcon(mod);
                    list.Add(mod);
                }
            }
            return list.OrderBy(m => m.Title).ToList();
        }

        public List<InstalledMod> GetInstalledContent(string profileGameDir)
        {
            var list = new List<InstalledMod>();
            if (!Directory.Exists(profileGameDir)) return list;

            // 1. Mods in mods/
            var modsDir = Path.Combine(profileGameDir, "mods");
            if (Directory.Exists(modsDir))
            {
                foreach (var file in new DirectoryInfo(modsDir).GetFiles())
                {
                    if (file.Extension.Equals(".jar", StringComparison.OrdinalIgnoreCase) ||
                        file.Name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        var mod = new InstalledMod
                        {
                            FileName = file.Name,
                            FullPath = file.FullName,
                            FileSize = file.Length,
                            IsEnabled = !file.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase),
                            ContentType = "mod"
                        };
                        ExtractJarMetadataAndIcon(mod);
                        list.Add(mod);
                    }
                }
            }

            // 2. Resource Packs in resourcepacks/
            ScanZipFolder(Path.Combine(profileGameDir, "resourcepacks"), "resourcepack", list);

            // 3. Shaders in shaderpacks/
            ScanZipFolder(Path.Combine(profileGameDir, "shaderpacks"), "shader", list);

            // 4. Data packs in datapacks/
            ScanZipFolder(Path.Combine(profileGameDir, "datapacks"), "datapack", list);

            return list.OrderBy(m => m.Title).ToList();
        }

        private static void ScanZipFolder(string folderPath, string contentType, List<InstalledMod> list)
        {
            if (!Directory.Exists(folderPath)) return;
            foreach (var file in new DirectoryInfo(folderPath).GetFiles())
            {
                if (file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                    file.Name.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase))
                {
                    var item = new InstalledMod
                    {
                        FileName = file.Name,
                        FullPath = file.FullName,
                        FileSize = file.Length,
                        IsEnabled = !file.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase),
                        ContentType = contentType
                    };
                    ExtractZipMetadataAndIcon(item);
                    list.Add(item);
                }
            }
        }

        private static void ExtractZipMetadataAndIcon(InstalledMod item)
        {
            try
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(item.FullPath);
                var packIconEntry = archive.GetEntry("pack.png") ?? archive.GetEntry("icon.png") ?? archive.GetEntry("logo.png");
                if (packIconEntry != null)
                {
                    using var s = packIconEntry.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    ms.Position = 0;
                    item.IconBitmap = new Avalonia.Media.Imaging.Bitmap(ms);
                }

                var mcmetaEntry = archive.GetEntry("pack.mcmeta");
                if (mcmetaEntry != null)
                {
                    using var s = mcmetaEntry.Open();
                    using var doc = System.Text.Json.JsonDocument.Parse(s);
                    if (doc.RootElement.TryGetProperty("pack", out var packEl))
                    {
                        if (packEl.TryGetProperty("description", out var descEl))
                        {
                            if (descEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                item.Author = descEl.GetString() ?? item.Author;
                            else if (descEl.TryGetProperty("text", out var textEl))
                                item.Author = textEl.GetString() ?? item.Author;
                        }
                    }
                }
            }
            catch { }
        }

        private static void ExtractJarMetadataAndIcon(InstalledMod mod)
        {
            try
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(mod.FullPath);
                
                string? iconEntryPath = null;

                // 1. Try fabric.mod.json
                var fabricEntry = archive.GetEntry("fabric.mod.json");
                if (fabricEntry != null)
                {
                    using var s = fabricEntry.Open();
                    using var doc = System.Text.Json.JsonDocument.Parse(s);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("name", out var nameProp))
                    {
                        mod.Title = nameProp.GetString() ?? mod.Title;
                    }
                    if (root.TryGetProperty("version", out var verProp))
                    {
                        mod.VersionNumber = verProp.GetString() ?? mod.VersionNumber;
                    }
                    if (root.TryGetProperty("authors", out var authProp))
                    {
                        if (authProp.ValueKind == System.Text.Json.JsonValueKind.Array && authProp.GetArrayLength() > 0)
                        {
                            var firstAuth = authProp[0];
                            if (firstAuth.ValueKind == System.Text.Json.JsonValueKind.String)
                                mod.Author = firstAuth.GetString() ?? mod.Author;
                            else if (firstAuth.TryGetProperty("name", out var authName))
                                mod.Author = authName.GetString() ?? mod.Author;
                        }
                    }
                    if (root.TryGetProperty("icon", out var iconProp))
                    {
                        if (iconProp.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            iconEntryPath = iconProp.GetString();
                        }
                        else if (iconProp.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            foreach (var p in iconProp.EnumerateObject())
                            {
                                iconEntryPath = p.Value.GetString();
                                break;
                            }
                        }
                    }
                }

                // 2. Try mods.toml (Forge/NeoForge)
                if (string.IsNullOrEmpty(mod.Title) || mod.Title == mod.DisplayName)
                {
                    var tomlEntry = archive.GetEntry("META-INF/mods.toml");
                    if (tomlEntry != null)
                    {
                        using var reader = new StreamReader(tomlEntry.Open());
                        var content = reader.ReadToEnd();
                        var nameMatch = System.Text.RegularExpressions.Regex.Match(content, @"displayName\s*=\s*""([^""]+)""");
                        if (nameMatch.Success) mod.Title = nameMatch.Groups[1].Value;
                        var verMatch = System.Text.RegularExpressions.Regex.Match(content, @"version\s*=\s*""([^""]+)""");
                        if (verMatch.Success) mod.VersionNumber = verMatch.Groups[1].Value;
                        var logoMatch = System.Text.RegularExpressions.Regex.Match(content, @"logoFile\s*=\s*""([^""]+)""");
                        if (logoMatch.Success) iconEntryPath = logoMatch.Groups[1].Value;
                    }
                }

                // 3. Find and extract icon entry
                System.IO.Compression.ZipArchiveEntry? targetIconEntry = null;
                if (!string.IsNullOrEmpty(iconEntryPath))
                {
                    iconEntryPath = iconEntryPath.TrimStart('/');
                    targetIconEntry = archive.GetEntry(iconEntryPath);
                }

                if (targetIconEntry == null)
                {
                    targetIconEntry = archive.Entries.FirstOrDefault(e => 
                        e.FullName.Equals("icon.png", StringComparison.OrdinalIgnoreCase) ||
                        e.FullName.EndsWith("/icon.png", StringComparison.OrdinalIgnoreCase) ||
                        e.FullName.Equals("logo.png", StringComparison.OrdinalIgnoreCase));
                }

                if (targetIconEntry != null)
                {
                    using var iconStream = targetIconEntry.Open();
                    using var ms = new MemoryStream();
                    iconStream.CopyTo(ms);
                    ms.Position = 0;
                    var bmp = new Avalonia.Media.Imaging.Bitmap(ms);
                    mod.IconBitmap = bmp;
                }
            }
            catch { }
        }

        public async Task EnrichInstalledModsAsync(IEnumerable<InstalledMod> mods, CancellationToken ct = default)
        {
            foreach (var mod in mods)
            {
                if (ct.IsCancellationRequested) break;
                if (mod.ProjectHit != null && mod.IconBitmap != null) continue;

                try
                {
                    // Clean file name to query Modrinth
                    var name = mod.DisplayName;
                    var cleanQuery = System.Text.RegularExpressions.Regex.Replace(name, @"[-_](fabric|forge|neoforge|quilt|mc[0-9.]+|v?[0-9.]+.*)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                    if (string.IsNullOrWhiteSpace(cleanQuery)) cleanQuery = name;

                    var searchRes = await SearchModsAsync(cleanQuery, null, null, limit: 3);
                    var matchedHit = searchRes.Hits.FirstOrDefault(h => 
                        cleanQuery.Contains(h.Slug, StringComparison.OrdinalIgnoreCase) || 
                        h.Slug.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase) ||
                        cleanQuery.Contains(h.Title, StringComparison.OrdinalIgnoreCase) ||
                        h.Title.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase)) ?? searchRes.Hits.FirstOrDefault();

                    if (matchedHit != null)
                    {
                        if (string.IsNullOrEmpty(mod.Title) || mod.Title == mod.DisplayName)
                        {
                            mod.Title = matchedHit.Title;
                        }
                        if (string.IsNullOrEmpty(mod.Author))
                        {
                            mod.Author = matchedHit.Author;
                        }
                        mod.IconUrl = matchedHit.IconUrl;
                        mod.ProjectId = matchedHit.ProjectId;
                        mod.Slug = matchedHit.Slug;
                        mod.ProjectHit = matchedHit;

                        if (mod.IconBitmap == null && !string.IsNullOrEmpty(matchedHit.IconUrl))
                        {
                            var bmp = await _imageCacheService.LoadImageAsync(matchedHit.IconUrl);
                            if (bmp != null)
                            {
                                Avalonia.Threading.Dispatcher.UIThread.Post(() => mod.IconBitmap = bmp);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        public bool ToggleMod(string fullPath)
        {
            if (!File.Exists(fullPath)) return false;

            try
            {
                if (fullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    var newPath = fullPath[..^9];
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(fullPath, newPath);
                    return true;
                }
                else
                {
                    var newPath = fullPath + ".disabled";
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(fullPath, newPath);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public async Task<Dictionary<string, ModrinthVersion>> CheckModUpdatesAsync(IEnumerable<InstalledMod> mods, string gameVersion, string? loader, CancellationToken ct = default)
        {
            var updateMap = new Dictionary<string, ModrinthVersion>(StringComparer.OrdinalIgnoreCase);
            var modModList = mods.ToList();
            if (modModList.Count == 0) return updateMap;

            // 1. Calculate SHA-512 hashes for mods
            var validMods = new List<InstalledMod>();
            foreach (var mod in modModList)
            {
                if (ct.IsCancellationRequested) break;
                if (!File.Exists(mod.FullPath)) continue;

                if (string.IsNullOrEmpty(mod.Sha512) || string.IsNullOrEmpty(mod.Sha1))
                {
                    try
                    {
                        var (sha1, sha512) = ComputeFileHashes(mod.FullPath);
                        mod.Sha1 = sha1;
                        mod.Sha512 = sha512;
                    }
                    catch { continue; }
                }

                if (!string.IsNullOrEmpty(mod.Sha512))
                {
                    validMods.Add(mod);
                }
            }

            if (validMods.Count == 0) return updateMap;

            try
            {
                var payload = new
                {
                    hashes = validMods.Select(m => m.Sha512!).Distinct().ToList(),
                    algorithm = "sha512",
                    loaders = !string.IsNullOrEmpty(loader) && !string.Equals(loader, "Vanilla", StringComparison.OrdinalIgnoreCase)
                        ? new[] { loader.ToLowerInvariant() }
                        : Array.Empty<string>(),
                    game_versions = !string.IsNullOrEmpty(gameVersion)
                        ? new[] { gameVersion }
                        : Array.Empty<string>()
                };

                var response = await _httpClient.PostAsJsonAsync($"{BaseApiUrl}/version_files/update", payload, ct);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<Dictionary<string, ModrinthVersion>>(cancellationToken: ct);
                    if (result != null)
                    {
                        foreach (var kvp in result)
                        {
                            updateMap[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking mod updates: {ex.Message}");
            }

            // Map results back to InstalledMod items
            foreach (var mod in validMods)
            {
                if (!string.IsNullOrEmpty(mod.Sha512) && updateMap.TryGetValue(mod.Sha512, out var updateVer))
                {
                    // Check if updateVer is the exact same version/file
                    var isSameFile = updateVer.Files.Any(f =>
                        string.Equals(f.Hashes?.Sha512, mod.Sha512, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(f.Hashes?.Sha1, mod.Sha1, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(f.FileName, mod.FileName, StringComparison.OrdinalIgnoreCase));

                    if (!isSameFile && !string.Equals(updateVer.VersionNumber, mod.VersionNumber, StringComparison.OrdinalIgnoreCase))
                    {
                        mod.HasUpdate = true;
                        mod.UpdateVersion = updateVer;
                    }
                    else
                    {
                        mod.HasUpdate = false;
                        mod.UpdateVersion = null;
                    }
                }
                else
                {
                    mod.HasUpdate = false;
                    mod.UpdateVersion = null;
                }
            }

            return updateMap;
        }

        public async Task<string> UpdateModAsync(InstalledMod mod, ModrinthVersion newVersion, string modsDirectory, string? gameVersion = null, string? loader = null, CancellationToken ct = default)
        {
            mod.IsUpdating = true;
            try
            {
                var oldPath = mod.FullPath;
                var newPath = await InstallModWithDependenciesAsync(newVersion, modsDirectory, gameVersion, loader, ct);

                if (!string.IsNullOrEmpty(newPath) && File.Exists(newPath))
                {
                    if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
                    {
                        try { File.Delete(oldPath); } catch { }
                    }

                    mod.FullPath = newPath;
                    mod.FileName = Path.GetFileName(newPath);
                    mod.VersionNumber = newVersion.VersionNumber;
                    mod.HasUpdate = false;
                    mod.UpdateVersion = null;

                    try
                    {
                        var (sha1, sha512) = ComputeFileHashes(newPath);
                        mod.Sha1 = sha1;
                        mod.Sha512 = sha512;
                    }
                    catch { }

                    return newPath;
                }
                return string.Empty;
            }
            finally
            {
                mod.IsUpdating = false;
            }
        }

        public async Task<int> UpdateAllModsAsync(IEnumerable<InstalledMod> modsWithUpdates, string modsDirectory, string gameVersion, string? loader, IProgress<(int current, int total, string modName)>? progress = null, CancellationToken ct = default)
        {
            var updateList = modsWithUpdates.Where(m => m.HasUpdate && m.UpdateVersion != null).ToList();
            int updatedCount = 0;
            int total = updateList.Count;

            for (int i = 0; i < total; i++)
            {
                if (ct.IsCancellationRequested) break;
                var mod = updateList[i];
                progress?.Report((i + 1, total, mod.Title));

                try
                {
                    var res = await UpdateModAsync(mod, mod.UpdateVersion!, modsDirectory, gameVersion, loader, ct);
                    if (!string.IsNullOrEmpty(res))
                    {
                        updatedCount++;
                    }
                }
                catch { }
            }

            return updatedCount;
        }

        public static (string sha1, string sha512) ComputeFileHashes(string filePath)
        {
            using var fs = File.OpenRead(filePath);
            using var sha1Alg = System.Security.Cryptography.SHA1.Create();
            using var sha512Alg = System.Security.Cryptography.SHA512.Create();

            var sha1Bytes = sha1Alg.ComputeHash(fs);
            fs.Position = 0;
            var sha512Bytes = sha512Alg.ComputeHash(fs);

            return (
                Convert.ToHexString(sha1Bytes).ToLowerInvariant(),
                Convert.ToHexString(sha512Bytes).ToLowerInvariant()
            );
        }

        public bool DeleteMod(string fullPath)
        {
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
