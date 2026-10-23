using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using ServantLauncher.Models;

namespace ServantLauncher.Services
{
    public interface IThemeService
    {
        ThemeConfig CurrentTheme { get; }
        event Action<ThemeConfig>? ThemeChanged;
        event Action<bool>? GameRunningStateChanged;

        void ApplyPreset(ThemePreset preset);
        void SetAccentColor(string hex);
        void SetBackgroundMode(BackgroundMode mode);
        void ApplyTheme(ThemeConfig config);
        void NotifyGameRunningState(bool isRunning);
        void Save();
    }

    public class ThemeService : IThemeService
    {
        private readonly string _themeFilePath;
        public ThemeConfig CurrentTheme { get; private set; }

        public event Action<ThemeConfig>? ThemeChanged;
        public event Action<bool>? GameRunningStateChanged;

        public ThemeService()
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".servantlauncher");
            Directory.CreateDirectory(appData);
            _themeFilePath = Path.Combine(appData, "theme.json");

            CurrentTheme = LoadOrCreateTheme();
            AttachThemeListener(CurrentTheme);
            ApplyThemeResources(CurrentTheme);
        }

        private void AttachThemeListener(ThemeConfig config)
        {
            config.PropertyChanged -= OnThemePropertyChanged;
            config.PropertyChanged += OnThemePropertyChanged;
        }

        private void OnThemePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            ApplyThemeResources(CurrentTheme);
            Save();
            ThemeChanged?.Invoke(CurrentTheme);
        }

        private ThemeConfig LoadOrCreateTheme()
        {
            if (File.Exists(_themeFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_themeFilePath);
                    var cfg = JsonSerializer.Deserialize<ThemeConfig>(json);
                    if (cfg != null) return cfg;
                }
                catch { }
            }

            var defaultCfg = new ThemeConfig();
            // Add some default screenshot locations if available
            var defaultScreenshots = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Images", "Screenshots");
            if (Directory.Exists(defaultScreenshots))
            {
                foreach (var f in Directory.GetFiles(defaultScreenshots, "*.*"))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp")
                    {
                        defaultCfg.SlideshowImages.Add(f);
                    }
                }
            }
            return defaultCfg;
        }

        public void ApplyPreset(ThemePreset preset)
        {
            CurrentTheme.SelectedPreset = preset;
            switch (preset)
            {
                case ThemePreset.ServantPurple:
                    CurrentTheme.AccentColorHex = "#C084FC";
                    CurrentTheme.BrandPrimaryHex = "#7C3AED";
                    CurrentTheme.BgDarkHex = "#0A0A0E";
                    CurrentTheme.HeaderBgHex = "#F20E0E13";
                    CurrentTheme.CardBgHex = "#DF121217";
                    CurrentTheme.CardBgHoverHex = "#F0181820";
                    CurrentTheme.NavBgHex = "#141419";
                    CurrentTheme.BorderCardHex = "#28FFFFFF";
                    CurrentTheme.BorderAccentHex = "#50A855F7";
                    CurrentTheme.TextBrightHex = "#F4F4F5";
                    CurrentTheme.TextMutedHex = "#A1A1AA";
                    CurrentTheme.BtnPrimaryHex = "#7C3AED";
                    CurrentTheme.BtnPrimaryHoverHex = "#8B5CF6";
                    CurrentTheme.BtnPurpleHex = "#6D28D9";
                    CurrentTheme.BtnPurpleHoverHex = "#8B5CF6";
                    CurrentTheme.BtnOutlineBgHex = "#131318";
                    CurrentTheme.BtnOutlineHoverBgHex = "#1E1E26";
                    break;

                case ThemePreset.CyberpunkNeon:
                    CurrentTheme.AccentColorHex = "#22D3EE";
                    CurrentTheme.BrandPrimaryHex = "#06B6D4";
                    CurrentTheme.BgDarkHex = "#060B12";
                    CurrentTheme.HeaderBgHex = "#F208101A";
                    CurrentTheme.CardBgHex = "#DF0E1926";
                    CurrentTheme.CardBgHoverHex = "#F0132336";
                    CurrentTheme.NavBgHex = "#0D1724";
                    CurrentTheme.BorderCardHex = "#2822D3EE";
                    CurrentTheme.BorderAccentHex = "#6006B6D4";
                    CurrentTheme.TextBrightHex = "#F0F9FF";
                    CurrentTheme.TextMutedHex = "#94A3B8";
                    CurrentTheme.BtnPrimaryHex = "#06B6D4";
                    CurrentTheme.BtnPrimaryHoverHex = "#22D3EE";
                    CurrentTheme.BtnPurpleHex = "#0891B2";
                    CurrentTheme.BtnPurpleHoverHex = "#06B6D4";
                    CurrentTheme.BtnOutlineBgHex = "#0C1A24";
                    CurrentTheme.BtnOutlineHoverBgHex = "#162B3B";
                    break;

                case ThemePreset.EmeraldGrove:
                    CurrentTheme.AccentColorHex = "#34D399";
                    CurrentTheme.BrandPrimaryHex = "#059669";
                    CurrentTheme.BgDarkHex = "#06100B";
                    CurrentTheme.HeaderBgHex = "#F2081710";
                    CurrentTheme.CardBgHex = "#DF0E2218";
                    CurrentTheme.CardBgHoverHex = "#F0142F22";
                    CurrentTheme.NavBgHex = "#0C1B14";
                    CurrentTheme.BorderCardHex = "#2834D399";
                    CurrentTheme.BorderAccentHex = "#60059669";
                    CurrentTheme.TextBrightHex = "#ECFDF5";
                    CurrentTheme.TextMutedHex = "#9CA3AF";
                    CurrentTheme.BtnPrimaryHex = "#059669";
                    CurrentTheme.BtnPrimaryHoverHex = "#10B981";
                    CurrentTheme.BtnPurpleHex = "#047857";
                    CurrentTheme.BtnPurpleHoverHex = "#059669";
                    CurrentTheme.BtnOutlineBgHex = "#0C1C14";
                    CurrentTheme.BtnOutlineHoverBgHex = "#152E22";
                    break;

                case ThemePreset.CrimsonFlame:
                    CurrentTheme.AccentColorHex = "#F87171";
                    CurrentTheme.BrandPrimaryHex = "#DC2626";
                    CurrentTheme.BgDarkHex = "#100707";
                    CurrentTheme.HeaderBgHex = "#F2170B0B";
                    CurrentTheme.CardBgHex = "#DF221010";
                    CurrentTheme.CardBgHoverHex = "#F02D1515";
                    CurrentTheme.NavBgHex = "#1B0D0D";
                    CurrentTheme.BorderCardHex = "#28F87171";
                    CurrentTheme.BorderAccentHex = "#60DC2626";
                    CurrentTheme.TextBrightHex = "#FFF1F2";
                    CurrentTheme.TextMutedHex = "#A8A29E";
                    CurrentTheme.BtnPrimaryHex = "#DC2626";
                    CurrentTheme.BtnPrimaryHoverHex = "#EF4444";
                    CurrentTheme.BtnPurpleHex = "#B91C1C";
                    CurrentTheme.BtnPurpleHoverHex = "#DC2626";
                    CurrentTheme.BtnOutlineBgHex = "#1C0F0F";
                    CurrentTheme.BtnOutlineHoverBgHex = "#301717";
                    break;

                case ThemePreset.GoldenSun:
                    CurrentTheme.AccentColorHex = "#FBBF24";
                    CurrentTheme.BrandPrimaryHex = "#D97706";
                    CurrentTheme.BgDarkHex = "#0E0B05";
                    CurrentTheme.HeaderBgHex = "#F2141008";
                    CurrentTheme.CardBgHex = "#DF20190D";
                    CurrentTheme.CardBgHoverHex = "#F02B2212";
                    CurrentTheme.NavBgHex = "#19140A";
                    CurrentTheme.BorderCardHex = "#28FBBF24";
                    CurrentTheme.BorderAccentHex = "#60D97706";
                    CurrentTheme.TextBrightHex = "#FFFBEB";
                    CurrentTheme.TextMutedHex = "#A8A29E";
                    CurrentTheme.BtnPrimaryHex = "#D97706";
                    CurrentTheme.BtnPrimaryHoverHex = "#F59E0B";
                    CurrentTheme.BtnPurpleHex = "#B45309";
                    CurrentTheme.BtnPurpleHoverHex = "#D97706";
                    CurrentTheme.BtnOutlineBgHex = "#1C170B";
                    CurrentTheme.BtnOutlineHoverBgHex = "#302613";
                    break;

                case ThemePreset.MidnightOled:
                    CurrentTheme.AccentColorHex = "#60A5FA";
                    CurrentTheme.BrandPrimaryHex = "#2563EB";
                    CurrentTheme.BgDarkHex = "#000000";
                    CurrentTheme.HeaderBgHex = "#F2050508";
                    CurrentTheme.CardBgHex = "#DF090A0D";
                    CurrentTheme.CardBgHoverHex = "#F0101116";
                    CurrentTheme.NavBgHex = "#08080B";
                    CurrentTheme.BorderCardHex = "#20FFFFFF";
                    CurrentTheme.BorderAccentHex = "#603B82F6";
                    CurrentTheme.TextBrightHex = "#FFFFFF";
                    CurrentTheme.TextMutedHex = "#9CA3AF";
                    CurrentTheme.BtnPrimaryHex = "#2563EB";
                    CurrentTheme.BtnPrimaryHoverHex = "#3B82F6";
                    CurrentTheme.BtnPurpleHex = "#1D4ED8";
                    CurrentTheme.BtnPurpleHoverHex = "#2563EB";
                    CurrentTheme.BtnOutlineBgHex = "#0B0E14";
                    CurrentTheme.BtnOutlineHoverBgHex = "#141A26";
                    break;

                case ThemePreset.Custom:
                    break;
            }

            ApplyThemeResources(CurrentTheme);
            Save();
            ThemeChanged?.Invoke(CurrentTheme);
        }

        public void SetAccentColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return;
            CurrentTheme.AccentColorHex = hex;
            CurrentTheme.BrandPrimaryHex = hex;
            CurrentTheme.BtnPrimaryHex = hex;
            CurrentTheme.BtnPurpleHex = hex;
            CurrentTheme.SelectedPreset = ThemePreset.Custom;
            ApplyThemeResources(CurrentTheme);
            Save();
            ThemeChanged?.Invoke(CurrentTheme);
        }

        public void SetBackgroundMode(BackgroundMode mode)
        {
            CurrentTheme.BackgroundMode = mode;
            Save();
            ThemeChanged?.Invoke(CurrentTheme);
        }

        public void ApplyTheme(ThemeConfig config)
        {
            CurrentTheme = config;
            AttachThemeListener(CurrentTheme);
            ApplyThemeResources(config);
            Save();
            ThemeChanged?.Invoke(config);
        }

        public void NotifyGameRunningState(bool isRunning)
        {
            GameRunningStateChanged?.Invoke(isRunning);
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(CurrentTheme, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_themeFilePath, json);
            }
            catch { }
        }

        private static void ApplyThemeResources(ThemeConfig config)
        {
            if (Application.Current == null) return;

            void SetBrush(string key, string? hex, string fallback)
            {
                try
                {
                    var color = Color.Parse(string.IsNullOrWhiteSpace(hex) ? fallback : hex);
                    Application.Current.Resources[key] = new SolidColorBrush(color);
                }
                catch
                {
                    try { Application.Current.Resources[key] = new SolidColorBrush(Color.Parse(fallback)); } catch { }
                }
            }

            SetBrush("BgDarkBrush", config.BgDarkHex, "#0A0A0D");
            SetBrush("HeaderBgBrush", config.HeaderBgHex, "#F20E0E12");

            // Card background with GlassOpacity applied to alpha channel
            try
            {
                var baseCard = Color.Parse(string.IsNullOrWhiteSpace(config.CardBgHex) ? "#111115" : config.CardBgHex);
                byte alpha = (byte)Math.Clamp((int)(config.GlassOpacity * 255), 25, 255);
                Application.Current.Resources["CardBgBrush"] = new SolidColorBrush(Color.FromArgb(alpha, baseCard.R, baseCard.G, baseCard.B));
            }
            catch
            {
                SetBrush("CardBgBrush", config.CardBgHex, "#DF111115");
            }

            SetBrush("CardBgHoverBrush", config.CardBgHoverHex, "#F017171D");
            SetBrush("NavBgBrush", config.NavBgHex, "#131317");

            SetBrush("BrandPrimaryBrush", config.BtnPrimaryHex, "#7C3AED");
            SetBrush("BrandPrimaryHoverBrush", config.BtnPrimaryHoverHex, "#8B5CF6");
            SetBrush("BrandPurpleBrush", config.BtnPurpleHex, "#6D28D9");
            SetBrush("BrandPurpleLightBrush", config.BtnPurpleHoverHex, "#8B5CF6");
            SetBrush("BtnOutlineBgBrush", config.BtnOutlineBgHex, "#131317");
            SetBrush("BtnOutlineHoverBgBrush", config.BtnOutlineHoverBgHex, "#1E1E26");
            SetBrush("BtnDangerBgBrush", config.BtnDangerHex, "#EF4444");
            SetBrush("BtnDangerHoverBgBrush", config.BtnDangerHoverHex, "#DC2626");

            SetBrush("PillActiveBgBrush", config.BtnPrimaryHex, "#7C3AED");
            SetBrush("BrandAccentBrush", config.AccentColorHex, "#C084FC");
            SetBrush("BorderCardBrush", config.BorderCardHex, "#28FFFFFF");
            SetBrush("BorderPurpleBrush", config.BorderAccentHex, "#50A855F7");
            SetBrush("TextBrightBrush", config.TextBrightHex, "#F4F4F5");
            SetBrush("TextMutedBrush", config.TextMutedHex, "#A1A1AA");

            // Dynamic derived alpha/subtle brushes
            try
            {
                var accent = Color.Parse(string.IsNullOrWhiteSpace(config.AccentColorHex) ? "#C084FC" : config.AccentColorHex);
                Application.Current.Resources["BrandAccentAlphaBrush"] = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B));
                Application.Current.Resources["BrandAccentBorderBrush"] = new SolidColorBrush(Color.FromArgb(95, accent.R, accent.G, accent.B));
            }
            catch { }

            try
            {
                Application.Current.Resources["BorderSubtleBrush"] = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
            }
            catch { }

            // Dynamic control brushes for ComboBox & TextBox so native controls follow theme
            try
            {
                Application.Current.Resources["ComboBoxBackground"] = Application.Current.Resources["BtnOutlineBgBrush"];
                Application.Current.Resources["ComboBoxBackgroundPointerOver"] = Application.Current.Resources["BtnOutlineHoverBgBrush"];
                Application.Current.Resources["ComboBoxDropDownBackground"] = Application.Current.Resources["HeaderBgBrush"];
                Application.Current.Resources["ComboBoxDropDownBorderBrush"] = Application.Current.Resources["BorderCardBrush"];
                Application.Current.Resources["ComboBoxItemBackgroundPointerOver"] = Application.Current.Resources["BrandAccentAlphaBrush"];
                Application.Current.Resources["ComboBoxItemBackgroundSelected"] = Application.Current.Resources["BrandPrimaryBrush"];
                Application.Current.Resources["TextControlBackground"] = Application.Current.Resources["BtnOutlineBgBrush"];
                Application.Current.Resources["TextControlBackgroundPointerOver"] = Application.Current.Resources["BtnOutlineHoverBgBrush"];
                Application.Current.Resources["TextControlBackgroundFocused"] = Application.Current.Resources["CardBgHoverBrush"];
            }
            catch { }
        }
    }
}
