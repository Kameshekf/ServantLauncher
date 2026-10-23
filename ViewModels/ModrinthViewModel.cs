using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServantLauncher.Models;
using ServantLauncher.Services;

namespace ServantLauncher.ViewModels
{
    public partial class ModrinthViewModel : ObservableObject
    {
        private readonly IModrinthService _modrinthService;
        private readonly IProfileManager _profileManager;
        private readonly IImageCacheService _imageCacheService;
        private readonly IModpackImportService? _modpackImportService;
        private readonly IGlobalProgressService? _globalProgressService;
        private readonly HttpClient _httpClient;
        private CancellationTokenSource? _searchDebounceCts;

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private bool _isSearching;

        [ObservableProperty]
        private bool _isInstalling;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private ModrinthProjectHit? _selectedProject;

        [ObservableProperty]
        private int _selectedTabIndex = 0; // 0 = Каталог, 1 = Установленные

        [ObservableProperty]
        private string _selectedCategory = "Все";

        [ObservableProperty]
        private int _currentPage = 1;

        [ObservableProperty]
        private int _totalHits;

        [ObservableProperty]
        private int _totalPages = 1;

        [ObservableProperty]
        private bool _canGoPrevPage;

        [ObservableProperty]
        private bool _canGoNextPage;

        [ObservableProperty]
        private string _paginationInfo = "Страница 1";

        [ObservableProperty]
        private bool _hasMultiplePages;

        // Modpack Version Selection Dialog
        [ObservableProperty]
        private bool _isModpackVersionDialogOpen;

        [ObservableProperty]
        private ModrinthProjectHit? _selectedModpackForInstall;

        [ObservableProperty]
        private bool _isLoadingModpackVersions;

        [ObservableProperty]
        private string _modpackVersionDialogStatus = string.Empty;

        [ObservableProperty]
        private string _selectedMinecraftVersion = string.Empty;

        [ObservableProperty]
        private ModrinthVersion? _selectedModpackRelease;

        [ObservableProperty]
        private bool _isInstallingModpack;

