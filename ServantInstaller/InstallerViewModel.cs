using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ServantInstaller
{
    public partial class InstallerViewModel : ObservableObject
    {
        [ObservableProperty]
        private int _currentStep = 0; // 0 = Config, 1 = Installing, 2 = Finished

        [ObservableProperty]
        private string _installDirectory;

        [ObservableProperty]
        private int _allocatedRamMb = 4096;

        public string RamFormattedText => $"{AllocatedRamMb} МБ ({(AllocatedRamMb / 1024.0):0.#} ГБ)";

        partial void OnAllocatedRamMbChanged(int value)
        {
            OnPropertyChanged(nameof(RamFormattedText));
        }

        [ObservableProperty]
        private bool _createDesktopShortcut = true;

        [ObservableProperty]
        private bool _createStartMenuShortcut = true;

        [ObservableProperty]
        private bool _launchAfterInstall = true;

        [ObservableProperty]
        private bool _preInstallJava21 = true;

        [ObservableProperty]
        private double _installProgressPercent;

        [ObservableProperty]
        private string _statusText = string.Empty;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private bool _hasError;

        public event Action? CloseRequested;

        public InstallerViewModel()
        {
            _installDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "ServantLauncher");
        }

        [RelayCommand]
        public void SetRam(int mb)
        {
            AllocatedRamMb = mb;
        }

        [RelayCommand]
        public async Task StartInstallAsync()
        {
            CurrentStep = 1;
            HasError = false;
            ErrorMessage = string.Empty;
            InstallProgressPercent = 0;
            StatusText = "Подготовка к установке...";

            try
            {
                await Task.Run(async () =>
                {
                    // 1. Create target directories
                    StatusText = "Создание папок установки...";
                    InstallProgressPercent = 10;
                    Directory.CreateDirectory(InstallDirectory);

                    var baseAppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".servantlauncher");
                    Directory.CreateDirectory(baseAppDataDir);

                    // 2. Extract payload files
                    StatusText = "Распаковка компонентов Servant Launcher (.NET 9 + библиотеки)...";
                    InstallProgressPercent = 20;

                    var assembly = Assembly.GetExecutingAssembly();
                    var resourceName = System.Linq.Enumerable.FirstOrDefault(assembly.GetManifestResourceNames(), n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase)) ?? "ServantSetup.payload.zip";
                    using var stream = assembly.GetManifestResourceStream(resourceName);

                    if (stream != null)
                    {
                        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                        int totalEntries = archive.Entries.Count;
                        int extracted = 0;

                        foreach (var entry in archive.Entries)
                        {
                            var destPath = Path.Combine(InstallDirectory, entry.FullName);
                            if (string.IsNullOrEmpty(entry.Name))
                            {
                                Directory.CreateDirectory(destPath);
                                continue;
                            }

                            var destDir = Path.GetDirectoryName(destPath);
                            if (!string.IsNullOrEmpty(destDir))
                            {
                                Directory.CreateDirectory(destDir);
                            }

                            entry.ExtractToFile(destPath, true);
                            extracted++;
                            InstallProgressPercent = 20.0 + ((double)extracted / totalEntries * 40.0);
                        }
                    }
                    else
                    {
                        // Development fallback (copy from publish dir if exists)
                        var devPublish = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "publish");
                        if (Directory.Exists(devPublish))
                        {
                            CopyDirectory(devPublish, InstallDirectory);
                        }
                        InstallProgressPercent = 60;
                    }

                    // 3. Save initial configuration
                    StatusText = "Сохранение настроек лаунчера...";
                    InstallProgressPercent = 65;

                    var configPath = Path.Combine(baseAppDataDir, "config.json");
                    var configNode = new JsonObject
                    {
                        ["DefaultRamMb"] = AllocatedRamMb,
                        ["ServerIp"] = "play.servant-ru.online",
                        ["EnableDiscordRpc"] = true,
                        ["DiscordClientId"] = "1342600293393698888",
                        ["DiscordRpcShowPage"] = true,
                        ["DiscordRpcShowProfile"] = true,
                        ["DiscordRpcShowTime"] = true,
                        ["ActionOnStart"] = 0
                    };

                    await File.WriteAllTextAsync(configPath, configNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

                    // 4. Download and configure Java 21 if selected
                    if (PreInstallJava21)
                    {
                        StatusText = "Проверка и загрузка среды выполнения Java 21 OpenJDK...";
                        InstallProgressPercent = 70;
                        await EnsureJava21InstalledAsync(baseAppDataDir);
                    }

                    // 5. Create Shortcuts
                    StatusText = "Создание ярлыков Windows...";
                    InstallProgressPercent = 90;

                    var exePath = Path.Combine(InstallDirectory, "ServantLauncher.exe");
                    var iconPath = Path.Combine(InstallDirectory, "Assets", "Icons", "app.ico");
                    if (!File.Exists(iconPath)) iconPath = exePath;

                    if (CreateDesktopShortcut)
                    {
                        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        var shortcutPath = Path.Combine(desktopPath, "Servant Launcher.lnk");
                        ShortcutHelper.CreateShortcut(shortcutPath, exePath, InstallDirectory, iconPath, "Servant Launcher — быстрый лаунчер Minecraft");
                    }

                    if (CreateStartMenuShortcut)
                    {
                        var startMenuPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                        var shortcutPath = Path.Combine(startMenuPrograms, "Servant Launcher.lnk");
                        ShortcutHelper.CreateShortcut(shortcutPath, exePath, InstallDirectory, iconPath, "Servant Launcher — быстрый лаунчер Minecraft");
                    }

                    // 6. Complete
                    InstallProgressPercent = 100;
                    StatusText = "Установка успешно завершена!";
                });

                CurrentStep = 2;
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"Ошибка установки: {ex.Message}";
                CurrentStep = 0;
            }
        }

        private async Task EnsureJava21InstalledAsync(string baseAppDataDir)
        {
            var targetDir = Path.Combine(baseAppDataDir, "runtimes", "java-21");
            var javaw = Path.Combine(targetDir, "bin", "javaw.exe");
            if (File.Exists(javaw)) return;

            Directory.CreateDirectory(Path.Combine(baseAppDataDir, "runtimes"));
            var tempZip = Path.Combine(baseAppDataDir, "runtimes", "java21_setup.zip");

            var urls = new[]
            {
                "https://api.adoptium.net/v3/binary/latest/21/ga/windows/x64/jre/hotspot/normal/eclipse",
                "https://aka.ms/download-jdk/microsoft-jdk-21.0.6-windows-x64.zip"
            };

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ServantInstaller/1.0");

            bool ok = false;
            foreach (var url in urls)
            {
                try
                {
                    using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    if (!resp.IsSuccessStatusCode) continue;

                    var total = resp.Content.Headers.ContentLength ?? -1L;
                    await using var inStream = await resp.Content.ReadAsStreamAsync();
                    await using var outStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);

                    var buf = new byte[65536];
                    long read = 0;
                    int n;
                    while ((n = await inStream.ReadAsync(buf, 0, buf.Length)) > 0)
                    {
                        await outStream.WriteAsync(buf.AsMemory(0, n));
                        read += n;
                        if (total > 0)
                        {
                            InstallProgressPercent = 70.0 + ((double)read / total * 18.0);
                        }
                    }

                    ok = true;
                    break;
                }
                catch { }
            }

            if (ok && File.Exists(tempZip))
            {
                StatusText = "Распаковка среды выполнения Java 21...";
                var extractTemp = Path.Combine(baseAppDataDir, "runtimes", "java21_temp");
                if (Directory.Exists(extractTemp)) Directory.Delete(extractTemp, true);

                try
                {
                    ZipFile.ExtractToDirectory(tempZip, extractTemp);
                }
                finally
                {
                    try { File.Delete(tempZip); } catch { }
                }

                var found = Directory.GetFiles(extractTemp, "javaw.exe", SearchOption.AllDirectories);
                if (found.Length > 0)
                {
                    var bin = Path.GetDirectoryName(found[0])!;
                    var root = Directory.GetParent(bin)!.FullName;
                    CopyDirectory(root, targetDir);
                }
                try { Directory.Delete(extractTemp, true); } catch { }
            }
        }

        [RelayCommand]
        public void LaunchAndExit()
        {
            var exePath = Path.Combine(InstallDirectory, "ServantLauncher.exe");
            if (File.Exists(exePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = InstallDirectory,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
            CloseRequested?.Invoke();
        }

        [RelayCommand]
        public void Cancel()
        {
            CloseRequested?.Invoke();
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var targetFilePath = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, targetFilePath, true);
            }
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var targetSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, targetSubDir);
            }
        }
    }
}
