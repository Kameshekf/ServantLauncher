using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ServantLauncher.Services
{
    public interface IGlobalProgressService
    {
        bool IsActive { get; }
        double Percent { get; }
        bool IsIndeterminate { get; }
        string Title { get; }
        string Subtitle { get; }
        string DetailLog { get; }
        string SpeedText { get; }
        bool CanCancel { get; }

        void StartTask(string title, string subtitle = "", bool canCancel = false);
        void UpdateProgress(double percent, string subtitle = "", string detailLog = "", string speedText = "");
        void ReportProgress(double percent, string status = "");
        void FinishTask(string? completionMessage = null);
        void Cancel();

        event Action? CancelRequested;
    }

    public partial class GlobalProgressService : ObservableObject, IGlobalProgressService
    {
        [ObservableProperty]
        private bool _isActive;

        [ObservableProperty]
        private double _percent;

        [ObservableProperty]
        private bool _isIndeterminate;

        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private string _subtitle = string.Empty;

        [ObservableProperty]
        private string _detailLog = string.Empty;

        [ObservableProperty]
        private string _speedText = string.Empty;

        [ObservableProperty]
        private bool _canCancel;

        public event Action? CancelRequested;

        public void StartTask(string title, string subtitle = "", bool canCancel = false)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Title = title;
                Subtitle = subtitle;
                Percent = 0;
                IsIndeterminate = false;
                DetailLog = string.Empty;
                SpeedText = string.Empty;
                CanCancel = canCancel;
                IsActive = true;
            });
        }

        public void UpdateProgress(double percent, string subtitle = "", string detailLog = "", string speedText = "")
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Percent = Math.Clamp(percent, 0, 100);
                IsIndeterminate = percent < 0;
                if (!string.IsNullOrEmpty(subtitle)) Subtitle = subtitle;
                if (!string.IsNullOrEmpty(detailLog)) DetailLog = detailLog;
                if (!string.IsNullOrEmpty(speedText)) SpeedText = speedText;
            });
        }

        public void ReportProgress(double percent, string status = "")
        {
            UpdateProgress(percent, status);
        }

        public void FinishTask(string? completionMessage = null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                Percent = 100;
                if (!string.IsNullOrEmpty(completionMessage))
                {
                    Subtitle = completionMessage;
                    await System.Threading.Tasks.Task.Delay(1200);
                }
                IsActive = false;
            });
        }

        public void Cancel()
        {
            CancelRequested?.Invoke();
        }
    }
}
