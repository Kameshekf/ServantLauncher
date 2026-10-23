using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using ServantLauncher.Models;

namespace ServantLauncher.Services
{
    public class ScreenshotItem : ObservableObject
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string ProfileId { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string FormattedSize { get; set; } = string.Empty;
        public string FormattedDate => CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

        private Bitmap? _thumbnail;
        public Bitmap? Thumbnail
        {
            get
            {
                if (_thumbnail == null && File.Exists(FilePath))
                {
                    try
                    {
                        // Open with decode width to keep memory small
                        _thumbnail = Bitmap.DecodeToWidth(File.OpenRead(FilePath), 480);
                    }
                    catch
                    {
                        try { _thumbnail = new Bitmap(FilePath); } catch { }
                    }
                }
                return _thumbnail;
            }
            set => SetProperty(ref _thumbnail, value);
        }

        public string DateGroup
        {
            get
            {
                var now = DateTime.UtcNow;
                var diff = now - CreatedAt;
                if (diff.TotalDays <= 30 && CreatedAt.Month == now.Month && CreatedAt.Year == now.Year)
                {
                    return "В этом месяце";
                }
                if ((now.Year == CreatedAt.Year && now.Month - CreatedAt.Month == 1) || (now.Year - CreatedAt.Year == 1 && now.Month == 1 && CreatedAt.Month == 12))
                {
                    return "В прошлом месяце";
                }
                return CreatedAt.ToLocalTime().ToString("MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
            }
        }
    }

    public class ScreenshotGroup : ObservableObject
    {
        public string Title { get; set; } = string.Empty;
        public ObservableCollection<ScreenshotItem> Items { get; } = new();

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public int Count => Items.Count;

        public ScreenshotGroup(string title, IEnumerable<ScreenshotItem> items)
        {
            Title = title;
            foreach (var item in items)
            {
                Items.Add(item);
            }
        }
    }

    public interface IScreenshotService
    {
        Task<List<ScreenshotItem>> GetAllScreenshotsAsync(IEnumerable<GameProfile> profiles);
        Task<List<ScreenshotItem>> GetProfileScreenshotsAsync(GameProfile profile);
        bool DeleteScreenshot(string filePath);
        void OpenInExplorer(string filePath);
        void OpenImageViewer(string filePath);
    }

    public class ScreenshotService : IScreenshotService
    {
        private readonly IProfileManager _profileManager;

        public ScreenshotService(IProfileManager profileManager)
        {
            _profileManager = profileManager;
        }

        public async Task<List<ScreenshotItem>> GetAllScreenshotsAsync(IEnumerable<GameProfile> profiles)
        {
            return await Task.Run(() =>
            {
                var list = new List<ScreenshotItem>();
                foreach (var profile in profiles)
                {
                    var profileScreenshots = ScanProfileScreenshots(profile);
                    list.AddRange(profileScreenshots);
                }

                return list.OrderByDescending(s => s.CreatedAt).ToList();
            });
        }

        public async Task<List<ScreenshotItem>> GetProfileScreenshotsAsync(GameProfile profile)
        {
            return await Task.Run(() =>
            {
                return ScanProfileScreenshots(profile).OrderByDescending(s => s.CreatedAt).ToList();
            });
        }

        private List<ScreenshotItem> ScanProfileScreenshots(GameProfile profile)
        {
            var result = new List<ScreenshotItem>();
            try
            {
                var gameDir = _profileManager.GetProfileGameDir(profile);
                var screenshotsDir = Path.Combine(gameDir, "screenshots");

                if (!Directory.Exists(screenshotsDir)) return result;

                var dirInfo = new DirectoryInfo(screenshotsDir);
                var files = dirInfo.GetFiles()
                    .Where(f => f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                                f.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                f.Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.CreationTimeUtc);

                foreach (var file in files)
                {
                    result.Add(new ScreenshotItem
                    {
                        FilePath = file.FullName,
                        FileName = file.Name,
                        CreatedAt = file.CreationTimeUtc > file.LastWriteTimeUtc ? file.CreationTimeUtc : file.LastWriteTimeUtc,
                        ProfileId = profile.Id,
                        ProfileName = profile.Name,
                        FileSizeBytes = file.Length,
                        FormattedSize = FormatFileSize(file.Length)
                    });
                }
            }
            catch { }

            return result;
        }

        public bool DeleteScreenshot(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public void OpenInExplorer(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{filePath}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    var dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{dir}\"",
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch { }
        }

        public void OpenImageViewer(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} Б";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} КБ";
            return $"{bytes / (1024.0 * 1024.0):F1} МБ";
        }
    }
}
