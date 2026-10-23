using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ServantLauncher.Models;

namespace ServantLauncher.Services
{
    public class GameLogEventArgs : EventArgs
    {
        public string Text { get; set; } = string.Empty;
        public bool IsError { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public interface IGameLauncherService
    {
        bool IsGameRunning { get; }
        event EventHandler<GameLogEventArgs>? GameLogReceived;
        event EventHandler<int>? GameExited;

        Task LaunchGameAsync(GameProfile profile, AccountModel account, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default);
        void KillGame();
        string GetRecentLogs(int maxLines = 60);
    }

    public class GameLauncherService : IGameLauncherService
    {
        private readonly IMojangService _mojangService;
        private readonly IFabricService _fabricService;
        private readonly IJavaService _javaService;
        private readonly IProfileManager _profileManager;
        private readonly INbtServerListService _nbtServerListService;
        private readonly PluginSystem.IPluginManager? _pluginManager;
        private readonly IThemeService? _themeService;

        private Process? _runningProcess;
        private readonly List<string> _logHistory = new();
        private readonly object _logLock = new();

        public bool IsGameRunning => _runningProcess != null && !_runningProcess.HasExited;
        public event EventHandler<GameLogEventArgs>? GameLogReceived;
        public event EventHandler<int>? GameExited;

        public GameLauncherService(
            IMojangService mojangService,
            IFabricService fabricService,
            IJavaService javaService,
            IProfileManager profileManager,
            INbtServerListService? nbtServerListService = null,
            PluginSystem.IPluginManager? pluginManager = null,
            IThemeService? themeService = null)
        {
            _mojangService = mojangService;
            _fabricService = fabricService;
            _javaService = javaService;
            _profileManager = profileManager;
            _nbtServerListService = nbtServerListService ?? new NbtServerListService();
            _pluginManager = pluginManager;
            _themeService = themeService;
        }

        public async Task LaunchGameAsync(GameProfile profile, AccountModel account, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default)
        {
            if (IsGameRunning)
                throw new InvalidOperationException("Minecraft уже запущен!");

            var commonDir = _profileManager.CommonGameDir;
            var profileDir = _profileManager.GetProfileGameDir(profile);
            var versionsDir = Path.Combine(commonDir, "versions");
            var librariesDir = Path.Combine(commonDir, "libraries");
            var assetsDir = Path.Combine(commonDir, "assets");
            var nativesDir = Path.Combine(versionsDir, profile.GameVersion, "natives");

            Directory.CreateDirectory(profileDir);
            Directory.CreateDirectory(nativesDir);

            // Automatically inject Servant RU and EU servers into servers.dat
            _nbtServerListService.EnsureServantServersInDirectory(profileDir);

            // 1. Download/Verify Mojang Game Files (Vanilla client, libraries, assets)
            progress?.Report((10, $"Проверка игровых файлов Minecraft {profile.GameVersion}..."));
            await _mojangService.DownloadGameFilesAsync(profile.GameVersion, commonDir, progress, ct);

            // 2. Resolve Classpath & Main Class
            var classpathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string mainClass = "net.minecraft.client.main.Main";
            var extraJvmArgs = new List<string>();

            // Always add Vanilla libraries first
            var vanillaJsonPath = Path.Combine(versionsDir, profile.GameVersion, $"{profile.GameVersion}.json");
            if (File.Exists(vanillaJsonPath))
            {
                var vJsonText = await File.ReadAllTextAsync(vanillaJsonPath, ct);
                var vRoot = JsonNode.Parse(vJsonText);
                var vLibs = vRoot?["libraries"]?.AsArray();
                if (vLibs != null)
                {
                    foreach (var lib in vLibs)
                    {
                        var name = lib?["name"]?.GetValue<string>();
                        var path = lib?["downloads"]?["artifact"]?["path"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(path))
                        {
                            var full = Path.Combine(librariesDir, path.Replace('/', Path.DirectorySeparatorChar));
                            if (File.Exists(full))
                            {
                                var key = GetArtifactKey(name, full);
                                classpathMap[key] = full;
                            }
                        }
                    }
                }
            }

            // If Fabric, download and add Fabric libraries (overriding duplicate libs like ASM)
            if (profile.LoaderType == LoaderType.Fabric && !string.IsNullOrEmpty(profile.LoaderVersion))
            {
                progress?.Report((75, "Загрузка и проверка библиотек Fabric..."));
                await _fabricService.DownloadFabricLibrariesAsync(profile.GameVersion, profile.LoaderVersion, commonDir, progress, ct);

                var fJsonPath = await _fabricService.EnsureFabricProfileJsonAsync(profile.GameVersion, profile.LoaderVersion, versionsDir, ct);
                if (File.Exists(fJsonPath))
                {
                    var fJsonText = await File.ReadAllTextAsync(fJsonPath, ct);
                    var fRoot = JsonNode.Parse(fJsonText);
                    mainClass = fRoot?["mainClass"]?.GetValue<string>() ?? "net.fabricmc.loader.impl.launch.knot.KnotClient";

                    var fLibs = fRoot?["libraries"]?.AsArray();
                    if (fLibs != null)
                    {
                        foreach (var lib in fLibs)
                        {
                            var name = lib?["name"]?.GetValue<string>();
                            if (!string.IsNullOrEmpty(name))
                            {
                                var relPath = FabricService.MavenNameToPath(name);
                                var full = Path.Combine(librariesDir, relPath);
                                if (File.Exists(full))
                                {
                                    var key = GetArtifactKey(name, full);
                                    classpathMap[key] = full;
                                }
                            }
                        }
                    }

                    var jvmArgsArray = fRoot?["arguments"]?["jvm"]?.AsArray();
                    if (jvmArgsArray != null)
                    {
                        foreach (var arg in jvmArgsArray)
                        {
                            var s = arg?.GetValue<string>()?.Trim();
                            if (!string.IsNullOrEmpty(s))
                            {
                                extraJvmArgs.Add(s.Contains(' ') ? $"\"{s}\"" : s);
                            }
                        }
                    }
                }
            }

            // Client JAR
            var clientJar = Path.Combine(versionsDir, profile.GameVersion, $"{profile.GameVersion}.jar");
            if (File.Exists(clientJar))
            {
                classpathMap["net.minecraft:client"] = clientJar;
            }
            else
            {
                throw new FileNotFoundException($"Файл клиента Minecraft {profile.GameVersion}.jar не найден! Проверьте подключение к интернету или корректность версии {profile.GameVersion}.");
            }

            if (classpathMap.Count == 0)
            {
                throw new InvalidOperationException($"Не удалось загрузить библиотеки для Minecraft {profile.GameVersion}. Проверьте соединение с интернетом.");
            }

            var classpathList = classpathMap.Values.ToList();

            // 3. Resolve Java (auto-download OpenJDK 21/25/17/8 if missing)
            int requiredMajor = _javaService.GetRequiredJavaVersion(profile.GameVersion);
            string javaExe;

            if (!string.IsNullOrEmpty(profile.CustomJavaPath) && File.Exists(profile.CustomJavaPath))
            {
                javaExe = profile.CustomJavaPath;
            }
            else if (_profileManager.Config.AutoDetectJava)
            {
                javaExe = await _javaService.EnsureJavaInstalledAsync(profile.GameVersion, progress, ct);
            }
            else if (!string.IsNullOrEmpty(_profileManager.Config.CustomJavaPath) && File.Exists(_profileManager.Config.CustomJavaPath))
            {
                javaExe = _profileManager.Config.CustomJavaPath;
                if (_javaService.ValidateJavaPath(javaExe, out int customMajor) && customMajor >= 25 && requiredMajor <= 21)
                {
                    Log($"[Launcher] Выбранная Java '{javaExe}' (Java {customMajor}) несовместима с Minecraft {profile.GameVersion}. Выбираем совместимую Java {requiredMajor}...");
                    javaExe = await _javaService.EnsureJavaInstalledAsync(profile.GameVersion, progress, ct);
                }
            }
            else
            {
                javaExe = await _javaService.EnsureJavaInstalledAsync(profile.GameVersion, progress, ct);
            }

            int detectedMajor = 0;
            if (!string.IsNullOrEmpty(javaExe))
            {
                _javaService.ValidateJavaPath(javaExe, out detectedMajor);
            }

            // Check Java compatibility if not skipped
            if (!_profileManager.Config.SkipJavaCheck && !string.IsNullOrEmpty(javaExe))
            {
                if (detectedMajor <= 0)
                {
                    Log($"[Launcher] Внимание: Java по пути '{javaExe}' не прошла проверку совместимости.", isError: true);
                }
            }

            // 4. Build JVM Arguments and Game Arguments
            var assetIndex = profile.GameVersion;
            if (File.Exists(vanillaJsonPath))
            {
                try
                {
                    var vText = await File.ReadAllTextAsync(vanillaJsonPath, ct);
                    var vNode = JsonNode.Parse(vText);
                    assetIndex = vNode?["assetIndex"]?["id"]?.GetValue<string>() ?? profile.GameVersion;
                }
                catch { }
            }

            var classpathString = string.Join(";", classpathList.Select(p => p.Replace('\\', '/')));

            // Write arguments file for Java to prevent command line length limits
            // IMPORTANT: Must use UTF-8 WITHOUT BOM!
            var argsFilePath = Path.Combine(profileDir, "launch_args.txt");

            int maxRam = profile.AllocatedRamMb > 0 ? profile.AllocatedRamMb : (_profileManager.Config.JavaMaxRamMb > 0 ? _profileManager.Config.JavaMaxRamMb : _profileManager.Config.DefaultRamMb);
            int minRam = _profileManager.Config.JavaMinRamMb > 0 ? _profileManager.Config.JavaMinRamMb : 640;
            int permGen = _profileManager.Config.JavaPermGenMb > 0 ? _profileManager.Config.JavaPermGenMb : 128;

            if (maxRam <= 0) maxRam = 4096;
            if (minRam <= 0) minRam = 512;
            if (minRam > maxRam) minRam = maxRam;

            var argsLines = new List<string>
            {
                "-XX:+IgnoreUnrecognizedVMOptions",
                $"-Xmx{maxRam}M",
                $"-Xms{minRam}M"
            };

            // PermGen was removed in Java 8 (replaced by Metaspace). Only pass PermSize if using legacy Java 7 or older.
            if (detectedMajor > 0 && detectedMajor < 8 && permGen > 0)
            {
                argsLines.Add($"-XX:PermSize={permGen}M");
            }

            argsLines.Add($"-Djava.library.path={nativesDir.Replace('\\', '/')}");
            argsLines.Add("-Dorg.lwjgl.util.Debug=false");

            // Official Launcher Security Token for Servant Velocity / Server Verification
            try
            {
                const string secretKey = "SERVANT_LAUNCHER_AUTH_SECURE_TOKEN_2026_98F3A2B1";
                var playerUuid = !string.IsNullOrEmpty(account.Uuid) ? account.Uuid : Guid.NewGuid().ToString();
                var playerName = !string.IsNullOrEmpty(account.Username) ? account.Username : "Player";
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var salt = Guid.NewGuid().ToString("N");
                var payload = $"{playerUuid}:{playerName}:{timestamp}:{salt}";

                using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secretKey));
                var hashBytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
                var signature = Convert.ToHexString(hashBytes).ToLowerInvariant();
                var rawToken = $"{payload}:{signature}";
                var tokenBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(rawToken));

                argsLines.Add($"-Dservant.launcher.token={tokenBase64}");
                argsLines.Add($"-Dservant.launcher.session={salt}");
            }
            catch (Exception ex)
            {
                Log($"[Launcher] Предупреждение: Не удалось сформировать токен безопасности лаунчера: {ex.Message}");
            }

