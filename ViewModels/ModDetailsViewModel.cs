using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServantLauncher.Models;
using ServantLauncher.Services;

namespace ServantLauncher.ViewModels
{
    public partial class ModDetailsViewModel : ObservableObject
    {
        private readonly IModrinthService _modrinthService;
        private readonly IProfileManager _profileManager;
        private readonly IImageCacheService _imageCacheService;
        private readonly IModpackImportService? _modpackImportService;
        private readonly IGlobalProgressService? _globalProgressService;
        private readonly HttpClient _httpClient;
        private readonly GameProfile? _targetProfile;

        [ObservableProperty]
        private ModrinthProjectHit _project;

        public bool IsModpack => Project.IsModpack || string.Equals(Project.ProjectType, "modpack", StringComparison.OrdinalIgnoreCase);

        public string HeaderTypeBadge => IsModpack ? "СБОРКА" : "МОД";
        public string VersionsTitleText => IsModpack ? "ДОСТУПНЫЕ ВЕРСИИ СБОРКИ" : "СОВМЕСТИМЫЕ ВЕРСИИ МОДА";
        public string GalleryTitleText => IsModpack ? "СКРИНШОТЫ СБОРКИ" : "СКРИНШОТЫ И ИЛЛЮСТРАЦИИ МОДА";
        public string InstallButtonText => IsModpack ? "Установить сборку" : "Установить";

        [ObservableProperty]
        private int _selectedTabIndex = 0; // 0 = Описание, 1 = Версии, 2 = Галерея

        public bool IsTabDescription => SelectedTabIndex == 0;
        public bool IsTabVersions => SelectedTabIndex == 1;
        public bool IsTabGallery => SelectedTabIndex == 2;

        partial void OnSelectedTabIndexChanged(int value)
        {
            OnPropertyChanged(nameof(IsTabDescription));
            OnPropertyChanged(nameof(IsTabVersions));
            OnPropertyChanged(nameof(IsTabGallery));
        }

        [RelayCommand]
        public void SelectTab(string tab)
        {
            SelectedTabIndex = tab switch
            {
                "versions" => 1,
                "gallery" => 2,
                _ => 0
            };
        }

        [ObservableProperty]
        private bool _isLoadingVersions;

        [ObservableProperty]
        private bool _isInstalling;

        [ObservableProperty]
        private string _statusText = string.Empty;

        [ObservableProperty]
        private ModrinthVersion? _selectedVersion;

        [ObservableProperty]
        private bool _hasGallery;

        public ObservableCollection<ModrinthVersion> Versions { get; } = new();
        public ObservableCollection<MarkdownBlock> MarkdownBlocks { get; } = new();
        public ObservableCollection<MarkdownBlock> GalleryBlocks { get; } = new();

        public event Action? CloseRequested;
        public event Action? ModInstalled;

        public ModDetailsViewModel(
            ModrinthProjectHit project,
            GameProfile? targetProfile,
            IModrinthService modrinthService,
            IProfileManager profileManager,
            IImageCacheService? imageCacheService = null,
            IModpackImportService? modpackImportService = null,
            IGlobalProgressService? globalProgressService = null,
            HttpClient? httpClient = null)
        {
            _project = project;
            _targetProfile = targetProfile;
            _modrinthService = modrinthService;
            _profileManager = profileManager;
            _imageCacheService = imageCacheService ?? new ImageCacheService();
            _modpackImportService = modpackImportService;
            _globalProgressService = globalProgressService;
            _httpClient = httpClient ?? HttpHelper.CreateHttpClient();

            _ = LoadDetailsAndVersionsAsync();
        }

