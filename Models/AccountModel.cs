using System;
using System.Text.Json.Serialization;

namespace ServantLauncher.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AccountType
    {
        Offline,
        Microsoft
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LauncherActionOnStart
    {
        MinimizeToTray,
        KeepOpen,
        Close
    }

    public class AccountModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("username")]
        public string Username { get; set; } = "Player";

        [JsonPropertyName("uuid")]
        public string Uuid { get; set; } = Guid.NewGuid().ToString("D");

        [JsonPropertyName("type")]
        public AccountType Type { get; set; } = AccountType.Offline;

        // Encrypted or managed in memory
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("expiresAt")]
        public DateTime? ExpiresAt { get; set; }

        private Avalonia.Media.Imaging.Bitmap? _headBitmap;
        [JsonIgnore]
        public Avalonia.Media.Imaging.Bitmap? HeadBitmap
        {
            get => _headBitmap;
            set
            {
                if (SetProperty(ref _headBitmap, value))
                {
                    OnPropertyChanged(nameof(HasHeadBitmap));
                }
            }
        }

        public bool HasHeadBitmap => _headBitmap != null;

        public string AvatarUrl => !string.IsNullOrEmpty(Uuid)
            ? $"https://mc-heads.net/avatar/{Uri.EscapeDataString(Uuid)}/64"
            : $"https://mc-heads.net/avatar/{Uri.EscapeDataString(Username)}/64";

        public string BigAvatarUrl => !string.IsNullOrEmpty(Uuid)
            ? $"https://mc-heads.net/avatar/{Uri.EscapeDataString(Uuid)}/128"
            : $"https://mc-heads.net/avatar/{Uri.EscapeDataString(Username)}/128";

        public string BodyUrl => $"https://servant-ru.online/api/profile/skin/body/{Uri.EscapeDataString(Username)}?size=256";
        public string TypeBadge => Type == AccountType.Microsoft ? "Лицензия (Microsoft)" : "Офлайн (Никнейм)";
    }

    public class LauncherConfig
    {
        [JsonPropertyName("activeProfileId")]
        public string? ActiveProfileId { get; set; }

        [JsonPropertyName("activeAccountId")]
        public string? ActiveAccountId { get; set; }

        [JsonPropertyName("defaultRamMb")]
        public int DefaultRamMb { get; set; } = 4096;

        [JsonPropertyName("customJavaPath")]
        public string? CustomJavaPath { get; set; }

        [JsonPropertyName("javaMinRamMb")]
        public int JavaMinRamMb { get; set; } = 640;

        [JsonPropertyName("javaMaxRamMb")]
        public int JavaMaxRamMb { get; set; } = 4096;

        [JsonPropertyName("javaPermGenMb")]
        public int JavaPermGenMb { get; set; } = 128;

        [JsonPropertyName("skipJavaCheck")]
        public bool SkipJavaCheck { get; set; } = true;

        [JsonPropertyName("skipJavaInstallPrompt")]
        public bool SkipJavaInstallPrompt { get; set; } = false;

        [JsonPropertyName("autoDetectJava")]
        public bool AutoDetectJava { get; set; } = true;

        [JsonPropertyName("autoDownloadMojangJava")]
        public bool AutoDownloadMojangJava { get; set; } = true;

        [JsonPropertyName("warnLowMemory")]
        public bool WarnLowMemory { get; set; } = true;

        [JsonPropertyName("customJvmArgs")]
        public string CustomJvmArgs { get; set; } = string.Empty;

        [JsonPropertyName("actionOnStart")]
        public LauncherActionOnStart ActionOnStart { get; set; } = LauncherActionOnStart.MinimizeToTray;

        [JsonPropertyName("closeLauncherOnGameStart")]
        public bool CloseLauncherOnGameStart { get; set; } = false;

        [JsonPropertyName("trimMemoryInBackground")]
        public bool TrimMemoryInBackground { get; set; } = true;

        [JsonPropertyName("openConsoleOnLaunch")]
        public bool OpenConsoleOnLaunch { get; set; } = false;

        [JsonPropertyName("enableDiscordRpc")]
        public bool EnableDiscordRpc { get; set; } = true;

        [JsonPropertyName("discordClientId")]
        public string? DiscordClientId { get; set; }

        [JsonPropertyName("discordRpcShowPage")]
        public bool DiscordRpcShowPage { get; set; } = true;

        [JsonPropertyName("discordRpcShowProfile")]
        public bool DiscordRpcShowProfile { get; set; } = true;

        [JsonPropertyName("discordRpcShowTime")]
        public bool DiscordRpcShowTime { get; set; } = true;

        [JsonPropertyName("serverIp")]
        public string ServerIp { get; set; } = "play.servant.su";

        // Global Resolution & Display
        [JsonPropertyName("useFullScreen")]
        public bool UseFullScreen { get; set; } = false;

        [JsonPropertyName("gameWindowWidth")]
        public int GameWindowWidth { get; set; } = 854;

        [JsonPropertyName("gameWindowHeight")]
        public int GameWindowHeight { get; set; } = 480;

        // Automation (Global Hooks)
        [JsonPropertyName("globalPreLaunchCommand")]
        public string GlobalPreLaunchCommand { get; set; } = string.Empty;

        [JsonPropertyName("globalPostLaunchCommand")]
        public string GlobalPostLaunchCommand { get; set; } = string.Empty;

        // Profile Ordering
        [JsonPropertyName("profileOrder")]
        public System.Collections.Generic.List<string> ProfileOrder { get; set; } = new();

        // Skin Ordering
        [JsonPropertyName("skinOrder")]
        public System.Collections.Generic.List<string> SkinOrder { get; set; } = new();
    }
}