            foreach (var extra in extraJvmArgs)
            {
                argsLines.Add(extra);
            }

            if (!string.IsNullOrWhiteSpace(_profileManager.Config.CustomJvmArgs))
            {
                argsLines.AddRange(_profileManager.Config.CustomJvmArgs.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            }

            if (!string.IsNullOrWhiteSpace(profile.JvmArgs))
            {
                argsLines.AddRange(profile.JvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }

            argsLines.Add("-cp");
            argsLines.Add(classpathString);
            argsLines.Add(mainClass);

            // Game arguments
            argsLines.Add("--username");
            argsLines.Add(account.Username);
            argsLines.Add("--version");
            argsLines.Add(profile.GameVersion);
            argsLines.Add("--gameDir");
            argsLines.Add(profileDir.Replace('\\', '/'));
            argsLines.Add("--assetsDir");
            argsLines.Add(assetsDir.Replace('\\', '/'));
            argsLines.Add("--assetIndex");
            argsLines.Add(assetIndex);
            argsLines.Add("--uuid");
            argsLines.Add(account.Uuid);
            argsLines.Add("--accessToken");
            argsLines.Add(string.IsNullOrEmpty(account.AccessToken) ? "0" : account.AccessToken);
            argsLines.Add("--userType");
            argsLines.Add(account.Type == AccountType.Microsoft ? "msa" : "offline");
            argsLines.Add("--versionType");
            argsLines.Add("ServantLauncher");

            // Screen Resolution & Fullscreen (Global vs Local)
            bool isFullscreen = profile.UseGlobalResolution ? _profileManager.Config.UseFullScreen : profile.UseFullScreen;
            int windowWidth = profile.UseGlobalResolution ? _profileManager.Config.GameWindowWidth : profile.GameWindowWidth;
            int windowHeight = profile.UseGlobalResolution ? _profileManager.Config.GameWindowHeight : profile.GameWindowHeight;

            if (isFullscreen)
            {
                argsLines.Add("--fullscreen");
            }
            else if (windowWidth > 0 && windowHeight > 0)
            {
                argsLines.Add("--width");
                argsLines.Add(windowWidth.ToString());
                argsLines.Add("--height");
                argsLines.Add(windowHeight.ToString());
            }

            // Automation & Plugins: Pre-Launch
            try
            {
                _pluginManager?.ExecutePreLaunchHooks(profile);
            }
            catch { }

            var preCmd = !string.IsNullOrWhiteSpace(profile.PreLaunchCommand)
                ? profile.PreLaunchCommand
                : _profileManager.Config.GlobalPreLaunchCommand;

            if (!string.IsNullOrWhiteSpace(preCmd))
            {
                await RunAutomationScriptAsync(preCmd, profile, account, profileDir, javaExe);
            }

            // Write without BOM
            var utf8NoBom = new UTF8Encoding(false);
            await File.WriteAllLinesAsync(argsFilePath, argsLines, utf8NoBom, ct);

            // 5. Start Java Process
            progress?.Report((100, "Запуск Minecraft..."));

            var psi = new ProcessStartInfo
            {
                FileName = javaExe,
                Arguments = $"@{argsFilePath}",
                WorkingDirectory = profileDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Log($"[Launcher] Запуск игры: {javaExe}");
            Log($"[Launcher] Рабочая папка: {profileDir}");
            Log($"[Launcher] Сборка: {profile.Name} (Minecraft {profile.GameVersion}, {profile.LoaderType})");
            Log($"[Launcher] Класс запуска: {mainClass}");
            Log($"[Launcher] Всего библиотек в classpath: {classpathList.Count}");
            Log($"[Launcher] Игрок: {account.Username} ({account.Type})");

            _runningProcess = Process.Start(psi);
            if (_runningProcess == null)
                throw new InvalidOperationException("Не удалось запустить процесс Java.");

            _themeService?.NotifyGameRunningState(true);

            profile.LastPlayed = DateTime.UtcNow;
            _profileManager.UpdateProfile(profile);

            _runningProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null) Log(e.Data, false);
            };

            _runningProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) Log(e.Data, true);
            };

