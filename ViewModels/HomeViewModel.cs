using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServantLauncher.Models;
using ServantLauncher.Services;

namespace ServantLauncher.ViewModels
{
    public partial class HomeViewModel : ObservableObject
    {
        private readonly IProfileManager _profileManager;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsProfileInstalled))]
        [NotifyPropertyChangedFor(nameof(PlayButtonText))]
        [NotifyPropertyChangedFor(nameof(PlayButtonIcon))]
        private GameProfile? _activeProfile;

        [ObservableProperty]
        private bool _isProfileDropdownOpen;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PlayButtonText))]
        [NotifyPropertyChangedFor(nameof(ShowPlayButton))]
        [NotifyPropertyChangedFor(nameof(ShowCancelButton))]
        [NotifyPropertyChangedFor(nameof(ShowCloseGameButton))]
        [NotifyPropertyChangedFor(nameof(ShowProgressBar))]
        [NotifyPropertyChangedFor(nameof(IsBusy))]
        private bool _isLaunching;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PlayButtonText))]
        [NotifyPropertyChangedFor(nameof(ShowPlayButton))]
        [NotifyPropertyChangedFor(nameof(ShowCancelButton))]
        [NotifyPropertyChangedFor(nameof(ShowCloseGameButton))]
        [NotifyPropertyChangedFor(nameof(ShowProgressBar))]
        [NotifyPropertyChangedFor(nameof(IsBusy))]
        private bool _isGameRunning;

        [ObservableProperty]
        private double _launchProgressPercent;

        [ObservableProperty]
        private string _launchStatusText = string.Empty;

        public bool IsProfileInstalled => _profileManager.IsProfileInstalled(ActiveProfile);

        public bool IsBusy => IsLaunching || IsGameRunning;

        public bool ShowPlayButton => !IsLaunching && !IsGameRunning;
        public bool ShowCancelButton => IsLaunching && !IsGameRunning;
        public bool ShowCloseGameButton => IsGameRunning;
        public bool ShowProgressBar => IsLaunching && !IsGameRunning;

        public string PlayButtonText
        {
            get
            {
                if (IsGameRunning) return "ЗАКРЫТЬ ИГРУ";
                if (IsLaunching) return "ОТМЕНА";
                return IsProfileInstalled ? "ИГРАТЬ" : "УСТАНОВИТЬ";
            }
        }

        public string PlayButtonIcon => IsProfileInstalled ? "IconPlay" : "IconDownload";

        public ObservableCollection<GameProfile> Profiles { get; } = new();

        public event Action? PlayRequested;
        public event Action? CancelLaunchRequested;
        public event Action? CloseGameRequested;
        public event Action? CreateProfileRequested;
        public event Action? NavigateToProfilesRequested;

        private readonly IThemeService? _themeService;

        [ObservableProperty]
        private bool _isLogoAnimationEnabled = true;

        [RelayCommand]
        private void NavigateToProfiles()
        {
            NavigateToProfilesRequested?.Invoke();
        }

        public HomeViewModel(IProfileManager profileManager, IThemeService? themeService = null)
        {
            _profileManager = profileManager;
            _themeService = themeService;

            if (_themeService != null)
            {
                IsLogoAnimationEnabled = _themeService.CurrentTheme.EnableLogoAnimation;
                _themeService.ThemeChanged += t =>
                {
                    IsLogoAnimationEnabled = t.EnableLogoAnimation;
                };
            }

            RefreshProfiles();
            _profileManager.ProfilesChanged += RefreshProfiles;
            _profileManager.ActiveProfileChanged += () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ActiveProfile = _profileManager.ActiveProfile;
                    NotifyStateChanged();
                });
            };
        }

        public void NotifyStateChanged()
        {
            OnPropertyChanged(nameof(IsProfileInstalled));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(ShowPlayButton));
            OnPropertyChanged(nameof(ShowCancelButton));
            OnPropertyChanged(nameof(ShowCloseGameButton));
            OnPropertyChanged(nameof(ShowProgressBar));
            OnPropertyChanged(nameof(PlayButtonText));
            OnPropertyChanged(nameof(PlayButtonIcon));
        }

        public void RefreshProfiles()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var list = _profileManager.Profiles;
                bool changed = Profiles.Count != list.Count || !Profiles.Select(p => p.Id).SequenceEqual(list.Select(p => p.Id));
                if (changed)
                {
                    Profiles.Clear();
                    foreach (var p in list)
                    {
                        Profiles.Add(p);
                    }
                }
                else
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        Profiles[i] = list[i];
                    }
                }
                ActiveProfile = _profileManager.ActiveProfile;
                NotifyStateChanged();
            });
        }

        public void UpdateLaunchProgress(bool isLaunching, bool isRunning, double percent, string status)
        {
            IsLaunching = isLaunching;
            IsGameRunning = isRunning;
            LaunchProgressPercent = percent;
            LaunchStatusText = status;
            NotifyStateChanged();
        }

        [RelayCommand]
        public void SelectProfile(GameProfile profile)
        {
            _profileManager.SetActiveProfile(profile);
            ActiveProfile = profile;
            IsProfileDropdownOpen = false;
            NotifyStateChanged();
        }

        [RelayCommand]
        public void ToggleProfileDropdown()
        {
            IsProfileDropdownOpen = !IsProfileDropdownOpen;
        }

        [RelayCommand]
        public void RequestPlay()
        {
            PlayRequested?.Invoke();
        }

        [RelayCommand]
        public void RequestCancelLaunch()
        {
            CancelLaunchRequested?.Invoke();
        }

        [RelayCommand]
        public void RequestCloseGame()
        {
            CloseGameRequested?.Invoke();
        }

        [RelayCommand]
        public void RequestCreateProfile()
        {
            IsProfileDropdownOpen = false;
            CreateProfileRequested?.Invoke();
        }
    }
}
