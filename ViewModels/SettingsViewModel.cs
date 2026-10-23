using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServantLauncher.Models;
using ServantLauncher.Services;
using ServantLauncher.Services.PluginSystem;

namespace ServantLauncher.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IProfileManager _profileManager;
        private readonly IJavaService _javaService;
        private readonly IDiscordRpcService? _discordRpcService;
        private readonly IThemeService _themeService;
        private readonly IPluginManager _pluginManager;
        private readonly IAutoUpdateService? _autoUpdateService;
        private readonly IServerPackUpdateService? _serverPackUpdateService;

        public event Action? CloseRequested;

        public IThemeService ThemeService => _themeService;
        public IPluginManager PluginManager => _pluginManager;
        public ThemeConfig Theme => _themeService.CurrentTheme;

        [RelayCommand]
        private void CloseDialog()
        {
            CloseRequested?.Invoke();
        }

        [ObservableProperty]
        private int _selectedCategoryIndex = 0;

        [RelayCommand]
        public void SelectCategory(object? parameter)
        {
            if (parameter != null && int.TryParse(parameter.ToString(), out int index))
            {
                SelectedCategoryIndex = index;
            }
        }

        [ObservableProperty]
        private int _defaultRamMb;

        [ObservableProperty]
        private string _customJavaPath = string.Empty;

        [ObservableProperty]
        private int _javaMinRamMb = 640;

        [ObservableProperty]
        private int _javaMaxRamMb = 4096;

        [ObservableProperty]
        private int _javaPermGenMb = 128;

        [ObservableProperty]
        private bool _skipJavaCheck = true;

        [ObservableProperty]
        private bool _skipJavaInstallPrompt = false;

        [ObservableProperty]
        private bool _autoDetectJava = true;

        [ObservableProperty]
        private bool _autoDownloadMojangJava = true;

        [ObservableProperty]
        private bool _warnLowMemory = true;

        [ObservableProperty]
        private string _customJvmArgs = string.Empty;

        [ObservableProperty]
        private string _javaCheckStatus = string.Empty;

        [ObservableProperty]
        private bool _isJavaValid;

        [ObservableProperty]
        private string _serverIp = "play.servant.su";

        [ObservableProperty]
        private bool _enableDiscordRpc;

        [ObservableProperty]
        private string _discordClientId = string.Empty;

        [ObservableProperty]
        private bool _discordRpcShowPage = true;

        [ObservableProperty]
        private bool _discordRpcShowProfile = true;

        [ObservableProperty]
        private bool _discordRpcShowTime = true;

        // Resolution
        [ObservableProperty]
        private bool _useFullScreen;

        [ObservableProperty]
        private int _gameWindowWidth;

        [ObservableProperty]
        private int _gameWindowHeight;

        // Automation
        [ObservableProperty]
        private string _globalPreLaunchCommand = string.Empty;

        [ObservableProperty]
        private string _globalPostLaunchCommand = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsActionMinimizeToTray))]
        [NotifyPropertyChangedFor(nameof(IsActionKeepOpen))]
        [NotifyPropertyChangedFor(nameof(IsActionClose))]
        private LauncherActionOnStart _actionOnStart;

        public bool IsActionMinimizeToTray
        {
            get => ActionOnStart == LauncherActionOnStart.MinimizeToTray;
            set { if (value) ActionOnStart = LauncherActionOnStart.MinimizeToTray; }
        }

        public bool IsActionKeepOpen
        {
            get => ActionOnStart == LauncherActionOnStart.KeepOpen;
            set { if (value) ActionOnStart = LauncherActionOnStart.KeepOpen; }
        }

        public bool IsActionClose
        {
            get => ActionOnStart == LauncherActionOnStart.Close;
            set { if (value) ActionOnStart = LauncherActionOnStart.Close; }
        }

        [ObservableProperty]
        private bool _closeLauncherOnStart;

        [ObservableProperty]
        private bool _openConsoleOnLaunch;

        [ObservableProperty]
        private string _ramUsageText = string.Empty;

        [ObservableProperty]
        private string _memoryOptimizationStatus = string.Empty;

        public bool TrimMemoryInBackground
        {
            get => _profileManager.Config.TrimMemoryInBackground;
            set
            {
                _profileManager.Config.TrimMemoryInBackground = value;
                _profileManager.SaveConfig();
                OnPropertyChanged();
            }
        }

        public void UpdateRamUsage()
        {
            long mb = MemoryOptimizer.GetCurrentWorkingSetMb();
            RamUsageText = $"{mb} МБ";
        }

        [RelayCommand]
        public void OptimizeMemoryNow()
        {
            long before = MemoryOptimizer.GetCurrentWorkingSetMb();
            MemoryOptimizer.TrimWorkingSet();
            long after = MemoryOptimizer.GetCurrentWorkingSetMb();
            UpdateRamUsage();
            long saved = Math.Max(0, before - after);
            MemoryOptimizationStatus = saved > 0
                ? $"Память освобождена! Сэкономлено ~{saved} МБ (текущее: {after} МБ)"
                : $"Память оптимизирована (текущее: {after} МБ)";
        }

        [ObservableProperty]
        private string _launcherCurrentVersion = $"v{AutoUpdateService.CurrentVersion}";

        [ObservableProperty]
        private string _launcherUpdateStatus = "Актуальная версия";

        [ObservableProperty]
        private bool _isCheckingLauncherUpdate;

        [ObservableProperty]
        private string _serverPackCurrentVersion = "1.0.0";

        [ObservableProperty]
        private string _serverPackUpdateStatus = "Проверка не выполнялась";

        [ObservableProperty]
        private bool _isCheckingServerPackUpdate;

        [RelayCommand]
        public async Task CheckLauncherUpdateAsync()
        {
            if (IsCheckingLauncherUpdate) return;
            IsCheckingLauncherUpdate = true;
            LauncherUpdateStatus = "Проверка наличия обновлений...";

            try
            {
                var service = _autoUpdateService ?? App.AutoUpdateServiceInstance;
                if (service == null)
                {
                    LauncherUpdateStatus = "Служба обновлений недоступна";
                    return;
                }

                var (success, hasUpdate, newVersion, message) = await service.CheckWithDetailsAsync(true);
                LauncherUpdateStatus = message;
            }
            catch (Exception ex)
            {
                LauncherUpdateStatus = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsCheckingLauncherUpdate = false;
            }
        }

        [RelayCommand]
        public async Task CheckServerPackUpdateAsync()
        {
            if (IsCheckingServerPackUpdate) return;
            IsCheckingServerPackUpdate = true;
            ServerPackUpdateStatus = "Проверка сборки сервера...";

            try
            {
                var service = _serverPackUpdateService ?? App.ServerPackUpdateServiceInstance;
                if (service == null)
                {
                    ServerPackUpdateStatus = "Служба сборки недоступна";
                    return;
                }

                var (success, hasUpdate, newVersion, message) = await service.CheckWithDetailsAsync(true);
                ServerPackUpdateStatus = message;

                var serverProfile = _profileManager.Profiles.FirstOrDefault(p => p.IsServerProfile);
                if (serverProfile != null && !string.IsNullOrEmpty(serverProfile.ServerPackVersion))
                {
                    ServerPackCurrentVersion = serverProfile.ServerPackVersion;
                }
            }
            catch (Exception ex)
            {
                ServerPackUpdateStatus = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsCheckingServerPackUpdate = false;
            }
        }

        [ObservableProperty]
        private JavaInstallation? _selectedJava;

        public ObservableCollection<JavaInstallation> DetectedJavas { get; } = new();

        public SettingsViewModel(
            IProfileManager profileManager,
            IJavaService javaService,
            IDiscordRpcService? discordRpcService = null,
            IThemeService? themeService = null,
            IPluginManager? pluginManager = null,
            IAutoUpdateService? autoUpdateService = null,
            IServerPackUpdateService? serverPackUpdateService = null)
        {
            _profileManager = profileManager;
            _javaService = javaService;
            _discordRpcService = discordRpcService;
            _themeService = themeService ?? App.ThemeServiceInstance ?? new ThemeService();
            _pluginManager = pluginManager ?? App.PluginManagerInstance ?? new PluginManager();
            _autoUpdateService = autoUpdateService ?? App.AutoUpdateServiceInstance;
            _serverPackUpdateService = serverPackUpdateService ?? App.ServerPackUpdateServiceInstance;

            var activeServerProfile = _profileManager.Profiles.FirstOrDefault(p => p.IsServerProfile);
            if (activeServerProfile != null && !string.IsNullOrEmpty(activeServerProfile.ServerPackVersion))
            {
                ServerPackCurrentVersion = activeServerProfile.ServerPackVersion;
            }

            DefaultRamMb = _profileManager.Config.DefaultRamMb;
            CustomJavaPath = _profileManager.Config.CustomJavaPath ?? string.Empty;
            JavaMinRamMb = _profileManager.Config.JavaMinRamMb > 0 ? _profileManager.Config.JavaMinRamMb : 640;
            JavaMaxRamMb = _profileManager.Config.JavaMaxRamMb > 0 ? _profileManager.Config.JavaMaxRamMb : _profileManager.Config.DefaultRamMb;
            JavaPermGenMb = _profileManager.Config.JavaPermGenMb > 0 ? _profileManager.Config.JavaPermGenMb : 128;
            SkipJavaCheck = _profileManager.Config.SkipJavaCheck;
            SkipJavaInstallPrompt = _profileManager.Config.SkipJavaInstallPrompt;
            AutoDetectJava = _profileManager.Config.AutoDetectJava;
            AutoDownloadMojangJava = _profileManager.Config.AutoDownloadMojangJava;
            WarnLowMemory = _profileManager.Config.WarnLowMemory;
            CustomJvmArgs = _profileManager.Config.CustomJvmArgs ?? string.Empty;
            ServerIp = _profileManager.Config.ServerIp;
            ActionOnStart = _profileManager.Config.ActionOnStart;
            CloseLauncherOnStart = _profileManager.Config.CloseLauncherOnGameStart;
            OpenConsoleOnLaunch = _profileManager.Config.OpenConsoleOnLaunch;
            EnableDiscordRpc = _profileManager.Config.EnableDiscordRpc;
            DiscordClientId = _profileManager.Config.DiscordClientId ?? string.Empty;
            DiscordRpcShowPage = _profileManager.Config.DiscordRpcShowPage;
            DiscordRpcShowProfile = _profileManager.Config.DiscordRpcShowProfile;
            DiscordRpcShowTime = _profileManager.Config.DiscordRpcShowTime;

            UseFullScreen = _profileManager.Config.UseFullScreen;
            GameWindowWidth = _profileManager.Config.GameWindowWidth > 0 ? _profileManager.Config.GameWindowWidth : 854;
            GameWindowHeight = _profileManager.Config.GameWindowHeight > 0 ? _profileManager.Config.GameWindowHeight : 480;

            GlobalPreLaunchCommand = _profileManager.Config.GlobalPreLaunchCommand ?? string.Empty;
            GlobalPostLaunchCommand = _profileManager.Config.GlobalPostLaunchCommand ?? string.Empty;

            LoadJavas();
            SyncSlidersFromTheme();
            UpdateRamUsage();
        }

        partial void OnUseFullScreenChanged(bool value)
        {
            _profileManager.Config.UseFullScreen = value;
            _profileManager.SaveConfig();
        }

        partial void OnGameWindowWidthChanged(int value)
        {
            _profileManager.Config.GameWindowWidth = value;
            _profileManager.SaveConfig();
        }

        partial void OnGameWindowHeightChanged(int value)
        {
            _profileManager.Config.GameWindowHeight = value;
            _profileManager.SaveConfig();
        }

        partial void OnGlobalPreLaunchCommandChanged(string value)
        {
            _profileManager.Config.GlobalPreLaunchCommand = value;
            _profileManager.SaveConfig();
        }

        partial void OnGlobalPostLaunchCommandChanged(string value)
        {
            _profileManager.Config.GlobalPostLaunchCommand = value;
            _profileManager.SaveConfig();
        }

        public int SelectedBackgroundModeIndex
        {
            get => Theme.BackgroundMode switch
            {
                BackgroundMode.Slideshow => 0,
                BackgroundMode.CustomSlideshow => 1,
                BackgroundMode.SingleImage => 2,
                BackgroundMode.AnimatedGif => 2,
                BackgroundMode.None => 3,
                _ => 0
            };
            set
            {
                var mode = value switch
                {
                    0 => BackgroundMode.Slideshow,
                    1 => BackgroundMode.CustomSlideshow,
                    2 => (!string.IsNullOrEmpty(Theme.CustomBackgroundImagePath) && Theme.CustomBackgroundImagePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? BackgroundMode.AnimatedGif : BackgroundMode.SingleImage),
                    3 => BackgroundMode.None,
                    _ => BackgroundMode.Slideshow
                };
                Theme.BackgroundMode = mode;
                _themeService.SetBackgroundMode(mode);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsModeDefaultScreenshots));
                OnPropertyChanged(nameof(IsModeCustomSlideshow));
                OnPropertyChanged(nameof(IsModeSinglePhotoOrGif));
                OnPropertyChanged(nameof(IsModeNone));
                OnPropertyChanged(nameof(Theme));
            }
        }

        public bool IsModeDefaultScreenshots => SelectedBackgroundModeIndex == 0;
        public bool IsModeCustomSlideshow => SelectedBackgroundModeIndex == 1;
        public bool IsModeSinglePhotoOrGif => SelectedBackgroundModeIndex == 2;
        public bool IsModeNone => SelectedBackgroundModeIndex == 3;

        public string ServerScreenshotsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".servantlauncher", "server_screenshots");

        [RelayCommand]
        public void OpenServerScreenshotsFolder()
        {
            try
            {
                if (!Directory.Exists(ServerScreenshotsPath))
                {
                    Directory.CreateDirectory(ServerScreenshotsPath);
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = ServerScreenshotsPath,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        // Live Slider and Numeric input properties
        public int GlassOpacityPercent
        {
            get => (int)Math.Round(Theme.GlassOpacity * 100);
            set
            {
                var clamped = Math.Clamp(value, 30, 100);
                Theme.GlassOpacity = clamped / 100.0;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Theme));
            }
        }

        public int DimOverlayPercent
        {
            get => (int)Math.Round(Theme.DimOverlayOpacity * 100);
            set
            {
                var clamped = Math.Clamp(value, 0, 95);
                Theme.DimOverlayOpacity = clamped / 100.0;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Theme));
            }
        }

        public int SlideshowIntervalSeconds
        {
            get => Theme.SlideshowIntervalSeconds;
            set
            {
                var clamped = Math.Clamp(value, 3, 60);
                Theme.SlideshowIntervalSeconds = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Theme));
            }
        }

        // Dedicated Color Target Index & Hex for Color Picker
        private int _selectedColorTargetIndex = 0;
        public int SelectedColorTargetIndex
        {
            get => _selectedColorTargetIndex;
            set
            {
                if (SetProperty(ref _selectedColorTargetIndex, value))
                {
                    OnPropertyChanged(nameof(CurrentTargetColorHex));
                    SyncSlidersFromTheme();
                }
            }
        }

        public string CurrentTargetColorHex
        {
            get => SelectedColorTargetIndex switch
            {
                0 => Theme.BtnPrimaryHex,
                1 => Theme.BtnPurpleHex,
                2 => Theme.BtnOutlineBgHex,
                3 => Theme.BtnDangerHex,
                4 => Theme.AccentColorHex,
                5 => Theme.CardBgHex,
                6 => Theme.BgDarkHex,
                7 => Theme.HeaderBgHex,
                8 => Theme.BorderAccentHex,
                _ => Theme.BtnPrimaryHex
            };
            set
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                switch (SelectedColorTargetIndex)
                {
                    case 0:
                        Theme.BtnPrimaryHex = value;
                        Theme.BtnPrimaryHoverHex = value;
                        break;
                    case 1:
                        Theme.BtnPurpleHex = value;
                        Theme.BtnPurpleHoverHex = value;
                        break;
                    case 2:
                        Theme.BtnOutlineBgHex = value;
                        Theme.BtnOutlineHoverBgHex = value;
                        break;
                    case 3:
                        Theme.BtnDangerHex = value;
                        Theme.BtnDangerHoverHex = value;
                        break;
                    case 4:
                        Theme.AccentColorHex = value;
                        break;
                    case 5:
                        Theme.CardBgHex = value;
                        break;
                    case 6:
                        Theme.BgDarkHex = value;
                        break;
                    case 7:
                        Theme.HeaderBgHex = value;
                        Theme.NavBgHex = value;
                        break;
                    case 8:
                        Theme.BorderAccentHex = value;
                        break;
                }
                Theme.SelectedPreset = ThemePreset.Custom;
                _themeService.ApplyTheme(Theme);
                OnPropertyChanged();
                OnPropertyChanged(nameof(Theme));
            }
        }

        [RelayCommand]
        public void ApplyPreset(string presetName)
        {
            if (Enum.TryParse<ThemePreset>(presetName, true, out var preset))
            {
                _themeService.ApplyPreset(preset);
                OnPropertyChanged(nameof(Theme));
            }
        }

        [RelayCommand]
        public void SetAccentColor(string hex)
        {
            _themeService.SetAccentColor(hex);
            OnPropertyChanged(nameof(Theme));
        }

        [RelayCommand]
        public void SetElementColor(string parameter)
        {
            if (string.IsNullOrEmpty(parameter)) return;
            var parts = parameter.Split(':');
            if (parts.Length != 2) return;
            var element = parts[0].Trim();
            var hex = parts[1].Trim();

            switch (element.ToLowerInvariant())
            {
                case "primary":
                    Theme.BtnPrimaryHex = hex;
                    Theme.BtnPrimaryHoverHex = hex;
                    break;
                case "primaryhover":
                    Theme.BtnPrimaryHoverHex = hex;
                    break;
                case "purple":
                    Theme.BtnPurpleHex = hex;
                    Theme.BtnPurpleHoverHex = hex;
                    break;
                case "purplehover":
                    Theme.BtnPurpleHoverHex = hex;
                    break;
                case "outline":
                    Theme.BtnOutlineBgHex = hex;
                    break;
                case "outlinehover":
                    Theme.BtnOutlineHoverBgHex = hex;
                    break;
                case "danger":
                    Theme.BtnDangerHex = hex;
                    Theme.BtnDangerHoverHex = hex;
                    break;
                case "dangerhover":
                    Theme.BtnDangerHoverHex = hex;
                    break;
                case "accent":
                    Theme.AccentColorHex = hex;
                    break;
                case "card":
                    Theme.CardBgHex = hex;
                    break;
                case "bg":
                    Theme.BgDarkHex = hex;
                    break;
            }
            Theme.SelectedPreset = ThemePreset.Custom;
            _themeService.ApplyTheme(Theme);
            SyncSlidersFromTheme();
            OnPropertyChanged(nameof(Theme));
        }

        [ObservableProperty]
        private int _colorSliderR = 124;

        [ObservableProperty]
        private int _colorSliderG = 58;

        [ObservableProperty]
        private int _colorSliderB = 237;

        [ObservableProperty]
        private string _colorHexPreview = "#7C3AED";

        private bool _isUpdatingColorInternally;

        partial void OnColorSliderRChanged(int value) => OnColorSliderChanged();
        partial void OnColorSliderGChanged(int value) => OnColorSliderChanged();
        partial void OnColorSliderBChanged(int value) => OnColorSliderChanged();

        private void OnColorSliderChanged()
        {
            if (_isUpdatingColorInternally) return;
            string hex = $"#{ColorSliderR:X2}{ColorSliderG:X2}{ColorSliderB:X2}";
            ColorHexPreview = hex;
            ApplyColorToSelectedTarget(hex);
        }

        public void SyncSlidersFromTheme()
        {
            _isUpdatingColorInternally = true;
            try
            {
                string hex = SelectedColorTargetIndex switch
                {
                    0 => Theme.BtnPrimaryHex,
                    1 => Theme.BtnPurpleHex,
                    2 => Theme.BtnOutlineBgHex,
                    3 => Theme.BtnDangerHex,
                    4 => Theme.AccentColorHex,
                    5 => Theme.CardBgHex,
                    6 => Theme.BgDarkHex,
                    _ => Theme.BtnPrimaryHex
                };

                if (string.IsNullOrWhiteSpace(hex)) hex = "#7C3AED";
                ColorHexPreview = hex;
                try
                {
                    var c = Avalonia.Media.Color.Parse(hex);
                    ColorSliderR = c.R;
                    ColorSliderG = c.G;
                    ColorSliderB = c.B;
                }
                catch { }
            }
            finally
            {
                _isUpdatingColorInternally = false;
            }
        }

        private void ApplyColorToSelectedTarget(string hex)
        {
            switch (SelectedColorTargetIndex)
            {
                case 0:
                    Theme.BtnPrimaryHex = hex;
                    Theme.BtnPrimaryHoverHex = LightenHex(hex, 1.15f);
                    break;
                case 1:
                    Theme.BtnPurpleHex = hex;
                    Theme.BtnPurpleHoverHex = LightenHex(hex, 1.15f);
                    break;
                case 2:
                    Theme.BtnOutlineBgHex = hex;
                    Theme.BtnOutlineHoverBgHex = LightenHex(hex, 1.25f);
                    break;
                case 3:
                    Theme.BtnDangerHex = hex;
                    Theme.BtnDangerHoverHex = LightenHex(hex, 1.15f);
                    break;
                case 4:
                    Theme.AccentColorHex = hex;
                    Theme.BrandPrimaryHex = hex;
                    break;
                case 5:
                    Theme.CardBgHex = hex;
                    Theme.CardBgHoverHex = LightenHex(hex, 1.15f);
                    break;
                case 6:
                    Theme.BgDarkHex = hex;
                    break;
            }
            Theme.SelectedPreset = ThemePreset.Custom;
            _themeService.ApplyTheme(Theme);
            OnPropertyChanged(nameof(Theme));
        }

        private static string LightenHex(string hex, float factor)
        {
            try
            {
                var c = Avalonia.Media.Color.Parse(hex);
                byte r = (byte)Math.Clamp((int)(c.R * factor), 0, 255);
                byte g = (byte)Math.Clamp((int)(c.G * factor), 0, 255);
                byte b = (byte)Math.Clamp((int)(c.B * factor), 0, 255);
                return $"#{r:X2}{g:X2}{b:X2}";
            }
            catch { return hex; }
        }

        [RelayCommand]
        public void SetSliderColorHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return;
            try
            {
                var c = Avalonia.Media.Color.Parse(hex);
                _isUpdatingColorInternally = true;
                ColorSliderR = c.R;
                ColorSliderG = c.G;
                ColorSliderB = c.B;
                ColorHexPreview = hex;
                _isUpdatingColorInternally = false;
                ApplyColorToSelectedTarget(hex);
            }
            catch { }
        }

        [RelayCommand]
        public void SetBackgroundMode(string modeName)
        {
            if (Enum.TryParse<BackgroundMode>(modeName, true, out var mode))
            {
                _themeService.SetBackgroundMode(mode);
                OnPropertyChanged(nameof(Theme));
                OnPropertyChanged(nameof(SelectedBackgroundModeIndex));
                OnPropertyChanged(nameof(IsModeDefaultScreenshots));
                OnPropertyChanged(nameof(IsModeCustomSlideshow));
                OnPropertyChanged(nameof(IsModeSinglePhotoOrGif));
                OnPropertyChanged(nameof(IsModeNone));
            }
        }

        [RelayCommand]
        public void SaveTheme()
        {
            _themeService.ApplyTheme(Theme);
            _themeService.Save();
            OnPropertyChanged(nameof(Theme));
        }

        [RelayCommand]
        public void RemoveSlideshowImage(string path)
        {
            if (Theme.SlideshowImages.Contains(path))
            {
                Theme.SlideshowImages.Remove(path);
                _themeService.Save();
                _themeService.ApplyTheme(Theme);
            }
        }

        [RelayCommand]
        public void MoveSlideshowImageUp(string path)
        {
            int idx = Theme.SlideshowImages.IndexOf(path);
            if (idx > 0)
            {
                Theme.SlideshowImages.Move(idx, idx - 1);
                _themeService.Save();
                _themeService.ApplyTheme(Theme);
            }
        }

        [RelayCommand]
        public void MoveSlideshowImageDown(string path)
        {
            int idx = Theme.SlideshowImages.IndexOf(path);
            if (idx >= 0 && idx < Theme.SlideshowImages.Count - 1)
            {
                Theme.SlideshowImages.Move(idx, idx + 1);
                _themeService.Save();
                _themeService.ApplyTheme(Theme);
            }
        }

        [RelayCommand]
        public void OpenPluginsFolder()
        {
            try
            {
                Directory.CreateDirectory(_pluginManager.PluginsDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{_pluginManager.PluginsDirectory}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        [RelayCommand]
        public void RefreshPlugins()
        {
            _pluginManager.ScanAndLoadPlugins();
        }

        [RelayCommand]
        public void TogglePlugin(object? parameter)
        {
            if (parameter is PluginItemViewModel plugin)
            {
                _pluginManager.TogglePlugin(plugin, !plugin.IsEnabled);
            }
        }

        [RelayCommand]
        public void OpenPluginGuide()
        {
            try
            {
                var candidatePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PLUGINS_GUIDE.md"),
                    Path.Combine(Environment.CurrentDirectory, "PLUGINS_GUIDE.md"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "PLUGINS_GUIDE.md")
                };

                foreach (var path in candidatePaths)
                {
                    var full = Path.GetFullPath(path);
                    if (File.Exists(full))
                    {
                        Process.Start(new ProcessStartInfo { FileName = full, UseShellExecute = true });
                        return;
                    }
                }
            }
            catch { }
        }

        private void LoadJavas()
        {
            DetectedJavas.Clear();
            var list = _javaService.FindInstalledJavas();
            foreach (var j in list)
            {
                DetectedJavas.Add(j);
            }
            SelectedJava = DetectedJavas.FirstOrDefault();
        }

        partial void OnDefaultRamMbChanged(int value)
        {
            _profileManager.Config.DefaultRamMb = value;
            _profileManager.SaveConfig();
        }

        partial void OnCustomJavaPathChanged(string value)
        {
            _profileManager.Config.CustomJavaPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _profileManager.SaveConfig();
        }

        partial void OnJavaMinRamMbChanged(int value)
        {
            _profileManager.Config.JavaMinRamMb = value;
            _profileManager.SaveConfig();
        }

        partial void OnJavaMaxRamMbChanged(int value)
        {
            _profileManager.Config.JavaMaxRamMb = value;
            _profileManager.Config.DefaultRamMb = value;
            _profileManager.SaveConfig();
        }

        partial void OnJavaPermGenMbChanged(int value)
        {
            _profileManager.Config.JavaPermGenMb = value;
            _profileManager.SaveConfig();
        }

        partial void OnSkipJavaCheckChanged(bool value)
        {
            _profileManager.Config.SkipJavaCheck = value;
            _profileManager.SaveConfig();
        }

        partial void OnSkipJavaInstallPromptChanged(bool value)
        {
            _profileManager.Config.SkipJavaInstallPrompt = value;
            _profileManager.SaveConfig();
        }

        partial void OnAutoDetectJavaChanged(bool value)
        {
            _profileManager.Config.AutoDetectJava = value;
            _profileManager.SaveConfig();
        }

        partial void OnAutoDownloadMojangJavaChanged(bool value)
        {
            _profileManager.Config.AutoDownloadMojangJava = value;
            _profileManager.SaveConfig();
        }

        partial void OnWarnLowMemoryChanged(bool value)
        {
            _profileManager.Config.WarnLowMemory = value;
            _profileManager.SaveConfig();
        }

        partial void OnCustomJvmArgsChanged(string value)
        {
            _profileManager.Config.CustomJvmArgs = value;
            _profileManager.SaveConfig();
        }

        partial void OnServerIpChanged(string value)
        {
            _profileManager.Config.ServerIp = value.Trim();
            _profileManager.SaveConfig();
        }

        partial void OnActionOnStartChanged(LauncherActionOnStart value)
        {
            _profileManager.Config.ActionOnStart = value;
            _profileManager.Config.CloseLauncherOnGameStart = (value == LauncherActionOnStart.Close);
            _profileManager.SaveConfig();
        }

        partial void OnCloseLauncherOnStartChanged(bool value)
        {
            _profileManager.Config.CloseLauncherOnGameStart = value;
            if (value) _profileManager.Config.ActionOnStart = LauncherActionOnStart.Close;
            _profileManager.SaveConfig();
        }

        partial void OnOpenConsoleOnLaunchChanged(bool value)
        {
            _profileManager.Config.OpenConsoleOnLaunch = value;
            _profileManager.SaveConfig();
        }

        partial void OnEnableDiscordRpcChanged(bool value)
        {
            _profileManager.Config.EnableDiscordRpc = value;
            _profileManager.SaveConfig();
            SyncDiscordRpc();
        }

        partial void OnDiscordClientIdChanged(string value)
        {
            _profileManager.Config.DiscordClientId = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _profileManager.SaveConfig();
            SyncDiscordRpc();
        }

        partial void OnDiscordRpcShowPageChanged(bool value)
        {
            _profileManager.Config.DiscordRpcShowPage = value;
            _profileManager.SaveConfig();
            SyncDiscordRpc();
        }

        partial void OnDiscordRpcShowProfileChanged(bool value)
        {
            _profileManager.Config.DiscordRpcShowProfile = value;
            _profileManager.SaveConfig();
            SyncDiscordRpc();
        }

        partial void OnDiscordRpcShowTimeChanged(bool value)
        {
            _profileManager.Config.DiscordRpcShowTime = value;
            _profileManager.SaveConfig();
            SyncDiscordRpc();
        }

        private void SyncDiscordRpc()
        {
            _discordRpcService?.UpdateConfig(
                EnableDiscordRpc,
                DiscordClientId,
                DiscordRpcShowPage,
                DiscordRpcShowProfile,
                DiscordRpcShowTime
            );
        }

        partial void OnSelectedJavaChanged(JavaInstallation? value)
        {
            if (value != null)
            {
                CustomJavaPath = value.Path;
            }
        }

        [ObservableProperty]
        private bool _isDownloadingJava;

        [ObservableProperty]
        private string _javaDownloadStatus = string.Empty;

        [RelayCommand]
        private async Task DownloadJava21()
        {
            if (IsDownloadingJava) return;
            IsDownloadingJava = true;
            JavaDownloadStatus = "Подготовка к загрузке Java 21...";
            try
            {
                var progress = new Progress<(double percent, string status)>(p =>
                {
                    JavaDownloadStatus = p.status;
                });
                var path = await _javaService.DownloadAndInstallJavaAsync(21, progress);
                LoadJavas();
                CustomJavaPath = path;
                JavaDownloadStatus = "Java 21 успешно установлена!";
            }
            catch (Exception ex)
            {
                JavaDownloadStatus = $"Ошибка загрузки Java 21: {ex.Message}";
            }
            finally
            {
                IsDownloadingJava = false;
            }
        }

        [RelayCommand]
        private async Task DownloadJava25()
        {
            if (IsDownloadingJava) return;
            IsDownloadingJava = true;
            JavaDownloadStatus = "Подготовка к загрузке Java 25...";
            try
            {
                var progress = new Progress<(double percent, string status)>(p =>
                {
                    JavaDownloadStatus = p.status;
                });
                var path = await _javaService.DownloadAndInstallJavaAsync(25, progress);
                LoadJavas();
                CustomJavaPath = path;
                JavaDownloadStatus = "Java 25 успешно установлена!";
            }
            catch (Exception ex)
            {
                JavaDownloadStatus = $"Ошибка загрузки Java 25: {ex.Message}";
            }
            finally
            {
                IsDownloadingJava = false;
            }
        }

        [RelayCommand]
        public void AutoFindJava()
        {
            LoadJavas();
            var best = DetectedJavas.OrderByDescending(j => j.MajorVersion).FirstOrDefault();
            if (best != null)
            {
                SelectedJava = best;
                CustomJavaPath = best.Path;
                JavaCheckStatus = $"Найдена и выбрана Java {best.MajorVersion}: {best.Path}";
                IsJavaValid = true;
            }
            else
            {
                JavaCheckStatus = "В системе не обнаружены установленные версии Java.";
                IsJavaValid = false;
            }
        }

        [RelayCommand]
        public void CheckJavaSettings()
        {
            var path = CustomJavaPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                JavaCheckStatus = "Путь к исполняемому файлу Java не указан.";
                IsJavaValid = false;
                return;
            }

            if (!File.Exists(path))
            {
                JavaCheckStatus = $"Файл не существует: {path}";
                IsJavaValid = false;
                return;
            }

            if (_javaService.ValidateJavaPath(path, out int majorVer))
            {
                JavaCheckStatus = $"✓ Java {majorVer} успешно проверена и готова к работе!";
                IsJavaValid = true;
            }
            else
            {
                JavaCheckStatus = "✗ Указанный файл не является корректным исполняемым файлом Java (javaw.exe).";
                IsJavaValid = false;
            }
        }

        [RelayCommand]
        private void OpenLauncherFolder()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{_profileManager.BaseLauncherDir}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
