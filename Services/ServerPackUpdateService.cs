using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ServantLauncher.Models;

namespace ServantLauncher.Services
{
    public interface IServerPackUpdateService
    {
        void StartPeriodicCheck();
        void StopPeriodicCheck();
        Task<bool> CheckForServerPackUpdateAsync(bool isManual = false);
        Task<(bool success, bool hasUpdate, string? remoteVersion, string message)> CheckWithDetailsAsync(bool isManual = false);
    }

    public class ServerPackUpdateService : IServerPackUpdateService
    {
        private static readonly string[] ServerPackUrls =
        {
            "https://servant-ru.online/api/serverpack/version.json",
            "https://srv-api-vps-92jksdf83.servant-ru.online:8081/api/serverpack/version.json"
        };

        private readonly IAutoUpdateService _autoUpdateService;
        private readonly IProfileManager _profileManager;
        private readonly IModpackImportService _modpackImportService;
        private readonly IGlobalProgressService _progressService;
        private readonly HttpClient _httpClient;

        private DispatcherTimer? _timer;
        private bool _isChecking;

        public ServerPackUpdateService(
            IAutoUpdateService autoUpdateService,
            IProfileManager profileManager,
            IModpackImportService modpackImportService,
            IGlobalProgressService progressService,
            HttpClient? httpClient = null)
        {
            _autoUpdateService = autoUpdateService;
            _profileManager = profileManager;
            _modpackImportService = modpackImportService;
            _progressService = progressService;
            _httpClient = httpClient ?? HttpHelper.CreateHttpClient();
        }

        public void StartPeriodicCheck()
        {
            _timer?.Stop();
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(30)
            };
            _timer.Tick += (s, e) => _ = CheckForServerPackUpdateAsync(false);
            _timer.Start();

            // Run initial check 2 seconds after startup
            _ = Task.Delay(2000).ContinueWith(_ => CheckForServerPackUpdateAsync(false));
        }

        public void StopPeriodicCheck()
        {
            _timer?.Stop();
            _timer = null;
        }

        public async Task<bool> CheckForServerPackUpdateAsync(bool isManual = false)
        {
            var result = await CheckWithDetailsAsync(isManual);
            return result.hasUpdate;
        }

        public async Task<(bool success, bool hasUpdate, string? remoteVersion, string message)> CheckWithDetailsAsync(bool isManual = false)
        {
            if (_isChecking)
            {
                return (false, false, null, "Проверка сборки уже выполняется...");
            }

            _isChecking = true;
            try
            {

                _profileManager.EnsureServerProfile();
                var serverProfile = _profileManager.Profiles.FirstOrDefault(p => p.IsServerProfile);
                if (serverProfile == null)
                {
                    return (false, false, null, "Профиль сервера Servant не найден");
                }

                string? json = null;
                string? lastError = null;

                foreach (var url in ServerPackUrls)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                        using var req = new HttpRequestMessage(HttpMethod.Get, url);
                        req.Headers.Add("User-Agent", "ServantLauncher/1.3.0 (+https://servant-ru.online)");
                        req.Headers.Add("Accept", "application/json");

                        var response = await _httpClient.SendAsync(req, cts.Token);
                        if (response.IsSuccessStatusCode)
                        {
                            json = await response.Content.ReadAsStringAsync(cts.Token);
                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                Debug.WriteLine($"[ServerPackUpdateService] Successfully fetched server pack manifest from: {url}");
                                break;
                            }
                        }
                        else
                        {
                            lastError = $"HTTP {(int)response.StatusCode} ({url})";
                            Debug.WriteLine($"[ServerPackUpdateService] Response {response.StatusCode} from {url}");
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        Debug.WriteLine($"[ServerPackUpdateService] Request to {url} failed: {ex.Message}");
                    }
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    return (false, false, null, $"Не удалось связаться с сервером сборки ({lastError})");
                }

                var node = JsonNode.Parse(json);
                if (node == null)
                {
                    return (false, false, null, "Некорректный формат манифеста сборки");
                }

                var remoteVersion = node["version"]?.GetValue<string>();
                var downloadUrl = node["url"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(remoteVersion) || string.IsNullOrWhiteSpace(downloadUrl))
                {
                    return (false, false, null, "Манифест сборки не содержит версии или ссылки");
                }

                // Check if server profile version is different or missing
                bool needsUpdate = string.IsNullOrEmpty(serverProfile.ServerPackVersion) ||
                                   !string.Equals(serverProfile.ServerPackVersion.Trim(), remoteVersion.Trim(), StringComparison.OrdinalIgnoreCase);

                if (!needsUpdate)
                {
                    return (true, false, remoteVersion, $"Сборка сервера актуальна (версия {remoteVersion})");
                }

                _ = Task.Run(() => DownloadAndApplyServerPackAsync(serverProfile, downloadUrl, remoteVersion));
                return (true, true, remoteVersion, $"Найдено обновление сборки до версии {remoteVersion}! Начинается загрузка...");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServerPackUpdateService] Update check failed: {ex.Message}");
                return (false, false, null, $"Ошибка проверки сборки: {ex.Message}");
            }
            finally
            {
                _isChecking = false;
            }
        }

        private async Task DownloadAndApplyServerPackAsync(GameProfile profile, string downloadUrl, string newVersion)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"servant_server_{Guid.NewGuid():N}.mrpack");
            try
            {
                _progressService.StartTask("Обновление сборки сервера", $"Найдена версия {newVersion}. Загрузка .mrpack...", false);

                var urlsToTry = new[]
                {
                    downloadUrl,
                    "https://srv-api-vps-92jksdf83.servant-ru.online:8081/api/serverpack/servant_server.mrpack"
                };

                bool downloaded = false;
                Exception? lastEx = null;

                foreach (var url in urlsToTry)
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, url);
                        req.Headers.Add("User-Agent", "ServantLauncher/1.3.0");

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
                                _progressService.UpdateProgress(pct, $"Загрузка сборки сервера v{newVersion}...", mbText);
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
                        Debug.WriteLine($"[ServerPackUpdateService] Download from {url} failed: {ex.Message}");
                    }
                }

                if (!downloaded)
                {
                    throw new Exception(lastEx?.Message ?? "Не удалось скачать файл сборки servant_server.mrpack");
                }

                var importProgress = new Progress<(double percent, string status)>(p =>
                {
                    _progressService.UpdateProgress(p.percent, p.status, p.status);
                });

                _progressService.UpdateProgress(10, "Применение обновления сборки (сохранение пользовательских модов)...");
                await _modpackImportService.UpdateProfileFromMrpackAsync(profile, tempFile, newVersion, importProgress);

                _progressService.FinishTask($"Серверная сборка успешно обновлена до версии {newVersion}!");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServerPackUpdateService] Update failed: {ex.Message}");
                _progressService.FinishTask($"Ошибка обновления сборки: {ex.Message}");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
        }
    }
}
