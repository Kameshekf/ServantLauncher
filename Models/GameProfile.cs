using System;
using System.Text.Json.Serialization;

namespace ServantLauncher.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LoaderType
    {
        Vanilla,
        Fabric
    }

    public class GameProfile : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("name")]
        public string Name { get; set; } = "Новая сборка";

        [JsonPropertyName("gameVersion")]
        public string GameVersion { get; set; } = "1.21.4";

        [JsonPropertyName("isSnapshot")]
        public bool IsSnapshot { get; set; }

        [JsonPropertyName("loaderType")]
        public LoaderType LoaderType { get; set; } = LoaderType.Vanilla;

        [JsonPropertyName("loaderVersion")]
        public string LoaderVersion { get; set; } = string.Empty;

        [JsonPropertyName("allocatedRamMb")]
        public int AllocatedRamMb { get; set; } = 4096;

        [JsonPropertyName("customJavaPath")]
        public string? CustomJavaPath { get; set; }

        [JsonPropertyName("jvmArgs")]
        public string JvmArgs { get; set; } = "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200";

        [JsonPropertyName("icon")]
        public string Icon { get; set; } = "pack://application:,,,/Assets/Icons/servant.png";

        [JsonPropertyName("iconPreset")]
        public string IconPreset { get; set; } = "default"; // default, grass, sword, pickaxe, redstone, diamond, book, creeper, portal, gear, custom

        [JsonPropertyName("customIconPath")]
        public string? CustomIconPath { get; set; }

        [JsonIgnore]
        private Avalonia.Media.Imaging.Bitmap? _iconBitmap;

        [JsonIgnore]
        public Avalonia.Media.Imaging.Bitmap? IconBitmap
        {
            get
            {
                if (_iconBitmap == null)
                {
                    if (!string.IsNullOrEmpty(CustomIconPath) && System.IO.File.Exists(CustomIconPath))
                    {
                        try { _iconBitmap = new Avalonia.Media.Imaging.Bitmap(CustomIconPath); } catch { }
                    }
                    else if (!string.IsNullOrEmpty(Id))
                    {
                        try
                        {
                            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                            var iconCandidate = System.IO.Path.Combine(appData, ".servantlauncher", "instances", Id, "icon.png");
                            if (System.IO.File.Exists(iconCandidate))
                            {
                                CustomIconPath = iconCandidate;
                                _iconBitmap = new Avalonia.Media.Imaging.Bitmap(iconCandidate);
                            }
                        }
                        catch { }
                    }
                }
                return _iconBitmap;
            }
            set
            {
                _iconBitmap = value;
                OnPropertyChanged(nameof(IconBitmap));
                OnPropertyChanged(nameof(HasCustomIcon));
            }
        }

        [JsonIgnore]
        public bool HasCustomIcon => IconBitmap != null;

        // Resolution & Window Mode (Local vs Global)
        [JsonPropertyName("useGlobalResolution")]
        public bool UseGlobalResolution { get; set; } = true;

        [JsonPropertyName("useFullScreen")]
        public bool UseFullScreen { get; set; } = false;

        [JsonPropertyName("gameWindowWidth")]
        public int GameWindowWidth { get; set; } = 854;

        [JsonPropertyName("gameWindowHeight")]
        public int GameWindowHeight { get; set; } = 480;

        // RAM & Java (Local vs Global)
        [JsonPropertyName("useGlobalRam")]
        public bool UseGlobalRam { get; set; } = false;

        [JsonPropertyName("useGlobalJava")]
        public bool UseGlobalJava { get; set; } = true;

        // Automation (Profile-specific hooks)
        [JsonPropertyName("preLaunchCommand")]
        public string PreLaunchCommand { get; set; } = string.Empty;

        [JsonPropertyName("postLaunchCommand")]
        public string PostLaunchCommand { get; set; } = string.Empty;

        [JsonPropertyName("totalPlayTimeSeconds")]
        public long TotalPlayTimeSeconds { get; set; }

        [JsonPropertyName("isServerProfile")]
        public bool IsServerProfile { get; set; }

        [JsonPropertyName("serverPackVersion")]
        public string? ServerPackVersion { get; set; }

        [JsonIgnore]
        public string PlayTimeText
        {
            get
            {
                if (TotalPlayTimeSeconds <= 0) return "0 мин.";
                if (TotalPlayTimeSeconds < 3600)
                {
                    int mins = (int)(TotalPlayTimeSeconds / 60);
                    return $"{Math.Max(1, mins)} мин.";
                }
                int hours = (int)(TotalPlayTimeSeconds / 3600);
                int remMins = (int)((TotalPlayTimeSeconds % 3600) / 60);
                return remMins > 0 ? $"{hours} ч. {remMins} мин." : $"{hours} ч.";
            }
        }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("lastPlayed")]
        public DateTime? LastPlayed { get; set; }

        [JsonIgnore]
        public string LastPlayedText
        {
            get
            {
                if (!LastPlayed.HasValue) return "Ещё не запускалась";
                var diff = DateTime.UtcNow - LastPlayed.Value;
                if (diff.TotalMinutes < 5) return "Только что";
                if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes} мин. назад";
                if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} ч. назад";
                if (diff.TotalDays < 2) return "Вчера";
                if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} дн. назад";
                return LastPlayed.Value.ToLocalTime().ToString("dd.MM.yyyy");
            }
        }

        [JsonIgnore]
        public string FormattedLastPlayedDetailed => LastPlayed.HasValue
            ? LastPlayed.Value.ToLocalTime().ToString("d MMMM yyyy, HH:mm", new System.Globalization.CultureInfo("ru-RU"))
            : "Ещё не запускалась";

        public string DisplaySubtitle => $"{GameVersion} • {(LoaderType == LoaderType.Fabric ? $"Fabric {LoaderVersion}" : "Vanilla")}";
    }

    public class InstalledMod : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        
        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        private string _title = string.Empty;
        public string Title
        {
            get => string.IsNullOrEmpty(_title) ? DisplayName : _title;
            set => SetProperty(ref _title, value);
        }

        private string _author = string.Empty;
        public string Author
        {
            get => _author;
            set => SetProperty(ref _author, value);
        }

        private string _versionNumber = string.Empty;
        public string VersionNumber
        {
            get => string.IsNullOrEmpty(_versionNumber) ? "Установлен" : _versionNumber;
            set => SetProperty(ref _versionNumber, value);
        }

        private string? _iconUrl;
        public string? IconUrl
        {
            get => _iconUrl;
            set => SetProperty(ref _iconUrl, value);
        }

        private Avalonia.Media.Imaging.Bitmap? _iconBitmap;
        public Avalonia.Media.Imaging.Bitmap? IconBitmap
        {
            get => _iconBitmap;
            set
            {
                if (SetProperty(ref _iconBitmap, value))
                {
                    OnPropertyChanged(nameof(HasIconBitmap));
                }
            }
        }

        public bool HasIconBitmap => _iconBitmap != null;
        public bool HasAuthor => !string.IsNullOrEmpty(_author);
        public bool HasVersionNumber => !string.IsNullOrEmpty(_versionNumber);

        public string? ProjectId { get; set; }
        public string? Slug { get; set; }
        public ModrinthProjectHit? ProjectHit { get; set; }

        public string? Sha512 { get; set; }
        public string? Sha1 { get; set; }

        private bool _hasUpdate;
        public bool HasUpdate
        {
            get => _hasUpdate;
            set => SetProperty(ref _hasUpdate, value);
        }

        private ModrinthVersion? _updateVersion;
        public ModrinthVersion? UpdateVersion
        {
            get => _updateVersion;
            set
            {
                if (SetProperty(ref _updateVersion, value))
                {
                    OnPropertyChanged(nameof(UpdateVersionNumber));
                }
            }
        }

        public string UpdateVersionNumber => _updateVersion?.VersionNumber ?? string.Empty;

        private bool _isUpdating;
        public bool IsUpdating
        {
            get => _isUpdating;
            set => SetProperty(ref _isUpdating, value);
        }

        public string ContentType { get; set; } = "mod"; // mod, resourcepack, shader, datapack

        public bool IsNotMod => ContentType != "mod";

        public string ContentTypeDisplay => ContentType switch
        {
            "resourcepack" => "Ресурспак",
            "shader" => "Шейдер",
            "datapack" => "Датапак",
            _ => "Мод"
        };

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string DisplayName
        {
            get
            {
                var name = FileName;
                if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) name = name[..^9];
                if (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
                return name;
            }
        }

        public string DisplaySize => $"{FileSize / 1024.0 / 1024.0:F1} MB";
    }
}
