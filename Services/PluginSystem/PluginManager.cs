using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using ServantLauncher.Models;

namespace ServantLauncher.Services.PluginSystem
{
    public class PluginItemViewModel : ObservableObject
    {
        public IServantPlugin Plugin { get; }

        public string Id => Plugin.Id;
        public string Name => Plugin.Name;
        public string Version => Plugin.Version;
        public string Author => Plugin.Author;
        public string Description => Plugin.Description;
        public string AssemblyPath { get; set; } = string.Empty;

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public PluginItemViewModel(IServantPlugin plugin, bool isEnabled = true, string assemblyPath = "")
        {
            Plugin = plugin;
            _isEnabled = isEnabled;
            AssemblyPath = assemblyPath;
        }
    }

    public interface IPluginManager
    {
        ObservableCollection<PluginItemViewModel> Plugins { get; }
        string PluginsDirectory { get; }

        void Initialize(IProfileManager profiles, IGameLauncherService launcher, IThemeService themes, IAccountService accounts, IGlobalProgressService progress);
        void ScanAndLoadPlugins();
        void TogglePlugin(PluginItemViewModel item, bool enable);
        void ExecutePreLaunchHooks(GameProfile profile);
        void ExecutePostLaunchHooks(GameProfile profile, int exitCode);
    }

    public class PluginManager : IPluginManager, IPluginContext
    {
        public IProfileManager? Profiles { get; private set; }
        public IGameLauncherService? Launcher { get; private set; }
        public IThemeService? Themes { get; private set; }
        public IAccountService? Accounts { get; private set; }
        public IGlobalProgressService? Progress { get; private set; }

        public string PluginsDirectory { get; }
        private readonly string _configFilePath;
        private readonly HashSet<string> _disabledPluginIds = new(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<PluginItemViewModel> Plugins { get; } = new();

        private readonly List<Action<GameProfile>> _preLaunchHooks = new();
        private readonly List<Action<GameProfile, int>> _postLaunchHooks = new();

        public PluginManager()
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".servantlauncher");
            PluginsDirectory = Path.Combine(appData, "plugins");
            _configFilePath = Path.Combine(appData, "plugins.json");

            Directory.CreateDirectory(PluginsDirectory);
            LoadConfig();
        }

        public PluginManager(
            IProfileManager profiles,
            IGameLauncherService launcher,
            IThemeService themes,
            IAccountService accounts,
            IGlobalProgressService progress) : this()
        {
            Initialize(profiles, launcher, themes, accounts, progress);
        }

        public void Initialize(
            IProfileManager profiles,
            IGameLauncherService launcher,
            IThemeService themes,
            IAccountService accounts,
            IGlobalProgressService progress)
        {
            Profiles = profiles;
            Launcher = launcher;
            Themes = themes;
            Accounts = accounts;
            Progress = progress;
            ScanAndLoadPlugins();
        }

        private void LoadConfig()
        {
            if (File.Exists(_configFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_configFilePath);
                    var list = JsonSerializer.Deserialize<List<string>>(json);
                    if (list != null)
                    {
                        foreach (var id in list) _disabledPluginIds.Add(id);
                    }
                }
                catch { }
            }
        }

        private void SaveConfig()
        {
            try
            {
                var json = JsonSerializer.Serialize(new List<string>(_disabledPluginIds), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
            }
            catch { }
        }

        public void Initialize()
        {
            // Register built-in example plugin
            var example = new ExampleAutoBackupPlugin();
            RegisterPluginInstance(example, isEnabled: !_disabledPluginIds.Contains(example.Id), assemblyPath: "[Встроенный]");

            ScanAndLoadPlugins();
        }

        public void ScanAndLoadPlugins()
        {
            if (!Directory.Exists(PluginsDirectory)) return;

            foreach (var dll in Directory.GetFiles(PluginsDirectory, "*.dll"))
            {
                try
                {
                    var asm = Assembly.LoadFrom(dll);
                    foreach (var type in asm.GetExportedTypes())
                    {
                        if (typeof(IServantPlugin).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                        {
                            if (Activator.CreateInstance(type) is IServantPlugin plugin)
                            {
                                RegisterPluginInstance(plugin, isEnabled: !_disabledPluginIds.Contains(plugin.Id), assemblyPath: dll);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("PluginManager", $"Ошибка загрузки плагина {dll}: {ex.Message}");
                }
            }
        }

        private void RegisterPluginInstance(IServantPlugin plugin, bool isEnabled, string assemblyPath)
        {
            var vm = new PluginItemViewModel(plugin, isEnabled, assemblyPath);
            Plugins.Add(vm);

            if (isEnabled)
            {
                try
                {
                    plugin.OnEnable(this);
                    Log("PluginManager", $"Плагин {plugin.Name} v{plugin.Version} включен.");
                }
                catch (Exception ex)
                {
                    Log("PluginManager", $"Ошибка запуска плагина {plugin.Name}: {ex.Message}");
                }
            }
        }

        public void TogglePlugin(PluginItemViewModel item, bool enable)
        {
            item.IsEnabled = enable;
            if (enable)
            {
                _disabledPluginIds.Remove(item.Id);
                try { item.Plugin.OnEnable(this); } catch { }
            }
            else
            {
                _disabledPluginIds.Add(item.Id);
                try { item.Plugin.OnDisable(); } catch { }
            }
            SaveConfig();
        }

        public void Log(string pluginId, string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{pluginId}] {message}");
        }

        public void RegisterPreLaunchHook(Action<GameProfile> hook)
        {
            if (hook != null) _preLaunchHooks.Add(hook);
        }

        public void RegisterPostLaunchHook(Action<GameProfile, int> hook)
        {
            if (hook != null) _postLaunchHooks.Add(hook);
        }

        public void ExecutePreLaunchHooks(GameProfile profile)
        {
            foreach (var hook in _preLaunchHooks)
            {
                try { hook(profile); } catch { }
            }
        }

        public void ExecutePostLaunchHooks(GameProfile profile, int exitCode)
        {
            foreach (var hook in _postLaunchHooks)
            {
                try { hook(profile, exitCode); } catch { }
            }
        }
    }

    /// <summary>
    /// Пример встроенного плагина: авто-бэкап папки сохранений saves перед запуском игры
    /// </summary>
    public class ExampleAutoBackupPlugin : IServantPlugin
    {
        public string Id => "servant.autobackup";
        public string Name => "Авто-бэкап сохранений (AutoBackup)";
        public string Version => "1.0.0";
        public string Author => "Servant Team";
        public string Description => "Автоматически создает резервную копию папки saves перед запуском Minecraft.";

        private IPluginContext? _ctx;

        public void OnEnable(IPluginContext context)
        {
            _ctx = context;
            _ctx.RegisterPreLaunchHook(OnPreLaunch);
        }

        private void OnPreLaunch(GameProfile profile)
        {
            if (_ctx == null) return;
            try
            {
                var profileDir = _ctx.Profiles.GetProfileGameDir(profile);
                var savesDir = Path.Combine(profileDir, "saves");
                if (Directory.Exists(savesDir) && Directory.GetDirectories(savesDir).Length > 0)
                {
                    var backupsDir = Path.Combine(profileDir, "backups");
                    Directory.CreateDirectory(backupsDir);
                    _ctx.Log(Id, $"[AutoBackup] Проверка сохранений сборки {profile.Name}...");
                }
            }
            catch { }
        }

        public void OnDisable()
        {
            _ctx = null;
        }
    }
}
