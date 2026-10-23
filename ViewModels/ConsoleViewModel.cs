using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServantLauncher.Services;

namespace ServantLauncher.ViewModels
{
    public class LogEntry
    {
        public string Time { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ColorBrush { get; set; } = "#E4E4E7";
    }

    public partial class ConsoleViewModel : ObservableObject
    {
        private readonly IGameLauncherService _launcherService;
        private readonly IMcLogsService _mcLogsService;

        [ObservableProperty]
        private bool _autoScroll = true;

        [ObservableProperty]
        private bool _isCopied;

        [ObservableProperty]
        private bool _isUploadingMcLogs;

        [ObservableProperty]
        private string _statusNotification = string.Empty;

        public ObservableCollection<LogEntry> Logs { get; } = new();

        public ConsoleViewModel(IGameLauncherService launcherService, IMcLogsService? mcLogsService = null)
        {
            _launcherService = launcherService;
            _mcLogsService = mcLogsService ?? new McLogsService();
            _launcherService.GameLogReceived += OnGameLogReceived;

            // Preload existing log history from launcher service
            try
            {
                var history = _launcherService.GetRecentLogs(1000);
                if (!string.IsNullOrWhiteSpace(history))
                {
                    var lines = history.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            AddLogLineInternal(line, DateTime.Now, isError: line.Contains("ERROR", StringComparison.OrdinalIgnoreCase));
                        }
                    }
                }
            }
            catch { }
        }

        public void EnsureCrashLog(string errorMessage, string stackTrace)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    AddLogLineInternal($"[CRASH ERROR] {errorMessage}", DateTime.Now, isError: true);
                }

                if (!string.IsNullOrWhiteSpace(stackTrace))
                {
                    var lines = stackTrace.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            AddLogLineInternal(line, DateTime.Now, isError: true);
                        }
                    }
                }
            });
        }

        private void OnGameLogReceived(object? sender, GameLogEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                AddLogLineInternal(e.Text, e.Timestamp.ToLocalTime(), e.IsError);
            });
        }

        private void AddLogLineInternal(string text, DateTime time, bool isError = false)
        {
            var color = "#E4E4E7"; // normal text
            if (isError || text.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || text.Contains("Exception", StringComparison.OrdinalIgnoreCase))
            {
                color = "#F87171"; // red
            }
            else if (text.Contains("WARN", StringComparison.OrdinalIgnoreCase))
            {
                color = "#FBBF24"; // yellow/amber
            }
            else if (text.StartsWith("[Launcher]", StringComparison.OrdinalIgnoreCase) || text.StartsWith("[CRASH", StringComparison.OrdinalIgnoreCase))
            {
                color = "#C084FC"; // neon purple
            }

            Logs.Add(new LogEntry
            {
                Time = time.ToString("HH:mm:ss"),
                Message = text,
                ColorBrush = color
            });

            if (Logs.Count > 3000)
            {
                Logs.RemoveAt(0);
            }
        }

        [RelayCommand]
        public void Clear()
        {
            Logs.Clear();
            StatusNotification = string.Empty;
        }

        [RelayCommand]
        public async Task CopyFullLogAsync()
        {
            var text = GetFullLogText();
            if (string.IsNullOrWhiteSpace(text)) return;

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard != null)
            {
                await desktop.MainWindow.Clipboard.SetTextAsync(text);
                IsCopied = true;
                StatusNotification = "Лог скопирован в буфер обмена!";
                await Task.Delay(2500);
                IsCopied = false;
                StatusNotification = string.Empty;
            }
        }

        [RelayCommand]
        public async Task UploadToMcLogsAsync()
        {
            var text = GetFullLogText();
            if (string.IsNullOrWhiteSpace(text))
            {
                StatusNotification = "Лог пуст, нечего загружать.";
                return;
            }

            IsUploadingMcLogs = true;
            StatusNotification = "Загрузка на mclo.gs...";

            try
            {
                var url = await _mcLogsService.UploadLogAsync(text);
                if (!string.IsNullOrEmpty(url))
                {
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard != null)
                    {
                        await desktop.MainWindow.Clipboard.SetTextAsync(url);
                    }
                    StatusNotification = $"Ссылка скопирована: {url}";
                }
                else
                {
                    StatusNotification = "Не удалось загрузить лог на mclo.gs";
                }
            }
            catch (Exception ex)
            {
                StatusNotification = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsUploadingMcLogs = false;
            }
        }

        public string GetFullLogText()
        {
            var sb = new StringBuilder();
            foreach (var log in Logs)
            {
                sb.AppendLine($"[{log.Time}] {log.Message}");
            }
            return sb.ToString();
        }
    }
}