        public ObservableCollection<string> AvailableMinecraftVersions { get; } = new();
        public ObservableCollection<ModrinthVersion> FilteredModpackReleases { get; } = new();
        private List<ModrinthVersion> _allModpackVersions = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HeaderTitle))]
        [NotifyPropertyChangedFor(nameof(HeaderSubtitle))]
        [NotifyPropertyChangedFor(nameof(SearchWatermark))]
        private bool _isModpackCatalogMode = true;

        [ObservableProperty]
        private ModDetailsViewModel? _activeModDetailsVm;

        [ObservableProperty]
        private bool _isModDetailsOpen;

        public string HeaderTitle => IsModpackCatalogMode ? "КАТАЛОГ СБОРОК (MODRINTH)" : "КАТАЛОГ МОДОВ (MODRINTH)";
        public string HeaderSubtitle => IsModpackCatalogMode ? "Каталог готовых сборок и модпаков. Установка в один клик!" : "Каталог модификаций для активного профиля";
        public string SearchWatermark => IsModpackCatalogMode ? "Поиск готовых сборок на Modrinth (начните вводить название)..." : "Поиск модов на Modrinth (начните вводить название)...";

        public ObservableCollection<ModrinthProjectHit> SearchResults { get; } = new();
        public ObservableCollection<InstalledMod> InstalledMods { get; } = new();
        public ObservableCollection<string> Categories { get; } = new();

        public event Action? BackRequested;

        public ModrinthViewModel(
            IModrinthService modrinthService,
            IProfileManager profileManager,
            IImageCacheService? imageCacheService = null,
            IModpackImportService? modpackImportService = null,
            IGlobalProgressService? globalProgressService = null,
            HttpClient? httpClient = null)
        {
            _modrinthService = modrinthService;
            _profileManager = profileManager;
            _imageCacheService = imageCacheService ?? new ImageCacheService();
            _modpackImportService = modpackImportService;
            _globalProgressService = globalProgressService;
            _httpClient = httpClient ?? HttpHelper.CreateHttpClient();

            UpdateCategories();

            _profileManager.ActiveProfileChanged += () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    RefreshInstalledMods();
                    _ = TriggerLiveSearchAsync();
                });
            };

            RefreshInstalledMods();
            _ = TriggerLiveSearchAsync();
        }

        public void SetCatalogMode(bool isModpackMode)
        {
            IsModpackCatalogMode = isModpackMode;
            SelectedCategory = "Все";
            SelectedTabIndex = 0;
            UpdateCategories();
            _ = TriggerLiveSearchAsync();
        }

        private void UpdateCategories()
        {
            Categories.Clear();
            if (IsModpackCatalogMode)
            {
                Categories.Add("Все");
                Categories.Add("Optimization");
                Categories.Add("Adventure");
                Categories.Add("Magic");
                Categories.Add("Technology");
                Categories.Add("Quests");
                Categories.Add("Combat");
            }
            else
            {
                Categories.Add("Все");
                Categories.Add("Модпаки");
                Categories.Add("Optimization");
                Categories.Add("Utility");
                Categories.Add("Decoration");
                Categories.Add("Voice Chat");
                Categories.Add("Technology");
                Categories.Add("Worldgen");
            }
        }

        [RelayCommand]
        public void Back()
        {
            BackRequested?.Invoke();
        }

        partial void OnSearchQueryChanged(string value)
        {
            CurrentPage = 1;
            DebounceSearch();
        }

        partial void OnSelectedCategoryChanged(string value)
        {
            CurrentPage = 1;
            DebounceSearch();
        }

        partial void OnSelectedTabIndexChanged(int value)
        {
            if (value == 1) RefreshInstalledMods();
            else if (SearchResults.Count == 0) _ = TriggerLiveSearchAsync();
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

        [RelayCommand]
        public void SelectCategory(string category)
        {
            SelectedCategory = category;
            CurrentPage = 1;
        }

        [RelayCommand]
        public async Task NextPageAsync()
        {
            if (!CanGoNextPage || IsSearching) return;
            CurrentPage++;
            await TriggerLiveSearchAsync();
        }

        [RelayCommand]
        public async Task PreviousPageAsync()
        {
            if (!CanGoPrevPage || IsSearching) return;
            CurrentPage--;
            await TriggerLiveSearchAsync();
        }

        [RelayCommand]
        public async Task TriggerLiveSearchAsync()
        {
            IsSearching = true;
            StatusMessage = "Загрузка проектов...";

            try
            {
                var active = _profileManager.ActiveProfile;
                bool isModpack = IsModpackCatalogMode || string.Equals(SelectedCategory, "Модпаки", StringComparison.OrdinalIgnoreCase);

                var loader = isModpack ? null : (active?.LoaderType == LoaderType.Fabric ? "fabric" : null);
                var gameVer = isModpack ? null : active?.GameVersion;

                var query = SearchQuery?.Trim() ?? "";
                if (SelectedCategory != "Все" && SelectedCategory != "Модпаки")
                {
                    query = string.IsNullOrEmpty(query) ? SelectedCategory : $"{query} {SelectedCategory}";
                }

                const int pageSize = 24;
                int offset = Math.Max(0, (CurrentPage - 1) * pageSize);
                var projectType = isModpack ? "modpack" : "mod";
                var res = await _modrinthService.SearchModsAsync(query, gameVer, loader, limit: pageSize, offset: offset, projectType: projectType);
                
                SearchResults.Clear();
                foreach (var hit in res.Hits)
                {
                    SearchResults.Add(hit);
                    // Load icon
                    if (!string.IsNullOrEmpty(hit.IconUrl))
                    {
                        _ = Task.Run(async () =>
                        {
                            var bmp = await _imageCacheService.LoadImageAsync(hit.IconUrl);
                            Dispatcher.UIThread.Post(() => hit.IconBitmap = bmp);
                        });
                    }
                }

                if (SearchResults.Count > 0)
                {
                    SelectedProject = SearchResults.First();
                }

                TotalHits = res.TotalHits;
                TotalPages = Math.Max(1, (int)Math.Ceiling((double)res.TotalHits / pageSize));
                CanGoPrevPage = CurrentPage > 1;
                CanGoNextPage = CurrentPage < TotalPages;
                HasMultiplePages = TotalPages > 1;
                PaginationInfo = $"Страница {CurrentPage} из {TotalPages}";

                StatusMessage = string.IsNullOrEmpty(query)
                    ? $"Популярные {(isModpack ? "сборки" : "моды")} ({res.TotalHits})"
                    : $"Найдено {res.TotalHits} по запросу '{query}'";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка поиска: {ex.Message}";
            }
            finally
            {
                IsSearching = false;
            }
        }

        [RelayCommand]
        public void OpenModDetails(ModrinthProjectHit? hit)
        {
            var project = hit ?? SelectedProject;
            if (project == null) return;

            var active = _profileManager.ActiveProfile;
            bool isModpack = project.IsModpack || string.Equals(project.ProjectType, "modpack", StringComparison.OrdinalIgnoreCase);
            if (!isModpack && active == null) return;

            ActiveModDetailsVm = new ModDetailsViewModel(
                project,
                active,
                _modrinthService,
                _profileManager,
                _imageCacheService,
                _modpackImportService,
                _globalProgressService,
                _httpClient);
            ActiveModDetailsVm.CloseRequested += () => IsModDetailsOpen = false;
            ActiveModDetailsVm.ModInstalled += () => RefreshInstalledMods();
            IsModDetailsOpen = true;
        }

        partial void OnSelectedMinecraftVersionChanged(string value)
        {
            UpdateFilteredModpackReleases();
        }

        private void UpdateFilteredModpackReleases()
        {
            FilteredModpackReleases.Clear();
            if (string.IsNullOrEmpty(SelectedMinecraftVersion))
            {
                foreach (var v in _allModpackVersions)
                {
                    FilteredModpackReleases.Add(v);
                }
            }
            else
            {
                var matching = _allModpackVersions
                    .Where(v => v.GameVersions != null && v.GameVersions.Contains(SelectedMinecraftVersion))
                    .ToList();
                foreach (var v in matching)
                {
                    FilteredModpackReleases.Add(v);
                }
            }
            SelectedModpackRelease = FilteredModpackReleases.FirstOrDefault();
        }

        public async Task OpenModpackVersionDialogAsync(ModrinthProjectHit project)
        {
            SelectedModpackForInstall = project;
            IsModpackVersionDialogOpen = true;
            IsLoadingModpackVersions = true;
            ModpackVersionDialogStatus = "Загрузка версий сборки...";
            AvailableMinecraftVersions.Clear();
            FilteredModpackReleases.Clear();
            _allModpackVersions.Clear();
            SelectedMinecraftVersion = string.Empty;
            SelectedModpackRelease = null;

            try
            {
                var versions = await _modrinthService.GetProjectVersionsAsync(project.ProjectId);
                var validVersions = versions
                    .Where(v => v.Files != null && v.Files.Count > 0)
                    .ToList();

                if (validVersions.Count == 0)
                {
                    ModpackVersionDialogStatus = "Для данной сборки не найдены файлы для скачивания.";
                    IsLoadingModpackVersions = false;
                    return;
                }

                _allModpackVersions = validVersions;

                // Extract unique game versions sorted descending
                var mcVersions = validVersions
                    .SelectMany(v => v.GameVersions ?? Enumerable.Empty<string>())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct()
                    .OrderByDescending(v => ParseMinecraftVersion(v))
                    .ToList();

                foreach (var v in mcVersions)
                {
                    AvailableMinecraftVersions.Add(v);
                }

                // If active profile exists and its version is in available list, default to it; otherwise default to newest
                var activeVer = _profileManager.ActiveProfile?.GameVersion;
                if (!string.IsNullOrEmpty(activeVer) && AvailableMinecraftVersions.Contains(activeVer))
                {
                    SelectedMinecraftVersion = activeVer;
                }
                else
                {
                    SelectedMinecraftVersion = AvailableMinecraftVersions.FirstOrDefault() ?? "";
                }

                UpdateFilteredModpackReleases();
                ModpackVersionDialogStatus = string.Empty;
            }
            catch (Exception ex)
            {
                ModpackVersionDialogStatus = $"Ошибка загрузки версий: {ex.Message}";
            }
            finally
            {
                IsLoadingModpackVersions = false;
            }
        }

        private static Version ParseMinecraftVersion(string v)
        {
            try
            {
                var m = Regex.Match(v, @"^(\d+)\.(\d+)(?:\.(\d+))?");
                if (m.Success)
                {
                    int maj = int.Parse(m.Groups[1].Value);
                    int min = int.Parse(m.Groups[2].Value);
                    int patch = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
                    return new Version(maj, min, patch);
                }
            }
            catch { }
            return new Version(0, 0, 0);
        }

        [RelayCommand]
        public void CloseModpackVersionDialog()
        {
            if (IsInstallingModpack) return;
            IsModpackVersionDialogOpen = false;
        }

        [RelayCommand]
        public async Task ConfirmInstallModpackAsync()
        {
            if (SelectedModpackForInstall == null || SelectedModpackRelease == null) return;
            if (_modpackImportService == null)
            {
                ModpackVersionDialogStatus = "Сервис импорта модпаков недоступен.";
                return;
            }

            var project = SelectedModpackForInstall;
            var targetVer = SelectedModpackRelease;

            IsInstallingModpack = true;
            ModpackVersionDialogStatus = $"Скачивание сборки '{project.Title}' ({targetVer.VersionNumber})...";
            _globalProgressService?.StartTask("Установка сборки", $"Загрузка {project.Title}...", canCancel: false);
            _globalProgressService?.UpdateProgress(10, $"Подготовка к установке {project.Title}...");

            try
            {
                var mrpackFile = targetVer.Files.FirstOrDefault(f => f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                    ?? targetVer.Files.FirstOrDefault()
                    ?? throw new InvalidOperationException($"Не найден файл .mrpack для сборки {project.Title}.");

                var tempDir = Path.Combine(Path.GetTempPath(), "ServantMrpacks");
                Directory.CreateDirectory(tempDir);
                var tempPath = Path.Combine(tempDir, mrpackFile.Filename);

                ModpackVersionDialogStatus = $"Скачивание {mrpackFile.Filename}...";
                _globalProgressService?.UpdateProgress(20, $"Скачивание {mrpackFile.Filename}...");

                using (var resp = await _httpClient.GetAsync(mrpackFile.Url, HttpCompletionOption.ResponseHeadersRead))
                {
                    resp.EnsureSuccessStatusCode();
                    using (var s = await resp.Content.ReadAsStreamAsync())
                    using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                    {
                        await s.CopyToAsync(fs);
                    }
                }

                var progress = new Progress<(double percent, string status)>(p =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        ModpackVersionDialogStatus = p.status;
                        StatusMessage = p.status;
                        _globalProgressService?.UpdateProgress(p.percent, p.status);
                    });
                });

                var newProfile = await _modpackImportService.ImportMrpackAsync(tempPath, progress);
                if ((string.IsNullOrEmpty(newProfile.CustomIconPath) || !File.Exists(newProfile.CustomIconPath)) && !string.IsNullOrEmpty(project.IconUrl))
                {
                    try
                    {
                        var gameDir = _profileManager.GetProfileGameDir(newProfile);
                        var targetIcon = Path.Combine(gameDir, "icon.png");
                        var iconBytes = await _httpClient.GetByteArrayAsync(project.IconUrl);
                        await File.WriteAllBytesAsync(targetIcon, iconBytes);
                        newProfile.CustomIconPath = targetIcon;
                        _profileManager.SaveProfiles();
                    }
                    catch { }
                }
                _profileManager.SetActiveProfile(newProfile);

                StatusMessage = $"Сборка '{newProfile.Name}' успешно установлена!";
                _globalProgressService?.FinishTask($"Сборка '{newProfile.Name}' успешно установлена!");
                RefreshInstalledMods();

                IsModpackVersionDialogOpen = false;
            }
            catch (Exception ex)
            {
                ModpackVersionDialogStatus = $"Ошибка: {ex.Message}";
                StatusMessage = $"Ошибка установки сборки: {ex.Message}";
                _globalProgressService?.FinishTask($"Ошибка: {ex.Message}");
            }
            finally
            {
                IsInstallingModpack = false;
            }
        }

        [RelayCommand]
        public async Task InstallSelectedModAsync(ModrinthProjectHit? hit)
        {
            var project = hit ?? SelectedProject;
            if (project == null) return;

            // Handle Modrinth Modpack version selection and install
            if (project.IsModpack || string.Equals(project.ProjectType, "modpack", StringComparison.OrdinalIgnoreCase))
            {
                await OpenModpackVersionDialogAsync(project);
                return;
            }

            // Normal mod installation
            var active = _profileManager.ActiveProfile;
            if (active == null) return;

            IsInstalling = true;
            StatusMessage = $"Установка {project.Title}...";

            try
            {
                var loader = active.LoaderType == LoaderType.Fabric ? "fabric" : null;
                var versions = await _modrinthService.GetProjectVersionsAsync(project.ProjectId, active.GameVersion, loader);

                var targetVer = versions.FirstOrDefault(v => v.Files.Count > 0)
                    ?? throw new InvalidOperationException($"Не найдена совместимая версия мода для {active.GameVersion} ({active.LoaderType}).");

                var modsDir = _profileManager.GetProfileModsDir(active);
                await _modrinthService.InstallModVersionAsync(targetVer, modsDir);

                StatusMessage = $"Мод {project.Title} успешно установлен в сборку {active.Name}!";
                RefreshInstalledMods();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка установки: {ex.Message}";
            }
            finally
            {
                IsInstalling = false;
            }
        }

        public void RefreshInstalledMods()
        {
            InstalledMods.Clear();
            var active = _profileManager.ActiveProfile;
            if (active != null)
            {
                var modsDir = _profileManager.GetProfileModsDir(active);
                var list = _modrinthService.GetInstalledMods(modsDir);
                foreach (var m in list)
                {
                    InstalledMods.Add(m);
                }
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
        private void OpenModsFolder()
        {
            var active = _profileManager.ActiveProfile;
            if (active != null)
            {
                var modsDir = _profileManager.GetProfileModsDir(active);
                Directory.CreateDirectory(modsDir);
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{modsDir}\"",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        public void AddFiles(IEnumerable<string> paths)
        {
            var active = _profileManager.ActiveProfile;
            if (active == null) return;

            var profileDir = _profileManager.GetProfileGameDir(active);
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
                    catch { }
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
    }
}
