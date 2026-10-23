using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace ServantLauncher.Services
{
    public interface IAutoUpdateService
    {
        void StartPeriodicCheck();
        void StopPeriodicCheck();
        Task<bool> CheckForUpdatesAsync(bool isManual = false);
        Task<(bool success, bool hasUpdate, string? newVersion, string message)> CheckWithDetailsAsync(bool isManual = false);
    }

    public class AutoUpdateService : IAutoUpdateService
    {
        public static Version CurrentVersion
        {
            get
            {
                try
                {
                    var ver = typeof(AutoUpdateService).Assembly.GetName().Version;
                    if (ver != null && ver > new Version(0, 0, 0, 0))
                    {
                        return new Version(ver.Major, ver.Minor, Math.Max(0, ver.Build));
                    }
                }
                catch { }
                return new Version(1, 3, 5);
            }
        }

        private static readonly string UpdateHistoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".servantlauncher",
            "update_history.json"
        );

        private static bool IsUpdateCooldownActive(string targetVersion)
        {
            try
            {
                if (File.Exists(UpdateHistoryPath))
                {
                    var json = File.ReadAllText(UpdateHistoryPath);
                    var node = JsonNode.Parse(json);
                    if (node != null)
                    {
                        var lastVersion = node["lastVersion"]?.GetValue<string>();
                        var lastTimeStr = node["lastTimeUtc"]?.GetValue<string>();
                        if (lastVersion == targetVersion && DateTime.TryParse(lastTimeStr, out var lastTime))
                        {
                            if (DateTime.UtcNow - lastTime < TimeSpan.FromMinutes(20))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static void RecordUpdateAttempt(string targetVersion, bool success)
        {
            try
            {
                var dir = Path.GetDirectoryName(UpdateHistoryPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var node = new JsonObject
                {
                    ["lastVersion"] = targetVersion,
                    ["lastTimeUtc"] = DateTime.UtcNow.ToString("O"),
                    ["success"] = success
                };
                File.WriteAllText(UpdateHistoryPath, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private static readonly string[] VersionUrls =
        {
            "https://servant-ru.online/api/launcher/version.json",
            "https://srv-api-vps-92jksdf83.servant-ru.online:8081/api/launcher/version.json"
        };

        private readonly HttpClient _httpClient;
        private readonly IGlobalProgressService _progressService;
        private DispatcherTimer? _timer;
        private bool _isChecking;

        public AutoUpdateService(HttpClient? httpClient, IGlobalProgressService progressService)
        {
            _httpClient = httpClient ?? HttpHelper.CreateHttpClient();
            _progressService = progressService;
        }

        public void StartPeriodicCheck()
        {
            _timer?.Stop();
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(30)
            };
            _timer.Tick += (s, e) => _ = CheckForUpdatesAsync(false);
            _timer.Start();

            // Run initial check 1.5 seconds after startup in background
            _ = Task.Delay(1500).ContinueWith(_ => CheckForUpdatesAsync(false));
        }

        public void StopPeriodicCheck()
        {
            _timer?.Stop();
            _timer = null;
        }

        public async Task<bool> CheckForUpdatesAsync(bool isManual = false)
        {
            var result = await CheckWithDetailsAsync(isManual);
            return result.hasUpdate;
        }

        public async Task<(bool success, bool hasUpdate, string? newVersion, string message)> CheckWithDetailsAsync(bool isManual = false)
        {
            if (_isChecking)
            {
                return (false, false, null, "Проверка обновлений уже выполняется...");
            }

            _isChecking = true;
            try
            {
                string? json = null;
                string? lastError = null;

                foreach (var url in VersionUrls)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                        using var req = new HttpRequestMessage(HttpMethod.Get, url);
                        req.Headers.Add("User-Agent", $"ServantLauncher/{CurrentVersion} (+https://servant-ru.online)");
                        req.Headers.Add("Accept", "application/json");

                        var response = await _httpClient.SendAsync(req, cts.Token);
                        if (response.IsSuccessStatusCode)
                        {
                            json = await response.Content.ReadAsStringAsync(cts.Token);
                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                Debug.WriteLine($"[AutoUpdateService] Successfully fetched version from: {url}");
                                break;
                            }
                        }
                        else
                        {
                            lastError = $"HTTP {(int)response.StatusCode} ({url})";
                            Debug.WriteLine($"[AutoUpdateService] Response {response.StatusCode} from {url}");
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        Debug.WriteLine($"[AutoUpdateService] Request to {url} failed: {ex.Message}");
                    }
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    return (false, false, null, $"Не удалось связаться с сервером обновлений ({lastError})");
                }

                var node = JsonNode.Parse(json);
                if (node == null)
                {
                    return (false, false, null, "Некорректный формат манифеста обновлений");
                }

                var remoteVersionStr = node["version"]?.GetValue<string>();
                var downloadUrl = node["url"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(remoteVersionStr) || string.IsNullOrWhiteSpace(downloadUrl))
                {
                    return (false, false, null, "Манифест не содержит версии или ссылки на загрузку");
                }

                if (Version.TryParse(remoteVersionStr, out var remoteVersion) && remoteVersion > CurrentVersion)
                {
                    if (!isManual && IsUpdateCooldownActive(remoteVersionStr))
                    {
                        Debug.WriteLine($"[AutoUpdateService] Auto-update to {remoteVersionStr} was already attempted recently. Cooldown active, skipping auto-update.");
                        return (true, false, remoteVersionStr, $"Обновление {remoteVersionStr} уже загружалось недавно.");
                    }

                    _ = Task.Run(() => DownloadAndApplyUpdateAsync(downloadUrl, remoteVersionStr));
                    return (true, true, remoteVersionStr, $"Найдена новая версия {remoteVersionStr}! Начинается загрузка...");
                }

                return (true, false, remoteVersionStr, $"У вас установлена последняя версия ({CurrentVersion})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoUpdateService] Check error: {ex.Message}");
                return (false, false, null, $"Ошибка проверки: {ex.Message}");
            }
            finally
            {
                _isChecking = false;
            }
        }

        private async Task DownloadAndApplyUpdateAsync(string downloadUrl, string newVersion)
        {
            try
            {
                RecordUpdateAttempt(newVersion, false);
                _progressService.StartTask("Обновление лаунчера", $"Найдена версия {newVersion}. Загрузка...", false);

                var tempFile = Path.Combine(Path.GetTempPath(), "ServantLauncher_update.exe");
                if (File.Exists(tempFile)) File.Delete(tempFile);

                var urlsToTry = new[]
                {
                    downloadUrl,
                    "https://srv-api-vps-92jksdf83.servant-ru.online:8081/api/launcher/ServantLauncher.exe"
                };

                bool downloaded = false;
                Exception? lastEx = null;

                foreach (var url in urlsToTry)
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, url);
                        req.Headers.Add("User-Agent", $"ServantLauncher/{CurrentVersion}");
                        using var response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

                        if (!response.IsSuccessStatusCode)
                        {
                            continue;
                        }

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        using var sourceStream = await response.Content.ReadAsStreamAsync();
                        using var targetStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);

                        var buffer = new byte[81920];
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await targetStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                double pct = (double)totalRead / totalBytes * 100.0;
                                string mbText = $"{totalRead / (1024.0 * 1024.0):F1} / {totalBytes / (1024.0 * 1024.0):F1} MB";
                                _progressService.UpdateProgress(pct, $"Загрузка обновления {newVersion}...", mbText);
                            }
                            else
                            {
                                _progressService.UpdateProgress(-1, $"Загружено {totalRead / (1024.0 * 1024.0):F1} MB...");
                            }
                        }

                        downloaded = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        Debug.WriteLine($"[AutoUpdateService] Download from {url} failed: {ex.Message}");
                    }
                }

                if (!downloaded)
                {
                    throw new Exception(lastEx?.Message ?? "Не удалось загрузить файл обновления");
                }

                // Verify downloaded file is actually newer to prevent infinite update loops!
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(tempFile);
                    string? rawVer = vi.ProductVersion?.Split('+')[0]?.Trim() ?? vi.FileVersion?.Trim();
                    if (!string.IsNullOrWhiteSpace(rawVer) && Version.TryParse(rawVer, out var downloadedVersion))
                    {
                        if (downloadedVersion <= CurrentVersion)
                        {
                            Debug.WriteLine($"[AutoUpdateService] Downloaded executable version ({downloadedVersion}) is <= CurrentVersion ({CurrentVersion}). Aborting self-update to prevent loop.");
                            _progressService.FinishTask($"Загруженная версия ({downloadedVersion}) не новее текущей ({CurrentVersion}).");
                            if (File.Exists(tempFile)) File.Delete(tempFile);
                            RecordUpdateAttempt(newVersion, true);
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AutoUpdateService] Warning: Could not verify FileVersionInfo of update: {ex.Message}");
                }

                _progressService.FinishTask("Обновление загружено! Перезапуск лаунчера...");
                await Task.Delay(1500);

                var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath;
                if (!string.IsNullOrEmpty(currentExe) && File.Exists(tempFile))
                {
                    RecordUpdateAttempt(newVersion, true);
                    var batPath = Path.Combine(Path.GetTempPath(), "servant_self_update.bat");
                    var batScript = $@"@echo off
setlocal enabledelayedexpansion
set ""TARGET={currentExe}""
set ""TEMP_EXE={tempFile}""

set COPIED=0
for /L %%i in (1,1,15) do (
    timeout /t 1 /nobreak >nul
    copy /y ""!TEMP_EXE!"" ""!TARGET!"" >nul 2>&1
    if !errorlevel! equ 0 (
        set COPIED=1
        goto done_copy
    )
)

:done_copy
if ""!COPIED!""==""1"" (
    timeout /t 1 /nobreak >nul
    start """" ""!TARGET!""
) else (
    echo [ERROR] Update failed to overwrite !TARGET! >> ""%TEMP%\servant_update_error.log""
)

del ""!TEMP_EXE!"" >nul 2>&1
(goto) 2>nul & del ""%~f0""
";
                    File.WriteAllText(batPath, batScript);

                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{batPath}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psi);
                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoUpdateService] Download failed: {ex.Message}");
                _progressService.FinishTask($"Ошибка обновления: {ex.Message}");
            }
        }
    }
}
