using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServantLauncher.Models;
using ServantLauncher.Services;

namespace ServantLauncher.ViewModels
{
    public partial class CrashDialogViewModel : ObservableObject
    {
        private readonly GameProfile? _profile;
        private readonly IProfileManager? _profileManager;
        private readonly IMcLogsService _mcLogsService;

        [ObservableProperty]
        private string _title = "Игра завершилась с ошибкой";

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private string _stackTrace = string.Empty;

        [ObservableProperty]
        private bool _isCopied;

        [ObservableProperty]
        private bool _isUploadingMcLogs;

        [ObservableProperty]
        private string _exportStatus = string.Empty;

        public event Action? CloseRequested;
        public event Action? OpenConsoleRequested;

        public CrashDialogViewModel(
            string errorMessage,
            string stackTrace = "",
            int exitCode = -1,
            GameProfile? profile = null,
            IProfileManager? profileManager = null,
            IModrinthService? modrinthService = null,
            ICrashAnalyzerService? crashAnalyzer = null,
            IJavaService? javaService = null,
            IMcLogsService? mcLogsService = null)
        {
            _errorMessage = errorMessage;
            _stackTrace = stackTrace;
            _profile = profile;
            _profileManager = profileManager;
            _mcLogsService = mcLogsService ?? new McLogsService();

            _title = exitCode != -1 ? $"Игра аварийно завершилась (код ошибки {exitCode})" : "Ошибка запуска Minecraft";
        }

        [RelayCommand]
        public async Task CopyErrorToClipboardAsync()
        {
            var text = $"{Title}\n\nОшибка: {ErrorMessage}\n\nПодробности лога:\n{StackTrace}";
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard != null)
            {
                await desktop.MainWindow.Clipboard.SetTextAsync(text);
                IsCopied = true;
                await Task.Delay(2000);
                IsCopied = false;
            }
        }

        [RelayCommand]
        public async Task UploadToMcLogsAsync()
        {
            if (IsUploadingMcLogs) return;

            IsUploadingMcLogs = true;
            ExportStatus = "Загрузка на mclo.gs...";

            try
            {
                var combined = $"{Title}\n\nОшибка: {ErrorMessage}\n\nПодробности лога:\n{StackTrace}";
                var url = await _mcLogsService.UploadLogAsync(combined);

                if (!string.IsNullOrEmpty(url))
                {
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard != null)
                    {
                        await desktop.MainWindow.Clipboard.SetTextAsync(url);
                    }
                    ExportStatus = $"Ссылка скопирована: {url}";
                }
                else
                {
                    ExportStatus = "Не удалось загрузить на mclo.gs";
                }
            }
            catch (Exception ex)
            {
                ExportStatus = $"Ошибка загрузки: {ex.Message}";
            }
            finally
            {
                IsUploadingMcLogs = false;
            }
        }

        [RelayCommand]
        public async Task ExportLogsZipAsync()
        {
            try
            {
                var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloads))
                {
                    downloads = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }
                var zipName = $"servant-crash-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
                var zipPath = Path.Combine(downloads, zipName);

                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    // 1. Summary
                    var entrySummary = zip.CreateEntry("crash-summary.txt");
                    using (var writer = new StreamWriter(entrySummary.Open()))
                    {
                        await writer.WriteLineAsync($"{Title}\n\nОшибка: {ErrorMessage}\n\n{StackTrace}");
                    }

                    // 2. Launcher log
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var launcherLog = Path.Combine(appData, ".servantlauncher", "launcher.log");
                    if (File.Exists(launcherLog))
                    {
                        zip.CreateEntryFromFile(launcherLog, "launcher.log");
                    }

                    // 3. Game instance logs & crash reports
                    if (_profile != null && _profileManager != null)
                    {
                        var gameDir = _profileManager.GetProfileGameDir(_profile);
                        var logsDir = Path.Combine(gameDir, "logs");
                        if (Directory.Exists(logsDir))
                        {
                            foreach (var file in Directory.GetFiles(logsDir))
                            {
                                var name = Path.GetFileName(file);
                                zip.CreateEntryFromFile(file, $"game-logs/{name}");
                            }
                        }

                        var crashReportsDir = Path.Combine(gameDir, "crash-reports");
                        if (Directory.Exists(crashReportsDir))
                        {
                            foreach (var file in Directory.GetFiles(crashReportsDir))
                            {
                                var name = Path.GetFileName(file);
                                zip.CreateEntryFromFile(file, $"crash-reports/{name}");
                            }
                        }
                    }
                }

                ExportStatus = $"Сохранено в Загрузки: {zipName}";

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{zipPath}\"",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
            catch (Exception ex)
            {
                ExportStatus = $"Ошибка: {ex.Message}";
            }
        }

        [RelayCommand]
        public void OpenConsole()
        {
            OpenConsoleRequested?.Invoke();
            CloseRequested?.Invoke();
        }

        [RelayCommand]
        public void Close()
        {
            CloseRequested?.Invoke();
        }
    }
}