            _runningProcess.BeginOutputReadLine();
            _runningProcess.BeginErrorReadLine();

            var gameStartTime = DateTime.UtcNow;
            SetupProcessListeners(profile, account, profileDir, javaExe, gameStartTime);
        }

        private volatile bool _isUserKilled;

        private void SetupProcessListeners(GameProfile profile, AccountModel account, string profileDir, string javaExe, DateTime gameStartTime)
        {
            var proc = _runningProcess;
            if (proc == null) return;

            _isUserKilled = false;

            // Trim memory in background if enabled
            if (_profileManager.Config.TrimMemoryInBackground)
            {
                Task.Delay(1500).ContinueWith(_ => MemoryOptimizer.TrimWorkingSet());
            }

            int exitHandled = 0;
            void HandleExit()
            {
                if (Interlocked.Exchange(ref exitHandled, 1) != 0) return;

                int exitCode = 0;
                try
                {
                    if (proc.HasExited)
                    {
                        exitCode = proc.ExitCode;
                    }
                }
                catch { }

                if (_isUserKilled)
                {
                    exitCode = 0;
                }
                _isUserKilled = false;
                Log($"[Launcher] Игра завершена с кодом: {exitCode}");

                try
                {
                    var elapsedSeconds = (long)(DateTime.UtcNow - gameStartTime).TotalSeconds;
                    if (elapsedSeconds > 0)
                    {
                        profile.TotalPlayTimeSeconds += elapsedSeconds;
                        _profileManager.UpdateProfile(profile);
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Launcher] Ошибка сохранения времени игры: {ex.Message}");
                }

                // Post-Launch Automation & Plugins
                try { _pluginManager?.ExecutePostLaunchHooks(profile, exitCode); } catch { }

                try
                {
                    var postCmd = !string.IsNullOrWhiteSpace(profile.PostLaunchCommand)
                        ? profile.PostLaunchCommand
                        : _profileManager.Config.GlobalPostLaunchCommand;

                    if (!string.IsNullOrWhiteSpace(postCmd))
                    {
                        _ = RunAutomationScriptAsync(postCmd, profile, account, profileDir, javaExe);
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Launcher] Ошибка автоматизации post-launch: {ex.Message}");
                }

                try { _themeService?.NotifyGameRunningState(false); } catch { }
                _runningProcess = null;

                try
                {
                    GameExited?.Invoke(this, exitCode);
                }
                catch (Exception ex)
                {
                    Log($"[Launcher] Ошибка в GameExited: {ex.Message}");
                }
            }

            try
            {
                proc.EnableRaisingEvents = true;
                proc.Exited += (s, e) => HandleExit();
            }
            catch { }

            _ = Task.Run(async () =>
            {
                try
                {
                    while (exitHandled == 0)
                    {
                        if (proc.HasExited)
                        {
                            HandleExit();
                            break;
                        }
                        await Task.Delay(500);
                    }
                }
                catch
                {
                    HandleExit();
                }
            });
        }

        private async Task RunAutomationScriptAsync(string cmdTemplate, GameProfile profile, AccountModel account, string profileDir, string javaExe)
        {
            try
            {
                var cmd = cmdTemplate
                    .Replace("{game_dir}", profileDir)
                    .Replace("{profile_name}", profile.Name)
                    .Replace("{version}", profile.GameVersion)
                    .Replace("{account}", account.Username)
                    .Replace("{java_path}", javaExe);

                Log($"[Автоматизация] Выполнение команды: {cmd}");
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {cmd}",
                    WorkingDirectory = profileDir,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    await proc.WaitForExitAsync(cts.Token);
                    Log($"[Автоматизация] Команда завершена с кодом: {proc.ExitCode}");
                }
            }
            catch (Exception ex)
            {
                Log($"[Автоматизация] Ошибка скрипта: {ex.Message}", true);
            }
        }

        public void KillGame()
        {
            try
            {
                if (IsGameRunning)
                {
                    _isUserKilled = true;
                    _runningProcess?.Kill(true);
                    Log("[Launcher] Процесс игры остановлен пользователем.");
                }
            }
            catch { }
        }

        private static string GetArtifactKey(string? mavenName, string filePath)
        {
            if (!string.IsNullOrEmpty(mavenName))
            {
                var parts = mavenName.Split(':');
                if (parts.Length >= 4)
                {
                    return $"{parts[0]}:{parts[1]}:{parts[3]}";
                }
                if (parts.Length >= 2)
                {
                    return $"{parts[0]}:{parts[1]}:base";
                }
            }
            return Path.GetFileName(filePath);
        }

        public string GetRecentLogs(int maxLines = 60)
        {
            lock (_logLock)
            {
                var lines = _logHistory.TakeLast(maxLines);
                return string.Join(Environment.NewLine, lines);
            }
        }

        private void Log(string text, bool isError = false)
        {
            lock (_logLock)
            {
                _logHistory.Add(text);
                if (_logHistory.Count > 1000)
                {
                    _logHistory.RemoveAt(0);
                }
            }

            GameLogReceived?.Invoke(this, new GameLogEventArgs
            {
                Text = text,
                IsError = isError,
                Timestamp = DateTime.UtcNow
            });
        }
    }
}
