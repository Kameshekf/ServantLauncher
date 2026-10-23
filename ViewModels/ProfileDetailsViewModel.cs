using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using ServantLauncher.Models;
using ServantLauncher.Services;

namespace ServantLauncher.ViewModels
{
    public class WorldItem : ObservableObject
    {
        public string Name { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public DateTime LastPlayed { get; set; }
        public string FormattedLastPlayed => LastPlayed.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        public string FormattedSize { get; set; } = string.Empty;
        public Avalonia.Media.Imaging.Bitmap? IconBitmap { get; set; }
        public bool HasIconBitmap => IconBitmap != null;
    }

    public partial class ProfileDetailsViewModel : ObservableObject
    {
        private readonly IProfileManager _profileManager;
        private readonly IModrinthService _modrinthService;
        private readonly IImageCacheService _imageCacheService;
        private readonly IGameLauncherService _launcherService;
        private readonly IModpackImportService _modpackImportService;
        private CancellationTokenSource? _searchDebounceCts;
        private CancellationTokenSource? _resourceSearchDebounceCts;

        [ObservableProperty]
        private GameProfile _profile;

        [ObservableProperty]
        private int _selectedTabIndex = 0; // 0 = Контент, 1 = Файлы, 2 = Миры, 3 = Скриншоты, 4 = Журнал, 5 = Настройки

        // Content Sub-Tab & Filters (media_1789669080903.png)
        [ObservableProperty]
        private bool _isSearchingResources;

        [ObservableProperty]
        private string _filterContentType = "all"; // all, mod, resourcepack, shader, datapack

        partial void OnFilterContentTypeChanged(string value) => ApplyContentFilters();

        [ObservableProperty]
        private string _searchContentQuery = string.Empty;

        partial void OnSearchContentQueryChanged(string value) => ApplyContentFilters();

        [ObservableProperty]
        private int _contentSortIndex = 0; // 0 = Название (А-Я), 1 = Название (Я-А), 2 = Сначала включенные

        partial void OnContentSortIndexChanged(int value) => ApplyContentFilters();

        // Modrinth Resource Finder (media_1789669192988.png)
        [ObservableProperty]
        private string _resourceFinderType = "mod"; // mod, resourcepack, datapack, shader

        partial void OnResourceFinderTypeChanged(string value)
        {
            ResourceCurrentPage = 1;
            _ = SearchResourcesAsync();
        }

        [ObservableProperty]
        private string _resourceFinderQuery = string.Empty;

        partial void OnResourceFinderQueryChanged(string value)
        {
            ResourceCurrentPage = 1;
            DebounceResourceSearch();
        }

        [ObservableProperty]
        private bool _isResourceFinderLoading;

        [ObservableProperty]
        private int _resourceCurrentPage = 1;

        [ObservableProperty]
        private int _resourceTotalHits;

        [ObservableProperty]
        private int _resourceTotalPages = 1;

        [ObservableProperty]
        private bool _canGoPrevResourcePage;

        [ObservableProperty]
        private bool _canGoNextResourcePage;

        [ObservableProperty]
        private string _resourcePaginationInfo = "Страница 1";

        [ObservableProperty]
        private bool _hasMultipleResourcePages;

        public ObservableCollection<WorldItem> Worlds { get; } = new();
        public ObservableCollection<InstalledMod> FilteredInstalledMods { get; } = new();
        public ObservableCollection<ModrinthProjectHit> ResourceSearchResults { get; } = new();
        public ScreenshotsViewModel ProfileScreenshotsVm { get; }

        [ObservableProperty]
        private string _searchModQuery = string.Empty;

        [ObservableProperty]
        private string _selectedCategory = "Все";

        [ObservableProperty]
        private bool _isSearchingMods;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _isCheckingUpdates;

        [ObservableProperty]
        private bool _isUpdatingAll;

        [ObservableProperty]
        private int _availableUpdatesCount;

        [ObservableProperty]
        private string _updatesStatusText = string.Empty;

        [ObservableProperty]
        private bool _isExportingMrpack;

        [ObservableProperty]
        private string _exportMrpackStatus = string.Empty;

        [ObservableProperty]
        private ModDetailsViewModel? _activeModDetailsVm;

        [ObservableProperty]
        private bool _isModDetailsOpen;

        [ObservableProperty]
        private ImageCropViewModel? _cropDialogVm;

