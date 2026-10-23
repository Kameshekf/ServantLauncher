using System;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ServantLauncher.Services;
using ServantLauncher.Services.PluginSystem;
using ServantLauncher.ViewModels;

namespace ServantLauncher
{
    public partial class App : Application
    {
        public static IThemeService? ThemeServiceInstance { get; private set; }
        public static IGlobalProgressService? GlobalProgressInstance { get; private set; }
        public static IPluginManager? PluginManagerInstance { get; private set; }
        public static IAutoUpdateService? AutoUpdateServiceInstance { get; private set; }
        public static IServerPackUpdateService? ServerPackUpdateServiceInstance { get; private set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var httpClient = HttpHelper.CreateHttpClient();
                
                var themeService = new ThemeService();
                ThemeServiceInstance = themeService;

                var globalProgress = new GlobalProgressService();
                GlobalProgressInstance = globalProgress;

                var pluginManager = new PluginManager();
                PluginManagerInstance = pluginManager;

                var mojangService = new MojangService(httpClient);
                var fabricService = new FabricService(httpClient);
                var modrinthService = new ModrinthService(httpClient);
                var accountService = new AccountService(httpClient);
                var profileManager = new ProfileManager();
                var javaService = new JavaService();
                var skinService = new SkinService(httpClient);
                var modpackImportService = new ModpackImportService(profileManager, httpClient);
                var launcherService = new GameLauncherService(mojangService, fabricService, javaService, profileManager, null, pluginManager, themeService);

                var autoUpdateService = new AutoUpdateService(httpClient, globalProgress);
                AutoUpdateServiceInstance = autoUpdateService;
                autoUpdateService.StartPeriodicCheck();

                var serverPackUpdateService = new ServerPackUpdateService(autoUpdateService, profileManager, modpackImportService, globalProgress, httpClient);
                ServerPackUpdateServiceInstance = serverPackUpdateService;
                serverPackUpdateService.StartPeriodicCheck();

                pluginManager.Initialize(profileManager, launcherService, themeService, accountService, globalProgress);

                var mainVm = new MainViewModel(
                    mojangService,
                    fabricService,
                    modrinthService,
                    accountService,
                    profileManager,
                    javaService,
                    launcherService,
                    skinService,
                    modpackImportService,
                    null,
                    null,
                    themeService,
                    globalProgress,
                    pluginManager
                );

                desktop.MainWindow = new MainWindow(mainVm);
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
