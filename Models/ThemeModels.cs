using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ServantLauncher.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ThemePreset
    {
        ServantPurple,
        CyberpunkNeon,
        EmeraldGrove,
        CrimsonFlame,
        GoldenSun,
        MidnightOled,
        Custom
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BackgroundMode
    {
        Slideshow, // Скриншоты с сервера
        CustomSlideshow,
        SingleImage,
        AnimatedGif,
        SolidGradient,
        None // Без фона (чистый цвет темы)
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlurLevel
    {
        Off,
        Low,
        Medium,
        High,
        Ultra
    }

    public class ThemeConfig : ObservableObject
    {
        private ThemePreset _selectedPreset = ThemePreset.ServantPurple;
        [JsonPropertyName("selectedPreset")]
        public ThemePreset SelectedPreset
        {
            get => _selectedPreset;
            set => SetProperty(ref _selectedPreset, value);
        }

        private string _accentColorHex = "#C084FC";
        [JsonPropertyName("accentColorHex")]
        public string AccentColorHex
        {
            get => _accentColorHex;
            set => SetProperty(ref _accentColorHex, value);
        }

        private string _brandPrimaryHex = "#7C3AED";
        [JsonPropertyName("brandPrimaryHex")]
        public string BrandPrimaryHex
        {
            get => _brandPrimaryHex;
            set => SetProperty(ref _brandPrimaryHex, value);
        }

        private string _bgDarkHex = "#0A0A0D";
        [JsonPropertyName("bgDarkHex")]
        public string BgDarkHex
        {
            get => _bgDarkHex;
            set => SetProperty(ref _bgDarkHex, value);
        }

        private string _headerBgHex = "#F20E0E12";
        [JsonPropertyName("headerBgHex")]
        public string HeaderBgHex
        {
            get => _headerBgHex;
            set => SetProperty(ref _headerBgHex, value);
        }

        private string _cardBgHex = "#DF111115";
        [JsonPropertyName("cardBgHex")]
        public string CardBgHex
        {
            get => _cardBgHex;
            set => SetProperty(ref _cardBgHex, value);
        }

        private string _cardBgHoverHex = "#F017171D";
        [JsonPropertyName("cardBgHoverHex")]
        public string CardBgHoverHex
        {
            get => _cardBgHoverHex;
            set => SetProperty(ref _cardBgHoverHex, value);
        }

        private string _navBgHex = "#131317";
        [JsonPropertyName("navBgHex")]
        public string NavBgHex
        {
            get => _navBgHex;
            set => SetProperty(ref _navBgHex, value);
        }

        private string _textBrightHex = "#F4F4F5";
        [JsonPropertyName("textBrightHex")]
        public string TextBrightHex
        {
            get => _textBrightHex;
            set => SetProperty(ref _textBrightHex, value);
        }

        private string _textMutedHex = "#A1A1AA";
        [JsonPropertyName("textMutedHex")]
        public string TextMutedHex
        {
            get => _textMutedHex;
            set => SetProperty(ref _textMutedHex, value);
        }

        private string _borderCardHex = "#28FFFFFF";
        [JsonPropertyName("borderCardHex")]
        public string BorderCardHex
        {
            get => _borderCardHex;
            set => SetProperty(ref _borderCardHex, value);
        }

        private string _borderAccentHex = "#50A855F7";
        [JsonPropertyName("borderAccentHex")]
        public string BorderAccentHex
        {
            get => _borderAccentHex;
            set => SetProperty(ref _borderAccentHex, value);
        }

        private string _btnPrimaryHex = "#7C3AED";
        [JsonPropertyName("btnPrimaryHex")]
        public string BtnPrimaryHex
        {
            get => _btnPrimaryHex;
            set => SetProperty(ref _btnPrimaryHex, value);
        }

        private string _btnPrimaryHoverHex = "#8B5CF6";
        [JsonPropertyName("btnPrimaryHoverHex")]
        public string BtnPrimaryHoverHex
        {
            get => _btnPrimaryHoverHex;
            set => SetProperty(ref _btnPrimaryHoverHex, value);
        }

        private string _btnPurpleHex = "#6D28D9";
        [JsonPropertyName("btnPurpleHex")]
        public string BtnPurpleHex
        {
            get => _btnPurpleHex;
            set => SetProperty(ref _btnPurpleHex, value);
        }

        private string _btnPurpleHoverHex = "#8B5CF6";
        [JsonPropertyName("btnPurpleHoverHex")]
        public string BtnPurpleHoverHex
        {
            get => _btnPurpleHoverHex;
            set => SetProperty(ref _btnPurpleHoverHex, value);
        }

        private string _btnOutlineBgHex = "#131317";
        [JsonPropertyName("btnOutlineBgHex")]
        public string BtnOutlineBgHex
        {
            get => _btnOutlineBgHex;
            set => SetProperty(ref _btnOutlineBgHex, value);
        }

        private string _btnOutlineHoverBgHex = "#1E1E26";
        [JsonPropertyName("btnOutlineHoverBgHex")]
        public string BtnOutlineHoverBgHex
        {
            get => _btnOutlineHoverBgHex;
            set => SetProperty(ref _btnOutlineHoverBgHex, value);
        }

        private string _btnDangerHex = "#EF4444";
        [JsonPropertyName("btnDangerHex")]
        public string BtnDangerHex
        {
            get => _btnDangerHex;
            set => SetProperty(ref _btnDangerHex, value);
        }

        private string _btnDangerHoverHex = "#DC2626";
        [JsonPropertyName("btnDangerHoverHex")]
        public string BtnDangerHoverHex
        {
            get => _btnDangerHoverHex;
            set => SetProperty(ref _btnDangerHoverHex, value);
        }

        private bool _enableLogoAnimation = true;
        [JsonPropertyName("enableLogoAnimation")]
        public bool EnableLogoAnimation
        {
            get => _enableLogoAnimation;
            set => SetProperty(ref _enableLogoAnimation, value);
        }

        // Blur and Transparency
        private BlurLevel _blurLevel = BlurLevel.Medium;
        [JsonPropertyName("blurLevel")]
        public BlurLevel BlurLevel
        {
            get => _blurLevel;
            set => SetProperty(ref _blurLevel, value);
        }

        private double _glassOpacity = 0.88;
        [JsonPropertyName("glassOpacity")]
        public double GlassOpacity
        {
            get => _glassOpacity;
            set => SetProperty(ref _glassOpacity, value);
        }

        // Background media
        private BackgroundMode _backgroundMode = BackgroundMode.Slideshow;
        [JsonPropertyName("backgroundMode")]
        public BackgroundMode BackgroundMode
        {
            get => _backgroundMode;
            set => SetProperty(ref _backgroundMode, value);
        }

        private string _customBackgroundImagePath = string.Empty;
        [JsonPropertyName("customBackgroundImagePath")]
        public string CustomBackgroundImagePath
        {
            get => _customBackgroundImagePath;
            set => SetProperty(ref _customBackgroundImagePath, value);
        }

        private string _customVideoPath = string.Empty;
        [JsonPropertyName("customVideoPath")]
        public string CustomVideoPath
        {
            get => _customVideoPath;
            set => SetProperty(ref _customVideoPath, value);
        }

        private int _slideshowIntervalSeconds = 8;
        [JsonPropertyName("slideshowIntervalSeconds")]
        public int SlideshowIntervalSeconds
        {
            get => _slideshowIntervalSeconds;
            set => SetProperty(ref _slideshowIntervalSeconds, value);
        }

        private int _dimOverlayPercent = 45;
        [JsonPropertyName("dimOverlayPercent")]
        public int DimOverlayPercent
        {
            get => _dimOverlayPercent;
            set => SetProperty(ref _dimOverlayPercent, value);
        }

        [JsonPropertyName("slideshowImages")]
        public ObservableCollection<string> SlideshowImages { get; set; } = new();

        [JsonIgnore]
        public string SingleImagePath
        {
            get => CustomBackgroundImagePath;
            set => CustomBackgroundImagePath = value;
        }

        [JsonIgnore]
        public string VideoPath
        {
            get => CustomVideoPath;
            set => CustomVideoPath = value;
        }

        [JsonIgnore]
        public string BrandAccentHex
        {
            get => AccentColorHex;
            set => AccentColorHex = value;
        }

        [JsonIgnore]
        public double DimOverlayOpacity
        {
            get => DimOverlayPercent / 100.0;
            set => DimOverlayPercent = (int)Math.Clamp(value * 100.0, 0, 100);
        }

        private bool _pauseWhenGameRunning = true;
        [JsonPropertyName("pauseWhenGameRunning")]
        public bool PauseWhenGameRunning
        {
            get => _pauseWhenGameRunning;
            set => SetProperty(ref _pauseWhenGameRunning, value);
        }

        public ThemeConfig Clone()
        {
            var c = new ThemeConfig
            {
                SelectedPreset = this.SelectedPreset,
                AccentColorHex = this.AccentColorHex,
                BrandPrimaryHex = this.BrandPrimaryHex,
                BgDarkHex = this.BgDarkHex,
                HeaderBgHex = this.HeaderBgHex,
                CardBgHex = this.CardBgHex,
                CardBgHoverHex = this.CardBgHoverHex,
                NavBgHex = this.NavBgHex,
                TextBrightHex = this.TextBrightHex,
                TextMutedHex = this.TextMutedHex,
                BorderCardHex = this.BorderCardHex,
                BorderAccentHex = this.BorderAccentHex,
                BtnPrimaryHex = this.BtnPrimaryHex,
                BtnPrimaryHoverHex = this.BtnPrimaryHoverHex,
                BtnPurpleHex = this.BtnPurpleHex,
                BtnPurpleHoverHex = this.BtnPurpleHoverHex,
                BtnOutlineBgHex = this.BtnOutlineBgHex,
                BtnOutlineHoverBgHex = this.BtnOutlineHoverBgHex,
                BtnDangerHex = this.BtnDangerHex,
                BtnDangerHoverHex = this.BtnDangerHoverHex,
                EnableLogoAnimation = this.EnableLogoAnimation,
                BlurLevel = this.BlurLevel,
                GlassOpacity = this.GlassOpacity,
                BackgroundMode = this.BackgroundMode,
                CustomBackgroundImagePath = this.CustomBackgroundImagePath,
                CustomVideoPath = this.CustomVideoPath,
                SlideshowIntervalSeconds = this.SlideshowIntervalSeconds,
                DimOverlayPercent = this.DimOverlayPercent
            };
            foreach (var img in this.SlideshowImages)
            {
                c.SlideshowImages.Add(img);
            }
            return c;
        }
    }
}