        [ObservableProperty]
        private bool _isCropDialogOpen;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PlayButtonText))]
        [NotifyPropertyChangedFor(nameof(PlayButtonIcon))]
        [NotifyPropertyChangedFor(nameof(IsDangerButton))]
        [NotifyPropertyChangedFor(nameof(ShowProgressBar))]
        private bool _isLaunching;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PlayButtonText))]
        [NotifyPropertyChangedFor(nameof(PlayButtonIcon))]
        [NotifyPropertyChangedFor(nameof(IsDangerButton))]
        [NotifyPropertyChangedFor(nameof(ShowProgressBar))]
        private bool _isGameRunning;

        [ObservableProperty]
        private double _launchProgressPercent;

        [ObservableProperty]
        private string _launchStatusText = string.Empty;

        public bool IsProfileInstalled => _profileManager.IsProfileInstalled(Profile);

        public bool IsDangerButton => IsGameRunning || IsLaunching;
        public bool ShowProgressBar => IsLaunching && !IsGameRunning;

        public string PlayButtonText
        {
            get
            {
                if (IsGameRunning) return "ЗАКРЫТЬ ИГРУ";
                if (IsLaunching) return "ОТМЕНА";
                return IsProfileInstalled ? "ИГРАТЬ" : "УСТАНОВИТЬ";
            }
        }

        public string PlayButtonIcon
        {
            get
            {
                if (IsGameRunning || IsLaunching) return "IconX";
                return IsProfileInstalled ? "IconPlay" : "IconDownload";
            }
        }

        public ConsoleViewModel ConsoleVm { get; }

        public ObservableCollection<InstalledMod> InstalledMods { get; } = new();
        public ObservableCollection<ModrinthProjectHit> ModSearchResults { get; } = new();
        public ObservableCollection<string> Categories { get; } = new()
        {
            "Все", "Optimization", "Utility", "Decoration", "Voice Chat", "Technology", "Worldgen"
        };

        public event Action? BackRequested;
        public event Action? PlayRequested;
        public event Action? CancelLaunchRequested;
        public event Action? CloseGameRequested;
        public event Action<GameProfile>? DuplicateRequested;
        public event Action<GameProfile>? DeleteRequested;

        public bool CanDelete => !Profile.IsServerProfile;
        public bool IsServerProfile => Profile.IsServerProfile;
        public string PlayTimeDisplay => Profile.PlayTimeText;

        public ProfileDetailsViewModel(
            GameProfile profile,
            IProfileManager profileManager,
            IModrinthService modrinthService,
            IImageCacheService imageCacheService,
            IGameLauncherService? launcherService = null,
            IModpackImportService? modpackImportService = null)
        {
            _profile = profile;
            _profileManager = profileManager;
            _modrinthService = modrinthService;
            _imageCacheService = imageCacheService;
            _launcherService = launcherService ?? new GameLauncherService(new MojangService(), new FabricService(), new JavaService(), profileManager);
            _modpackImportService = modpackImportService ?? new ModpackImportService(profileManager);

            ConsoleVm = new ConsoleViewModel(_launcherService);
            ProfileScreenshotsVm = new ScreenshotsViewModel(new ScreenshotService(_profileManager), _profileManager, Profile);

            RefreshInstalledMods();
            _ = TriggerLiveSearchAsync();
        }

        [RelayCommand]
        public async Task CheckModUpdatesAsync()
        {
            if (IsCheckingUpdates || InstalledMods.Count == 0) return;

            IsCheckingUpdates = true;
            UpdatesStatusText = "Проверка обновлений модов через Modrinth...";

            try
            {
                var loader = Profile.LoaderType == LoaderType.Fabric ? "fabric" : null;
                await _modrinthService.CheckModUpdatesAsync(InstalledMods, Profile.GameVersion, loader);

                AvailableUpdatesCount = InstalledMods.Count(m => m.HasUpdate);
                UpdatesStatusText = AvailableUpdatesCount > 0
                    ? $"Доступно обновлений: {AvailableUpdatesCount}"
                    : "Все моды актуальны!";
            }
            catch (Exception ex)
            {
                UpdatesStatusText = $"Ошибка проверки: {ex.Message}";
            }
            finally
            {
                IsCheckingUpdates = false;
            }
        }

        [RelayCommand]
        public async Task UpdateSingleModAsync(InstalledMod? mod)
        {
            if (mod == null || !mod.HasUpdate || mod.UpdateVersion == null) return;

            UpdatesStatusText = $"Обновление {mod.Title}...";
            try
            {
                var modsDir = _profileManager.GetProfileModsDir(Profile);
                var loader = Profile.LoaderType == LoaderType.Fabric ? "fabric" : null;
                var res = await _modrinthService.UpdateModAsync(mod, mod.UpdateVersion, modsDir, Profile.GameVersion, loader);
                if (!string.IsNullOrEmpty(res))
                {
                    AvailableUpdatesCount = InstalledMods.Count(m => m.HasUpdate);
                    UpdatesStatusText = $"Мод {mod.Title} успешно обновлен до {mod.VersionNumber}!";
                }
            }
            catch (Exception ex)
            {
                UpdatesStatusText = $"Ошибка обновления {mod.Title}: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task UpdateAllModsAsync()
        {
            if (IsUpdatingAll || AvailableUpdatesCount == 0) return;

            IsUpdatingAll = true;
            try
            {
                var modsDir = _profileManager.GetProfileModsDir(Profile);
                var loader = Profile.LoaderType == LoaderType.Fabric ? "fabric" : null;

                var progress = new Progress<(int current, int total, string modName)>(p =>
                {
                    UpdatesStatusText = $"Обновление ({p.current}/{p.total}): {p.modName}...";
                });

                var updated = await _modrinthService.UpdateAllModsAsync(InstalledMods, modsDir, Profile.GameVersion, loader, progress);
                AvailableUpdatesCount = InstalledMods.Count(m => m.HasUpdate);
                UpdatesStatusText = $"Успешно обновлено модов: {updated}!";
                RefreshInstalledMods();
            }
            catch (Exception ex)
            {
                UpdatesStatusText = $"Ошибка при обновлении: {ex.Message}";
            }
            finally
            {
                IsUpdatingAll = false;
            }
        }

        [RelayCommand]
        public async Task ExportMrpackAsync()
        {
            if (IsExportingMrpack) return;

            IsExportingMrpack = true;
            ExportMrpackStatus = "Подготовка экспорта .mrpack...";

            try
            {
                var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloads))
                {
                    downloads = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }

                var cleanName = Regex.Replace(Profile.Name, @"[^\w\-_\.]+", "-").Trim('-');
                var exportFile = Path.Combine(downloads, $"{cleanName}-{Profile.GameVersion}.mrpack");

                var progress = new Progress<(double percent, string status)>(p =>
                {
                    ExportMrpackStatus = p.status;
                });

                await _modpackImportService.ExportMrpackAsync(Profile, exportFile, progress);
                ExportMrpackStatus = $"Сохранено в Загрузки: {Path.GetFileName(exportFile)}";

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{exportFile}\"",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
            catch (Exception ex)
            {
                ExportMrpackStatus = $"Ошибка экспорта: {ex.Message}";
            }
            finally
            {
                IsExportingMrpack = false;
            }
        }

        public async Task ExportMrpackCustomPathAsync(string targetFilePath)
        {
            if (IsExportingMrpack || string.IsNullOrEmpty(targetFilePath)) return;

            IsExportingMrpack = true;
            ExportMrpackStatus = "Экспорт модпака .mrpack...";

            try
            {
                var progress = new Progress<(double percent, string status)>(p =>
                {
                    ExportMrpackStatus = p.status;
                });

                await _modpackImportService.ExportMrpackAsync(Profile, targetFilePath, progress);
                ExportMrpackStatus = $"Успешно экспортировано: {Path.GetFileName(targetFilePath)}";

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{targetFilePath}\"",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
            catch (Exception ex)
            {
                ExportMrpackStatus = $"Ошибка экспорта: {ex.Message}";
            }
            finally
            {
                IsExportingMrpack = false;
            }
        }

        partial void OnSearchModQueryChanged(string value)
        {
            DebounceSearch();
        }

        partial void OnSelectedCategoryChanged(string value)
        {
            DebounceSearch();
        }

        private void DebounceSearch()
        {
            _searchDebounceCts?.Cancel();
            _searchDebounceCts = new CancellationTokenSource();
            var token = _searchDebounceCts.Token;

            Task.Delay(350, token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    Dispatcher.UIThread.Post(() => _ = TriggerLiveSearchAsync());
                }
            }, TaskScheduler.Default);
        }

        public void RefreshInstalledMods()
        {
            InstalledMods.Clear();
            var profileDir = _profileManager.GetProfileGameDir(Profile);
            var list = _modrinthService.GetInstalledContent(profileDir);
            foreach (var m in list)
            {
                InstalledMods.Add(m);
            }
            ApplyContentFilters();
            StatusMessage = $"В сборке установлено {InstalledMods.Count} компонентов.";
            UpdateInstalledStatusInSearchResults();

            _ = Task.Run(async () =>
            {
                try
                {
                    await _modrinthService.EnrichInstalledModsAsync(list);
                    Dispatcher.UIThread.Post(() =>
                    {
                        ApplyContentFilters();
                        UpdateInstalledStatusInSearchResults();
                    });
                }
                catch { }
            });
        }

        public void ApplyContentFilters()
        {
            var query = InstalledMods.AsEnumerable();

            if (!string.IsNullOrEmpty(FilterContentType) && !string.Equals(FilterContentType, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(m => string.Equals(m.ContentType, FilterContentType, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(SearchContentQuery))
            {
                var q = SearchContentQuery.Trim();
                query = query.Where(m => m.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                         m.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                         m.Author.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            query = ContentSortIndex switch
            {
                1 => query.OrderByDescending(m => m.Title),
                2 => query.OrderByDescending(m => m.IsEnabled).ThenBy(m => m.Title),
                _ => query.OrderBy(m => m.Title)
            };

            FilteredInstalledMods.Clear();
            foreach (var item in query)
            {
                FilteredInstalledMods.Add(item);
            }
        }

        [RelayCommand]
        public void RefreshWorlds()
        {
            Worlds.Clear();
            try
            {
                var savesDir = Path.Combine(_profileManager.GetProfileGameDir(Profile), "saves");
                if (!Directory.Exists(savesDir)) return;

                foreach (var dir in new DirectoryInfo(savesDir).GetDirectories())
                {
                    var item = new WorldItem
                    {
                        Name = dir.Name,
                        FolderName = dir.Name,
                        FullPath = dir.FullName,
                        LastPlayed = dir.LastWriteTimeUtc,
                        FormattedSize = FormatDirSize(dir)
                    };

                    var iconPath = Path.Combine(dir.FullName, "icon.png");
                    if (File.Exists(iconPath))
                    {
                        try { item.IconBitmap = new Avalonia.Media.Imaging.Bitmap(iconPath); } catch { }
                    }
                    Worlds.Add(item);
                }
            }
            catch { }
        }

        private static string FormatDirSize(DirectoryInfo dir)
        {
            try
            {
                long size = dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                if (size < 1024) return $"{size} Б";
                if (size < 1024 * 1024) return $"{size / 1024.0:F1} КБ";
                return $"{size / (1024.0 * 1024.0):F1} МБ";
            }
            catch
            {
                return "";
            }
        }

        [RelayCommand]
        public void ShowFindResources()
        {
            IsSearchingResources = true;
            if (ResourceSearchResults.Count == 0)
            {
                _ = SearchResourcesAsync();
            }
        }

        [RelayCommand]
        public void ShowContentList()
        {
            IsSearchingResources = false;
            RefreshInstalledMods();
        }

        [RelayCommand]
        public void SetFilterContentType(string type)
        {
            FilterContentType = type;
        }

        [RelayCommand]
        public void SetResourceFinderType(string type)
        {
            ResourceFinderType = type;
        }

        private void DebounceResourceSearch()
        {
            _resourceSearchDebounceCts?.Cancel();
            _resourceSearchDebounceCts = new CancellationTokenSource();
            var token = _resourceSearchDebounceCts.Token;

            Task.Delay(350, token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    Dispatcher.UIThread.Post(() => _ = SearchResourcesAsync());
                }
            }, TaskScheduler.Default);
        }

        public async Task SearchResourcesAsync()
        {
            if (IsResourceFinderLoading) return;
            IsResourceFinderLoading = true;
            try
            {
                const int pageSize = 24;
                int offset = Math.Max(0, (ResourceCurrentPage - 1) * pageSize);
                var loader = (Profile.LoaderType == LoaderType.Fabric && ResourceFinderType == "mod") ? "fabric" : null;
                var res = await _modrinthService.SearchModsAsync(ResourceFinderQuery, Profile.GameVersion, loader, limit: pageSize, offset: offset, projectType: ResourceFinderType);

                ResourceSearchResults.Clear();
                var installedNames = InstalledMods.Select(m => m.Title.ToLowerInvariant()).ToHashSet();
                var installedSlugs = InstalledMods.Select(m => m.Slug?.ToLowerInvariant()).Where(s => !string.IsNullOrEmpty(s)).ToHashSet();

                foreach (var hit in res.Hits)
                {
                    hit.IsInstalled = installedNames.Contains(hit.Title.ToLowerInvariant()) ||
                                      (!string.IsNullOrEmpty(hit.Slug) && installedSlugs.Contains(hit.Slug.ToLowerInvariant()));
                    ResourceSearchResults.Add(hit);

                    if (!string.IsNullOrEmpty(hit.IconUrl) && hit.IconBitmap == null)
                    {
                        _ = Task.Run(async () =>
                        {
                            var bmp = await _imageCacheService.LoadImageAsync(hit.IconUrl);
                            if (bmp != null)
                            {
                                Dispatcher.UIThread.Post(() => hit.IconBitmap = bmp);
                            }
                        });
                    }
                }

                ResourceTotalHits = res.TotalHits;
                ResourceTotalPages = Math.Max(1, (int)Math.Ceiling((double)res.TotalHits / pageSize));
                CanGoPrevResourcePage = ResourceCurrentPage > 1;
                CanGoNextResourcePage = ResourceCurrentPage < ResourceTotalPages;
                HasMultipleResourcePages = ResourceTotalPages > 1;
                ResourcePaginationInfo = $"Страница {ResourceCurrentPage} из {ResourceTotalPages}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка поиска: {ex.Message}";
            }
            finally
            {
                IsResourceFinderLoading = false;
            }
        }

        [RelayCommand]
        public async Task NextResourcePageAsync()
        {
            if (!CanGoNextResourcePage || IsResourceFinderLoading) return;
            ResourceCurrentPage++;
            await SearchResourcesAsync();
        }

        [RelayCommand]
        public async Task PrevResourcePageAsync()
        {
            if (!CanGoPrevResourcePage || IsResourceFinderLoading) return;
            ResourceCurrentPage--;
            await SearchResourcesAsync();
        }

        [RelayCommand]
        public async Task QuickInstallResourceAsync(ModrinthProjectHit? hit)
        {
            if (hit == null) return;

            StatusMessage = $"Загрузка {hit.Title}...";
            try
            {
                var loader = (Profile.LoaderType == LoaderType.Fabric && hit.ProjectType == "mod") ? "fabric" : null;
                var versions = await _modrinthService.GetProjectVersionsAsync(hit.ProjectId, Profile.GameVersion, loader);
                var targetVer = versions.FirstOrDefault(v => v.Files.Count > 0)
                    ?? throw new InvalidOperationException("Нет подходящей версии для текущей сборки.");

                var profileDir = _profileManager.GetProfileGameDir(Profile);
                var targetDir = hit.ProjectType switch
                {
                    "resourcepack" => Path.Combine(profileDir, "resourcepacks"),
                    "shader" => Path.Combine(profileDir, "shaderpacks"),
                    "datapack" => Path.Combine(profileDir, "datapacks"),
                    _ => _profileManager.GetProfileModsDir(Profile)
                };

                Directory.CreateDirectory(targetDir);
                await _modrinthService.InstallModWithDependenciesAsync(targetVer, targetDir, Profile.GameVersion, loader);
                hit.IsInstalled = true;
                StatusMessage = $"{hit.Title} успешно установлен в сборку!";
                RefreshInstalledMods();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка установки: {ex.Message}";
            }
        }

        public void AddFiles(IEnumerable<string> paths)
        {
            var profileDir = _profileManager.GetProfileGameDir(Profile);
            int added = 0;

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        var dirName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        var jars = Directory.GetFiles(path, "*.jar", SearchOption.AllDirectories);
                        if (jars.Length > 0 && !File.Exists(Path.Combine(path, "pack.mcmeta")))
                        {
                            var modsDir = Path.Combine(profileDir, "mods");
                            Directory.CreateDirectory(modsDir);
                            foreach (var jar in jars)
                            {
                                var jarName = Path.GetFileName(jar);
                                File.Copy(jar, Path.Combine(modsDir, jarName), true);
                                added++;
                            }
                            continue;
                        }

                        if (File.Exists(Path.Combine(path, "pack.mcmeta")))
                        {
                            var targetDir = Path.Combine(profileDir, "resourcepacks", dirName);
                            CopyDirectoryRecursively(path, targetDir);
                            added++;
                            continue;
                        }

                        if (Directory.Exists(Path.Combine(path, "shaders")) || Directory.GetFiles(path, "*.vsh", SearchOption.AllDirectories).Length > 0)
                        {
                            var targetDir = Path.Combine(profileDir, "shaderpacks", dirName);
                            CopyDirectoryRecursively(path, targetDir);
                            added++;
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error importing dropped directory {path}: {ex.Message}");
                    }
                    continue;
                }

                if (!File.Exists(path)) continue;
                var fileName = Path.GetFileName(path);
                string targetFolder;

                if (fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                {
                    targetFolder = Path.Combine(profileDir, "mods");
                }
                else if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    targetFolder = Path.Combine(profileDir, "resourcepacks");
                    try
                    {
                        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
                        if (zip.Entries.Any(e => e.FullName.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".vsh", StringComparison.OrdinalIgnoreCase)))
                        {
                            targetFolder = Path.Combine(profileDir, "shaderpacks");
                        }
                        else if (zip.Entries.Any(e => e.FullName.StartsWith("data/", StringComparison.OrdinalIgnoreCase)))
                        {
                            targetFolder = Path.Combine(profileDir, "datapacks");
                        }
                    }
                    catch { }
                }
                else
                {
                    continue;
                }

                Directory.CreateDirectory(targetFolder);
                var destFile = Path.Combine(targetFolder, fileName);
                File.Copy(path, destFile, true);
                added++;
            }

            if (added > 0)
            {
                StatusMessage = $"Добавлено файлов/пакетов: {added}";
                RefreshInstalledMods();
            }
        }

        private static void CopyDirectoryRecursively(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
            }
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectoryRecursively(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
            }
        }

        private void UpdateInstalledStatusInSearchResults()
        {
            var installedIds = InstalledMods.Select(m => m.ProjectId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var installedSlugs = InstalledMods.Select(m => m.Slug).Where(s => !string.IsNullOrEmpty(s)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var installedNames = InstalledMods.Select(m => m.Title.ToLowerInvariant()).ToHashSet();

            foreach (var hit in ModSearchResults)
            {
                hit.IsInstalled = (!string.IsNullOrEmpty(hit.ProjectId) && installedIds.Contains(hit.ProjectId)) ||
                                  (!string.IsNullOrEmpty(hit.Slug) && installedSlugs.Contains(hit.Slug)) ||
                                  installedNames.Contains(hit.Title.ToLowerInvariant()) ||
                                  installedNames.Contains(hit.Slug.ToLowerInvariant());
            }

            foreach (var hit in ResourceSearchResults)
            {
                hit.IsInstalled = (!string.IsNullOrEmpty(hit.ProjectId) && installedIds.Contains(hit.ProjectId)) ||
                                  (!string.IsNullOrEmpty(hit.Slug) && installedSlugs.Contains(hit.Slug)) ||
                                  installedNames.Contains(hit.Title.ToLowerInvariant()) ||
                                  installedNames.Contains(hit.Slug.ToLowerInvariant());
            }
        }

        partial void OnSelectedTabIndexChanged(int value)
        {
            if (value == 0) RefreshInstalledMods();
            else if (value == 1) { /* Files */ }
            else if (value == 2) RefreshWorlds();
            else if (value == 3) _ = ProfileScreenshotsVm.LoadScreenshotsAsync();
        }

        [RelayCommand]
        public async Task ChangeModVersionAsync(InstalledMod? mod)
        {
            if (mod == null) return;

            if (mod.ProjectHit != null)
            {
                OpenModDetails(mod.ProjectHit);
                if (ActiveModDetailsVm != null)
                {
                    ActiveModDetailsVm.SelectedTabIndex = 1; // Jump straight to "Версии"
                }
                return;
            }

            if (!string.IsNullOrEmpty(mod.ProjectId) || !string.IsNullOrEmpty(mod.Slug))
            {
                var projId = !string.IsNullOrEmpty(mod.ProjectId) ? mod.ProjectId : mod.Slug;
                if (!string.IsNullOrEmpty(projId))
                {
                    var proj = await _modrinthService.GetProjectDetailsAsync(projId);
                    if (proj != null)
                    {
                        var hit = new ModrinthProjectHit
                        {
                            ProjectId = proj.Id,
                            Slug = proj.Slug,
                            Title = proj.Title,
                            Description = proj.Description,
                            Body = proj.Body,
                            Categories = proj.Categories,
                            IconUrl = proj.IconUrl,
                            Downloads = proj.Downloads
                        };
                        OpenModDetails(hit);
                        if (ActiveModDetailsVm != null)
                        {
                            ActiveModDetailsVm.SelectedTabIndex = 1; // Jump straight to "Версии"
                        }
                        return;
                    }
                }
            }

            // Search by name
            SelectedTabIndex = 1;
            SearchModQuery = mod.Title;
        }

        [RelayCommand]
        public void OpenInstalledModDetails(InstalledMod? mod)
        {
            if (mod == null) return;
            _ = ChangeModVersionAsync(mod);
        }

        [RelayCommand]
        public void SelectCategory(string category)
        {
            SelectedCategory = category;
        }

        [RelayCommand]
        public async Task TriggerLiveSearchAsync()
        {
            IsSearchingMods = true;
            StatusMessage = $"Поиск модов для {Profile.GameVersion} ({Profile.LoaderType})...";

            try
            {
                var loader = Profile.LoaderType == LoaderType.Fabric ? "fabric" : null;
                var query = SearchModQuery?.Trim() ?? "";
                if (SelectedCategory != "Все")
                {
                    query = string.IsNullOrEmpty(query) ? SelectedCategory : $"{query} {SelectedCategory}";
                }

                var res = await _modrinthService.SearchModsAsync(query, Profile.GameVersion, loader, limit: 30);
                
                ModSearchResults.Clear();
                foreach (var hit in res.Hits)
                {
                    ModSearchResults.Add(hit);
                    if (!string.IsNullOrEmpty(hit.IconUrl))
                    {
                        _ = Task.Run(async () =>
                        {
                            var bmp = await _imageCacheService.LoadImageAsync(hit.IconUrl);
                            Dispatcher.UIThread.Post(() => hit.IconBitmap = bmp);
                        });
                    }
                }

                UpdateInstalledStatusInSearchResults();

                StatusMessage = string.IsNullOrEmpty(query)
                    ? $"Популярные моды ({res.TotalHits})"
                    : $"Найдено {res.TotalHits} модов по запросу '{query}'";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsSearchingMods = false;
            }
        }

        [RelayCommand]
        public void OpenModDetails(ModrinthProjectHit? hit)
        {
            if (hit == null) return;

            ActiveModDetailsVm = new ModDetailsViewModel(hit, Profile, _modrinthService, _profileManager, _imageCacheService, _modpackImportService);
            ActiveModDetailsVm.CloseRequested += () => IsModDetailsOpen = false;
            ActiveModDetailsVm.ModInstalled += () => RefreshInstalledMods();
            IsModDetailsOpen = true;
        }

        [RelayCommand]
        public async Task QuickInstallModAsync(ModrinthProjectHit? hit)
        {
            if (hit == null) return;

            StatusMessage = $"Установка {hit.Title} и зависимостей...";
            try
            {
                var loader = Profile.LoaderType == LoaderType.Fabric ? "fabric" : null;
                var versions = await _modrinthService.GetProjectVersionsAsync(hit.ProjectId, Profile.GameVersion, loader);
                var targetVer = versions.FirstOrDefault(v => v.Files.Count > 0)
                    ?? throw new InvalidOperationException("Нет подходящей версии для текущего Minecraft.");

                var modsDir = _profileManager.GetProfileModsDir(Profile);
                await _modrinthService.InstallModWithDependenciesAsync(targetVer, modsDir, Profile.GameVersion, loader);
                StatusMessage = $"Мод {hit.Title} и зависимости успешно установлены в сборку!";
                RefreshInstalledMods();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ToggleMod(InstalledMod mod)
        {
            if (_modrinthService.ToggleMod(mod.FullPath))
            {
                RefreshInstalledMods();
            }
        }

        [RelayCommand]
        private void DeleteMod(InstalledMod mod)
        {
            if (_modrinthService.DeleteMod(mod.FullPath))
            {
                RefreshInstalledMods();
            }
        }

        [RelayCommand]
        private void OpenGameFolder() => OpenFolder(_profileManager.GetProfileGameDir(Profile));

        [RelayCommand]
        private void OpenModsFolder() => OpenFolder(_profileManager.GetProfileModsDir(Profile));

        [RelayCommand]
        private void OpenResourcePacksFolder()
        {
            var dir = Path.Combine(_profileManager.GetProfileGameDir(Profile), "resourcepacks");
            Directory.CreateDirectory(dir);
            OpenFolder(dir);
        }

        [RelayCommand]
        private void OpenShaderPacksFolder()
        {
            var dir = Path.Combine(_profileManager.GetProfileGameDir(Profile), "shaderpacks");
            Directory.CreateDirectory(dir);
            OpenFolder(dir);
        }

        [RelayCommand]
        private void OpenSavesFolder()
        {
            var dir = Path.Combine(_profileManager.GetProfileGameDir(Profile), "saves");
            Directory.CreateDirectory(dir);
            OpenFolder(dir);
        }

        [RelayCommand]
        private void OpenScreenshotsFolder()
        {
            var dir = Path.Combine(_profileManager.GetProfileGameDir(Profile), "screenshots");
            Directory.CreateDirectory(dir);
            OpenFolder(dir);
        }

        [RelayCommand]
        private void OpenDataPacksFolder()
        {
            var dir = Path.Combine(_profileManager.GetProfileGameDir(Profile), "datapacks");
            Directory.CreateDirectory(dir);
            OpenFolder(dir);
        }

        [RelayCommand]
        private void OpenConfigFolder()
        {
            var dir = Path.Combine(_profileManager.GetProfileGameDir(Profile), "config");
            Directory.CreateDirectory(dir);
            OpenFolder(dir);
        }

        [RelayCommand]
        private void OpenWorldFolder(WorldItem? item)
        {
            if (item != null && Directory.Exists(item.FullPath)) OpenFolder(item.FullPath);
        }

        [RelayCommand]
        private void OpenWorldDataPacks(WorldItem? item)
        {
            if (item != null)
            {
                var dir = Path.Combine(item.FullPath, "datapacks");
                Directory.CreateDirectory(dir);
                OpenFolder(dir);
            }
        }

        private static void OpenFolder(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        [RelayCommand]
        private void Duplicate()
        {
            DuplicateRequested?.Invoke(Profile);
        }

        [RelayCommand]
        private void Delete()
        {
            if (Profile.IsServerProfile) return;
            DeleteRequested?.Invoke(Profile);
        }

        public Avalonia.Media.Geometry? IconGeometry
        {
            get
            {
                var key = Profile.IconPreset switch
                {
                    "sword" => "IconSword",
                    "pickaxe" => "IconPickaxe",
                    "diamond" => "IconDiamond",
                    "creeper" => "IconShield",
                    "redstone" => "IconZap",
                    "book" => "IconFileText",
                    "portal" => "IconLayers",
                    "gear" => "IconSettings",
                    _ => "IconBoxes"
                };

                if (Avalonia.Application.Current != null && Avalonia.Application.Current.TryGetResource(key, null, out var res) && res is Avalonia.Media.Geometry g)
                {
                    return g;
                }
                return null;
            }
        }

        [RelayCommand]
        public void SetIconPreset(string preset)
        {
            Profile.IconPreset = preset;
            Profile.CustomIconPath = null;
            Profile.IconBitmap = null;
            _profileManager.UpdateProfile(Profile);
            OnPropertyChanged(nameof(Profile));
            OnPropertyChanged(nameof(IconGeometry));
            OnPropertyChanged(nameof(HasCustomIcon));
            OnPropertyChanged(nameof(CurrentIconResolution));
            StatusMessage = $"Иконка сборки изменена: {preset}";
        }

        [RelayCommand]
        public void OpenSettings()
        {
            SelectedTabIndex = 5;
        }

        [RelayCommand]
        private void SaveProfileChanges()
        {
            _profileManager.UpdateProfile(Profile);
            StatusMessage = "Настройки сборки сохранены!";
        }

        public void UpdateLaunchProgress(bool isLaunching, bool isRunning, double percent, string status)
        {
            IsLaunching = isLaunching;
            IsGameRunning = isRunning;
            LaunchProgressPercent = percent;
            LaunchStatusText = status;
            OnPropertyChanged(nameof(IsProfileInstalled));
            OnPropertyChanged(nameof(PlayButtonText));
            OnPropertyChanged(nameof(PlayButtonIcon));
            OnPropertyChanged(nameof(IsDangerButton));
            OnPropertyChanged(nameof(ShowProgressBar));
        }

        [RelayCommand]
        private void Play()
        {
            if (IsLaunching)
            {
                CancelLaunchRequested?.Invoke();
                return;
            }

            if (IsGameRunning)
            {
                CloseGameRequested?.Invoke();
                return;
            }

            _profileManager.SetActiveProfile(Profile);
            PlayRequested?.Invoke();
        }

        public bool HasCustomIcon => Profile.HasCustomIcon;

        public string CurrentIconResolution
        {
            get
            {
                if (Profile.IconBitmap != null)
                {
                    return $"{(int)Profile.IconBitmap.Size.Width} × {(int)Profile.IconBitmap.Size.Height} px";
                }
                return "Стандартный размер";
            }
        }

        public async Task SetCustomImageAsync(string sourcePath)
        {
            try
            {
                var profileDir = _profileManager.GetProfileGameDir(Profile);
                Directory.CreateDirectory(profileDir);
                var targetIconPath = Path.Combine(profileDir, "icon.png");

                await Task.Run(() =>
                {
                    using var inStream = File.OpenRead(sourcePath);
                    using var srcBmp = SKBitmap.Decode(inStream);
                    if (srcBmp != null)
                    {
                        int maxDim = Math.Max(srcBmp.Width, srcBmp.Height);
                        using var squareBmp = new SKBitmap(maxDim, maxDim, SKColorType.Bgra8888, SKAlphaType.Premul);
                        using (var canvas = new SKCanvas(squareBmp))
                        {
                            canvas.Clear(SKColors.Transparent);
                            float left = (maxDim - srcBmp.Width) / 2f;
                            float top = (maxDim - srcBmp.Height) / 2f;
                            canvas.DrawBitmap(srcBmp, left, top);
                        }

                        using var img = SKImage.FromBitmap(squareBmp);
                        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                        using var outStream = File.Create(targetIconPath);
                        data.SaveTo(outStream);
                    }
                    else
                    {
                        File.Copy(sourcePath, targetIconPath, true);
                    }
                });

                var bytes = await File.ReadAllBytesAsync(targetIconPath);
                using var ms = new MemoryStream(bytes);
                Profile.IconBitmap = new Avalonia.Media.Imaging.Bitmap(ms);
                Profile.CustomIconPath = targetIconPath;
                Profile.IconPreset = "custom";
                _profileManager.UpdateProfile(Profile);

                OnPropertyChanged(nameof(Profile));
                OnPropertyChanged(nameof(HasCustomIcon));
                OnPropertyChanged(nameof(CurrentIconResolution));
                StatusMessage = "Изображение сборки успешно установлено!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка загрузки изображения: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task Upscale2xAsync() => await UpscaleIconAsync(2);

        [RelayCommand]
        public async Task Upscale4xAsync() => await UpscaleIconAsync(4);

        private async Task UpscaleIconAsync(int multiplier)
        {
            if (string.IsNullOrEmpty(Profile.CustomIconPath) || !File.Exists(Profile.CustomIconPath))
            {
                StatusMessage = "Сначала загрузите собственное изображение для сборки.";
                return;
            }

            try
            {
                StatusMessage = $"Выполняется апскейл {multiplier}x (супер-разрешение)...";
                var iconPath = Profile.CustomIconPath;

                await Task.Run(() =>
                {
                    using var inStream = File.OpenRead(iconPath);
                    using var orig = SKBitmap.Decode(inStream);
                    if (orig == null) throw new InvalidOperationException("Не удалось прочитать изображение.");

                    int newW = orig.Width * multiplier;
                    int newH = orig.Height * multiplier;

                    // 1. High quality bicubic resampling
                    using var upscaled = new SKBitmap(newW, newH, SKColorType.Bgra8888, SKAlphaType.Premul);
                    using (var canvas = new SKCanvas(upscaled))
                    {
                        canvas.Clear(SKColors.Transparent);
                        using var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
                        canvas.DrawBitmap(orig, new SKRect(0, 0, newW, newH), p);
                    }

                    // 2. Multi-stage edge-preserving sharpening convolution
                    float[] kernel = new float[]
                    {
                         0.0f, -0.2f,  0.0f,
                        -0.2f,  1.8f, -0.2f,
                         0.0f, -0.2f,  0.0f
                    };
                    using var sharpenFilter = SKImageFilter.CreateMatrixConvolution(
                        new SKSizeI(3, 3),
                        kernel,
                        1.0f,
                        0.0f,
                        new SKPointI(1, 1),
                        SKShaderTileMode.Clamp,
                        false);

                    using var finalBmp = new SKBitmap(newW, newH, SKColorType.Bgra8888, SKAlphaType.Premul);
                    using (var finalCanvas = new SKCanvas(finalBmp))
                    {
                        finalCanvas.Clear(SKColors.Transparent);
                        using var sp = new SKPaint { ImageFilter = sharpenFilter, IsAntialias = true };
                        finalCanvas.DrawBitmap(upscaled, 0, 0, sp);
                    }

                    using var img = SKImage.FromBitmap(finalBmp);
                    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                    using var outStream = File.Create(iconPath);
                    data.SaveTo(outStream);
                });

                var bytes = await File.ReadAllBytesAsync(iconPath);
                using var ms = new MemoryStream(bytes);
                Profile.IconBitmap = new Avalonia.Media.Imaging.Bitmap(ms);
                _profileManager.UpdateProfile(Profile);

                OnPropertyChanged(nameof(Profile));
                OnPropertyChanged(nameof(HasCustomIcon));
                OnPropertyChanged(nameof(CurrentIconResolution));
                StatusMessage = $"Апскейл {multiplier}x завершён ({CurrentIconResolution})! Детализация повышена.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка при апскейле изображения: {ex.Message}";
            }
        }

        [RelayCommand]
        public void ResetCustomIcon()
        {
            Profile.CustomIconPath = null;
            Profile.IconBitmap = null;
            Profile.IconPreset = "default";
            _profileManager.UpdateProfile(Profile);
            OnPropertyChanged(nameof(Profile));
            OnPropertyChanged(nameof(HasCustomIcon));
            OnPropertyChanged(nameof(CurrentIconResolution));
            StatusMessage = "Иконка сборки сброшена на стандартную.";
        }

        [RelayCommand]
        public void OpenCropCurrentIcon()
        {
            if (!string.IsNullOrEmpty(Profile.CustomIconPath) && File.Exists(Profile.CustomIconPath))
            {
                StartChangeIcon(Profile.CustomIconPath);
            }
            else
            {
                StatusMessage = "Сначала загрузите картинку для сборки.";
            }
        }

        public void StartChangeIcon(string sourceImagePath)
        {
            if (!File.Exists(sourceImagePath)) return;

            var profileDir = _profileManager.GetProfileGameDir(Profile);
            Directory.CreateDirectory(profileDir);
            var targetIconPath = Path.Combine(profileDir, "icon.png");

            CropDialogVm = new ImageCropViewModel(sourceImagePath, targetIconPath);
            CropDialogVm.CropCompleted += (savedPath) =>
            {
                IsCropDialogOpen = false;
                CropDialogVm = null;
                Profile.CustomIconPath = savedPath;
                Profile.IconPreset = "custom";
                byte[] bytes = File.ReadAllBytes(savedPath);
                using (var ms = new MemoryStream(bytes))
                {
                    Profile.IconBitmap = new Avalonia.Media.Imaging.Bitmap(ms);
                }
                _profileManager.UpdateProfile(Profile);
                OnPropertyChanged(nameof(Profile));
                OnPropertyChanged(nameof(HasCustomIcon));
                OnPropertyChanged(nameof(CurrentIconResolution));
                StatusMessage = "Иконка сборки успешно кадрирована (кроп) и применена!";
            };
            CropDialogVm.CancelRequested += () =>
            {
                IsCropDialogOpen = false;
                CropDialogVm = null;
            };
            IsCropDialogOpen = true;
        }

        [RelayCommand]
        private void GoBack()
        {
            BackRequested?.Invoke();
        }
    }
}