        private async Task LoadDetailsAndVersionsAsync()
        {
            IsLoadingVersions = true;
            StatusText = IsModpack ? "Загрузка информации о сборке..." : "Загрузка информации о моде...";
            Versions.Clear();
            MarkdownBlocks.Clear();
            GalleryBlocks.Clear();

            try
            {
                // Fetch full project details for body and gallery
                var details = await _modrinthService.GetProjectDetailsAsync(Project.ProjectId);
                if (details != null)
                {
                    if (!string.IsNullOrEmpty(details.Body))
                    {
                        Project.Body = details.Body;
                        ParseMarkdown(details.Body);
                    }
                    else if (!string.IsNullOrEmpty(Project.Description))
                    {
                        MarkdownBlocks.Add(new MarkdownBlock
                        {
                            Type = MarkdownBlockType.Paragraph,
                            Text = Project.Description
                        });
                    }

                    if (details.Gallery != null && details.Gallery.Count > 0)
                    {
                        HasGallery = true;
                        foreach (var item in details.Gallery)
                        {
                            var block = new MarkdownBlock
                            {
                                Type = MarkdownBlockType.Image,
                                Text = string.IsNullOrEmpty(item.Title) ? item.Description : item.Title,
                                ImageUrl = item.Url
                            };
                            GalleryBlocks.Add(block);
                            _ = Task.Run(async () =>
                            {
                                var bmp = await _imageCacheService.LoadImageAsync(item.Url);
                                if (bmp != null)
                                {
                                    Dispatcher.UIThread.Post(() => block.ImageBitmap = bmp);
                                }
                            });
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(Project.Description))
                {
                    MarkdownBlocks.Add(new MarkdownBlock
                    {
                        Type = MarkdownBlockType.Paragraph,
                        Text = Project.Description
                    });
                }

                if (IsModpack)
                {
                    var list = await _modrinthService.GetProjectVersionsAsync(Project.ProjectId);
                    foreach (var v in list)
                    {
                        Versions.Add(v);
                    }

                    SelectedVersion = Versions.FirstOrDefault();
                    StatusText = Versions.Count > 0
                        ? $"Найдено {Versions.Count} доступных версий сборки"
                        : "Нет доступных версий.";
                }
                else
                {
                    var loader = _targetProfile?.LoaderType == LoaderType.Fabric ? "fabric" : null;
                    var gameVer = _targetProfile?.GameVersion;
                    var list = await _modrinthService.GetProjectVersionsAsync(Project.ProjectId, gameVer, loader);

                    foreach (var v in list)
                    {
                        Versions.Add(v);
                    }

                    SelectedVersion = Versions.FirstOrDefault();
                    StatusText = Versions.Count > 0 
                        ? $"Найдено {Versions.Count} совместимых версий для {_targetProfile?.GameVersion} ({_targetProfile?.LoaderType})" 
                        : "Нет версий, точно совместимых с текущей сборкой.";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsLoadingVersions = false;
            }
        }

        private void ParseMarkdown(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return;

            // Pre-clean raw HTML breaklines and div spacing
            var normalized = body.Replace("\r\n", "\n")
                                 .Replace("<br>", "\n")
                                 .Replace("<br/>", "\n")
                                 .Replace("<br />", "\n");

            var lines = normalized.Split('\n');
            bool inCodeBlock = false;
            string currentCode = string.Empty;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();

                if (line.StartsWith("```"))
                {
                    if (inCodeBlock)
                    {
                        inCodeBlock = false;
                        if (!string.IsNullOrWhiteSpace(currentCode))
                        {
                            MarkdownBlocks.Add(new MarkdownBlock
                            {
                                Type = MarkdownBlockType.Code,
                                Text = currentCode.Trim()
                            });
                            currentCode = string.Empty;
                        }
                    }
                    else
                    {
                        inCodeBlock = true;
                        currentCode = string.Empty;
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    currentCode += line + "\n";
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line)) continue;

                // Check Markdown table separator line: |---|---| or |:---|
                if (Regex.IsMatch(line, @"^\|?\s*[-:]+[-| :]*\|$"))
                {
                    continue;
                }

                // Check Markdown table row
                if (line.StartsWith("|") && line.EndsWith("|"))
                {
                    var cells = line.Trim('|').Split('|').Select(c => CleanMarkdown(c)).Where(c => !string.IsNullOrEmpty(c)).ToList();
                    if (cells.Count > 0)
                    {
                        var formattedRow = string.Join("  •  ", cells);
                        MarkdownBlocks.Add(new MarkdownBlock
                        {
                            Type = MarkdownBlockType.Table,
                            Text = formattedRow
                        });
                    }
                    continue;
                }

                // Check image markdown ![alt](url)
                var imgMatch = Regex.Match(line, @"!\[(.*?)\]\((https?://.*?)\)");
                if (imgMatch.Success)
                {
                    var alt = imgMatch.Groups[1].Value;
                    var url = imgMatch.Groups[2].Value;
                    var block = new MarkdownBlock
                    {
                        Type = MarkdownBlockType.Image,
                        Text = CleanMarkdown(alt),
                        ImageUrl = url,
                        IsBadge = CheckIsBadge(url, alt)
                    };
                    MarkdownBlocks.Add(block);
                    _ = Task.Run(async () =>
                    {
                        var bmp = await _imageCacheService.LoadImageAsync(url);
                        if (bmp != null)
                        {
                            Dispatcher.UIThread.Post(() => block.ImageBitmap = bmp);
                        }
                    });
                    continue;
                }

                // Check HTML img <img ... src="url" ...>
                var htmlImgMatch = Regex.Match(line, @"<img\s+[^>]*src=[""'](https?://[^""']+)[""'][^>]*>", RegexOptions.IgnoreCase);
                if (htmlImgMatch.Success)
                {
                    var url = htmlImgMatch.Groups[1].Value;
                    var block = new MarkdownBlock
                    {
                        Type = MarkdownBlockType.Image,
                        Text = "",
                        ImageUrl = url,
                        IsBadge = CheckIsBadge(url, "")
                    };
                    MarkdownBlocks.Add(block);
                    _ = Task.Run(async () =>
                    {
                        var bmp = await _imageCacheService.LoadImageAsync(url);
                        if (bmp != null)
                        {
                            Dispatcher.UIThread.Post(() => block.ImageBitmap = bmp);
                        }
                    });
                    continue;
                }

                var cleaned = CleanMarkdown(line);
                if (string.IsNullOrWhiteSpace(cleaned)) continue;

                // Headings
                if (line.StartsWith("### "))
                {
                    MarkdownBlocks.Add(new MarkdownBlock { Type = MarkdownBlockType.Heading3, Text = CleanMarkdown(line[4..]) });
                }
                else if (line.StartsWith("## "))
                {
                    MarkdownBlocks.Add(new MarkdownBlock { Type = MarkdownBlockType.Heading2, Text = CleanMarkdown(line[3..]) });
                }
                else if (line.StartsWith("# "))
                {
                    MarkdownBlocks.Add(new MarkdownBlock { Type = MarkdownBlockType.Heading1, Text = CleanMarkdown(line[2..]) });
                }
                else if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    MarkdownBlocks.Add(new MarkdownBlock { Type = MarkdownBlockType.Bullet, Text = CleanMarkdown(line[2..]) });
                }
                else
                {
                    MarkdownBlocks.Add(new MarkdownBlock { Type = MarkdownBlockType.Paragraph, Text = cleaned });
                }
            }
        }

        private static bool CheckIsBadge(string url, string alt)
        {
            var lowerUrl = url.ToLowerInvariant();
            var lowerAlt = alt.ToLowerInvariant();

            return lowerUrl.Contains("badge") ||
                   lowerUrl.Contains("shield") ||
                   lowerUrl.Contains("discord") ||
                   lowerUrl.Contains("cf.jsdelivr") ||
                   lowerUrl.Contains("curseforge") ||
                   lowerUrl.Contains("button") ||
                   lowerUrl.Contains("kofi") ||
                   lowerUrl.Contains("patreon") ||
                   lowerUrl.Contains("donate") ||
                   lowerAlt.Contains("badge") ||
                   lowerAlt.Contains("discord");
        }

        private static string CleanMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            // Strip HTML tags like <p align="...">, </p>, <br>, <a>, etc.
            var res = Regex.Replace(text, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
            res = Regex.Replace(res, @"<[^>]+>", "");

            // Decode HTML entities
            res = res.Replace("&nbsp;", " ")
                     .Replace("&amp;", "&")
                     .Replace("&lt;", "<")
                     .Replace("&gt;", ">")
                     .Replace("&quot;", "\"")
                     .Replace("&#39;", "'");

            // Clean markdown links [text](url) -> text
            res = Regex.Replace(res, @"\[(.*?)\]\((.*?)\)", "$1");
            res = res.Replace("**", "").Replace("__", "").Replace("`", "");
            return res.Trim();
        }

        [RelayCommand]
        public async Task InstallVersionAsync(ModrinthVersion? version)
        {
            var targetVer = version ?? SelectedVersion;
            if (targetVer == null) return;

            if (IsModpack)
            {
                if (_modpackImportService == null)
                {
                    StatusText = "Сервис импорта модпаков недоступен.";
                    return;
                }

                IsInstalling = true;
                StatusText = $"Скачивание сборки {Project.Title} ({targetVer.VersionNumber})...";
                _globalProgressService?.StartTask("Установка сборки", $"Загрузка {Project.Title}...", canCancel: false);
                _globalProgressService?.UpdateProgress(10, $"Подготовка к установке {Project.Title}...");

                try
                {
                    var mrpackFile = targetVer.Files.FirstOrDefault(f => f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                        ?? targetVer.Files.FirstOrDefault()
                        ?? throw new InvalidOperationException($"Не найден файл сборки (.mrpack) для {Project.Title}.");

                    var tempDir = Path.Combine(Path.GetTempPath(), "ServantMrpacks");
                    Directory.CreateDirectory(tempDir);
                    var tempPath = Path.Combine(tempDir, mrpackFile.Filename);

                    StatusText = $"Скачивание .mrpack ({mrpackFile.Filename})...";
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
                            StatusText = p.status;
                            _globalProgressService?.UpdateProgress(p.percent, p.status);
                        });
                    });

                    var newProfile = await _modpackImportService.ImportMrpackAsync(tempPath, progress);
                    if ((string.IsNullOrEmpty(newProfile.CustomIconPath) || !File.Exists(newProfile.CustomIconPath)) && !string.IsNullOrEmpty(Project.IconUrl))
                    {
                        try
                        {
                            var gameDir = _profileManager.GetProfileGameDir(newProfile);
                            var targetIcon = Path.Combine(gameDir, "icon.png");
                            var iconBytes = await _httpClient.GetByteArrayAsync(Project.IconUrl);
                            await File.WriteAllBytesAsync(targetIcon, iconBytes);
                            newProfile.CustomIconPath = targetIcon;
                            _profileManager.SaveProfiles();
                        }
                        catch { }
                    }
                    _profileManager.SetActiveProfile(newProfile);

                    StatusText = $"Сборка '{newProfile.Name}' успешно установлена!";
                    _globalProgressService?.FinishTask($"Сборка '{newProfile.Name}' успешно установлена!");
                    ModInstalled?.Invoke();
                }
                catch (Exception ex)
                {
                    StatusText = $"Ошибка установки сборки: {ex.Message}";
                    _globalProgressService?.FinishTask($"Ошибка установки: {ex.Message}");
                }
                finally
                {
                    IsInstalling = false;
                }
                return;
            }

            if (_targetProfile == null)
            {
                StatusText = "Не выбрана активная сборка для установки мода.";
                return;
            }

            IsInstalling = true;
            StatusText = $"Установка {Project.Title} ({targetVer.VersionNumber}) и зависимостей...";

            try
            {
                var modsDir = _profileManager.GetProfileModsDir(_targetProfile);
                var loader = _targetProfile.LoaderType == LoaderType.Fabric ? "fabric" : null;
                await _modrinthService.InstallModWithDependenciesAsync(targetVer, modsDir, _targetProfile.GameVersion, loader);
                StatusText = $"Мод {Project.Title} и зависимости успешно установлены!";
                ModInstalled?.Invoke();
            }
            catch (Exception ex)
            {
                StatusText = $"Ошибка установки: {ex.Message}";
            }
            finally
            {
                IsInstalling = false;
            }
        }

        [RelayCommand]
        private void Close()
        {
            CloseRequested?.Invoke();
        }
    }
}
