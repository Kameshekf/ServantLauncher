using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServantLauncher.Models;
using ServantLauncher.Services;
using ServantLauncher.Services.PluginSystem;

namespace ServantLauncher.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IMojangService _mojangService;
        private readonly IFabricService _fabricService;
        private readonly IModrinthService _modrinthService;
        private readonly IAccountService _accountService;
        private readonly IProfileManager _profileManager;
        private readonly IJavaService _javaService;
        private readonly ISkinService _skinService;
        private readonly IModpackImportService _modpackImportService;
        private readonly IImageCacheService _imageCacheService;
        private readonly IGameLauncherService _launcherService;
        private readonly IDiscordRpcService _discordRpcService;

        private CancellationTokenSource? _launchCts;

        // Services
        public IThemeService ThemeService { get; }
        public IGlobalProgressService GlobalProgress { get; }
        public IPluginManager PluginManager { get; }

        // Child ViewModels
        public HomeViewModel HomeVm { get; }
        public ProfilesViewModel ProfilesVm { get; }
        public ModrinthViewModel ModrinthVm { get; }
        public SkinsViewModel SkinsVm { get; }
        public AccountsViewModel AccountsVm { get; }
        public SettingsViewModel SettingsVm { get; }
        public IScreenshotService ScreenshotService { get; }
        public ScreenshotsViewModel ScreenshotsVm { get; }

        [ObservableProperty]
        private int _selectedPageIndex = 0; // 0 = Главная, 1 = Сборки, 2 = Моды, 3 = Скины, 4 = Скриншоты

        [ObservableProperty]
        private bool _isLogoAnimationEnabled = true;

        partial void OnSelectedPageIndexChanged(int value)
        {
            var pageName = value switch
            {
                0 => "Главная",
                1 => "Сборки",
                2 => "Каталог",
                3 => "Скины",
                4 => "Скриншоты",
                _ => "В лаунчере"
            };
            if (!_launcherService.IsGameRunning)
            {
                _discordRpcService.SetPresence("Servant Launcher BETA", $"Раздел: {pageName}", false);
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsModalDialogOpen))]
        private bool _isAccountsDialogOpen;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsModalDialogOpen))]
        private bool _isSettingsDialogOpen;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsModalDialogOpen))]
        private bool _isCrashDialogOpen;

        public bool IsModalDialogOpen => IsAccountsDialogOpen || IsSettingsDialogOpen || IsCrashDialogOpen || (SkinsVm?.IsAddSkinDialogOpen ?? false);

        [ObservableProperty]
        private CrashDialogViewModel? _crashDialogVm;

        [ObservableProperty]
        private bool _isLaunching;

        [ObservableProperty]
        private double _launchProgressPercent;

        [ObservableProperty]
        private string _launchStatusText = string.Empty;

        public GameProfile? ActiveProfile => _profileManager.ActiveProfile;
        public AccountModel? ActiveAccount => _accountService.ActiveAccount;
        public bool IsMicrosoftAccountActive => ActiveAccount?.Type == AccountType.Microsoft;

        public event Action? MinimizeRequested;
        public event Action? MaximizeRequested;
        public event Action? CloseAppRequested;
        public event Action? HideToTrayRequested;
        public event Action? RestoreFromTrayRequested;

        public MainViewModel(
            IMojangService mojangService,
            IFabricService fabricService,
            IModrinthService modrinthService,
            IAccountService accountService,
            IProfileManager profileManager,
            IJavaService javaService,
            IGameLauncherService launcherService,
            ISkinService? skinService = null,
            IModpackImportService? modpackImportService = null,
            IImageCacheService? imageCacheService = null,
            IDiscordRpcService? discordRpcService = null,
            IThemeService? themeService = null,
            IGlobalProgressService? globalProgressService = null,
            IPluginManager? pluginManager = null)
        {
            _mojangService = mojangService;
            _fabricService = fabricService;
            _modrinthService = modrinthService;
            _accountService = accountService;
            _profileManager = profileManager;
            _javaService = javaService;
            _launcherService = launcherService;
            _skinService = skinService ?? new SkinService();
            _modpackImportService = modpackImportService ?? new ModpackImportService(profileManager);
            _imageCacheService = imageCacheService ?? new ImageCacheService();
            _discordRpcService = discordRpcService ?? new DiscordRpcService(profileManager.Config.EnableDiscordRpc);

            ThemeService = themeService ?? App.ThemeServiceInstance ?? new ThemeService();
            IsLogoAnimationEnabled = ThemeService.CurrentTheme.EnableLogoAnimation;
            ThemeService.ThemeChanged += t =>
            {
                IsLogoAnimationEnabled = t.EnableLogoAnimation;
            };
            GlobalProgress = globalProgressService ?? App.GlobalProgressInstance ?? new GlobalProgressService();
            PluginManager = pluginManager ?? App.PluginManagerInstance ?? new PluginManager();

            GlobalProgress.CancelRequested += CancelLaunch;

            HomeVm = new HomeViewModel(_profileManager, ThemeService);
            ProfilesVm = new ProfilesViewModel(_profileManager, _mojangService, _fabricService, _modrinthService, _imageCacheService, _launcherService, _modpackImportService);
            ModrinthVm = new ModrinthViewModel(_modrinthService, _profileManager, _imageCacheService, _modpackImportService, GlobalProgress);
            SkinsVm = new SkinsViewModel(_skinService, _accountService, _profileManager);
            AccountsVm = new AccountsViewModel(_accountService);
            SettingsVm = new SettingsViewModel(_profileManager, _javaService, _discordRpcService, ThemeService, PluginManager);
            ScreenshotService = new ScreenshotService(_profileManager);
            ScreenshotsVm = new ScreenshotsViewModel(ScreenshotService, _profileManager);

            // Initialize Discord RPC
            _discordRpcService.Initialize();

            // Hook launch requests
            HomeVm.PlayRequested += () => _ = LaunchActiveGameAsync();
            HomeVm.CancelLaunchRequested += CancelLaunch;
            HomeVm.CloseGameRequested += () =>
            {
                try
                {
                    _launcherService.KillGame();
                    UpdateAllLaunchState(false, false, 0, string.Empty);
                }
                catch { }
            };
            HomeVm.CreateProfileRequested += () =>
            {
                SelectedPageIndex = 1;
                ProfilesVm.OpenCreateDialogCommand.Execute(null);
            };
            HomeVm.NavigateToProfilesRequested += () =>
            {
                SelectedPageIndex = 1;
            };

            ProfilesVm.PlayActiveRequested += () => _ = LaunchActiveGameAsync();
            ProfilesVm.CancelLaunchRequested += CancelLaunch;
            ProfilesVm.CloseGameRequested += () =>
            {
                try
                {
                    _launcherService.KillGame();
                    UpdateAllLaunchState(false, false, 0, string.Empty);
                }
                catch { }
            };
            ProfilesVm.OpenModrinthCatalogRequested += () =>
            {
                SelectedPageIndex = 2;
                ModrinthVm.SetCatalogMode(isModpackMode: true);
            };

            ModrinthVm.BackRequested += () =>
            {
                SelectedPageIndex = 1;
            };

            _profileManager.ActiveProfileChanged += () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    OnPropertyChanged(nameof(ActiveProfile));
                    HomeVm.RefreshProfiles();
                    ProfilesVm.RefreshProfiles();
                });
            };
            _accountService.AccountsChanged += () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    OnPropertyChanged(nameof(ActiveAccount));
                    OnPropertyChanged(nameof(IsMicrosoftAccountActive));
                });
            };

            _launcherService.GameExited += (s, code) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateAllLaunchState(false, false, 0, string.Empty);
                    HomeVm.RefreshProfiles();
                    ProfilesVm.RefreshProfiles();
                    _discordRpcService.SetPresence("Servant Launcher BETA", "В лаунчере", false);
                    RestoreFromTrayRequested?.Invoke();
                    if (code != 0)
                    {
                        var recentLogs = _launcherService.GetRecentLogs();
                        ShowCrashDialog($"Игра аварийно завершилась (код {code})", recentLogs, code);
                    }
                });
            };

            AccountsVm.CloseRequested += () => IsAccountsDialogOpen = false;
            SettingsVm.CloseRequested += () => IsSettingsDialogOpen = false;
        }

        public void ShowCrashDialog(string errorMessage, string stackTrace = "", int exitCode = -1)
        {
            if (exitCode == 0 || _launchCts?.IsCancellationRequested == true)
                return;

            CrashDialogVm = new CrashDialogViewModel(
                errorMessage,
                stackTrace,
                exitCode,
                ActiveProfile,
                _profileManager,
                _modrinthService,
                javaService: _javaService);
            CrashDialogVm.CloseRequested += () => IsCrashDialogOpen = false;
            CrashDialogVm.OpenConsoleRequested += () =>
            {
                IsCrashDialogOpen = false;
                var prof = ActiveProfile;
                if (prof != null)
                {
                    SelectedPageIndex = 1;
                    ProfilesVm.OpenProfileDetails(prof);
                    if (ProfilesVm.ActiveDetailsVm != null)
                    {
                        ProfilesVm.ActiveDetailsVm.SelectedTabIndex = 2; // Console
                        ProfilesVm.ActiveDetailsVm.ConsoleVm.EnsureCrashLog(errorMessage, stackTrace);
                    }
                }
            };
            IsCrashDialogOpen = true;
        }

        [RelayCommand]
        public void NavigateTo(int pageIndex)
        {
            SelectedPageIndex = pageIndex;
        }

        [RelayCommand]
        public void OpenAccountsDialog()
        {
            IsAccountsDialogOpen = true;
        }

        [RelayCommand]
        public void CloseAccountsDialog()
        {
            IsAccountsDialogOpen = false;
        }

        [RelayCommand]
        public void OpenSettingsDialog()
        {
            IsSettingsDialogOpen = true;
        }

        [RelayCommand]
        public void CloseSettingsDialog()
        {
            IsSettingsDialogOpen = false;
        }

        private void UpdateAllLaunchState(bool isLaunching, bool isRunning, double percent, string status)
        {
            IsLaunching = isLaunching;
            LaunchProgressPercent = percent;
            LaunchStatusText = status;
            HomeVm.UpdateLaunchProgress(isLaunching, isRunning, percent, status);
            ProfilesVm.UpdateLaunchProgress(isLaunching, isRunning, percent, status);

            if (isLaunching)
            {
                if (!GlobalProgress.IsActive)
                {
                    GlobalProgress.StartTask("Запуск игры", status, true);
                }
                GlobalProgress.ReportProgress(percent, status);
            }
            else if (isRunning)
            {
                GlobalProgress.FinishTask("Minecraft запущен!");
            }
            else if (GlobalProgress.IsActive)
            {
                GlobalProgress.FinishTask(status);
            }
        }

        private bool _isCancelling;

        [RelayCommand]
        public void CancelLaunch()
        {
            if (_isCancelling) return;
            _isCancelling = true;
            try
            {
                _launchCts?.Cancel();
                if (_launcherService.IsGameRunning)
                {
                    _launcherService.KillGame();
                }
            }
            catch { }
            finally
            {
                GlobalProgress.FinishTask("Запуск отменен");
                UpdateAllLaunchState(false, false, 0, "Запуск отменен");
                _isCancelling = false;
            }
        }

        [RelayCommand]
        public async Task LaunchActiveGameAsync()
        {
            if (IsLaunching || _launcherService.IsGameRunning)
            {
                CancelLaunch();
                return;
            }

            var profile = _profileManager.ActiveProfile;
            var account = _accountService.ActiveAccount;

            if (profile == null)
            {
                UpdateAllLaunchState(false, false, 0, "Ошибка: не выбрана сборка!");
                return;
            }

            if (account == null)
            {
                IsAccountsDialogOpen = true;
                return;
            }

            GlobalProgress.StartTask($"Запуск {profile.Name}", "Подготовка файлов Minecraft...", true);
            UpdateAllLaunchState(true, false, 0, "Подготовка файлов Minecraft...");
            _launchCts = new CancellationTokenSource();

            var progress = new Progress<(double percent, string status)>(p =>
            {
                if (_launcherService.IsGameRunning) return;
                UpdateAllLaunchState(true, false, p.percent, p.status);
            });

            try
            {
                await _launcherService.LaunchGameAsync(profile, account, progress, _launchCts.Token);
                UpdateAllLaunchState(false, true, 100, "Игра запущена!");
                _discordRpcService.SetPresence("В игре Minecraft", $"Сборка: {profile.Name} ({profile.GameVersion})", true);

                var action = _profileManager.Config.ActionOnStart;
                if (_profileManager.Config.CloseLauncherOnGameStart) action = LauncherActionOnStart.Close;

                if (action == LauncherActionOnStart.MinimizeToTray)
                {
                    HideToTrayRequested?.Invoke();
                    if (_profileManager.Config.TrimMemoryInBackground)
                    {
                        MemoryOptimizer.TrimWorkingSet();
                    }
                }
                else if (action == LauncherActionOnStart.Close)
                {
                    CloseAppRequested?.Invoke();
                }
                else
                {
                    if (_profileManager.Config.OpenConsoleOnLaunch)
                    {
                        SelectedPageIndex = 1;
                        ProfilesVm.OpenProfileDetails(profile);
                        if (ProfilesVm.ActiveDetailsVm != null)
                        {
                            ProfilesVm.ActiveDetailsVm.SelectedTabIndex = 2; // Console tab
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                UpdateAllLaunchState(false, false, 0, "Запуск отменен");
            }
            catch (Exception ex)
            {
                if (_launchCts?.IsCancellationRequested == true || ex is OperationCanceledException)
                {
                    UpdateAllLaunchState(false, false, 0, "Запуск отменен");
                }
                else
                {
                    UpdateAllLaunchState(false, false, 0, $"Ошибка: {ex.Message}");
                    ShowCrashDialog(ex.Message, ex.ToString(), -1);
                }
            }
            finally
            {
                if (!_launcherService.IsGameRunning)
                {
                    UpdateAllLaunchState(false, false, 0, string.Empty);
                }
                HomeVm.RefreshProfiles();
                ProfilesVm.RefreshProfiles();
            }
        }

        [RelayCommand]
        public void Minimize() => MinimizeRequested?.Invoke();

        [RelayCommand]
        public void Maximize() => MaximizeRequested?.Invoke();

        [RelayCommand]
        public void CloseApp() => CloseAppRequested?.Invoke();
    }
}
