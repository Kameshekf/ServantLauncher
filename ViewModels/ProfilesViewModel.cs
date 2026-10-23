using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServantLauncher.Models;
using ServantLauncher.Services;

namespace ServantLauncher.ViewModels
{
    public partial class CreateProfileViewModel : ObservableObject
    {
        private readonly IMojangService _mojangService;
        private readonly IFabricService _fabricService;

        [ObservableProperty]
        private string _name = "Новая сборка 1.21.4";

        [ObservableProperty]
        private bool _showSnapshots;

        [ObservableProperty]
        private string _searchVersionQuery = string.Empty;

        [ObservableProperty]
        private MinecraftVersion? _selectedVersion;

        [ObservableProperty]
        private bool _isFabric = true;

        [ObservableProperty]
        private FabricLoaderInfo? _selectedFabricLoader;

        [ObservableProperty]
        private int _allocatedRamMb = 4096;

        [ObservableProperty]
        private bool _isLoadingVersions = true;

        [ObservableProperty]
        private bool _isLoadingLoaders;

        private List<MinecraftVersion> _allReleases = new();
        private List<MinecraftVersion> _allSnapshots = new();

        public ObservableCollection<MinecraftVersion> FilteredVersions { get; } = new();
        public ObservableCollection<FabricLoaderInfo> FabricLoaders { get; } = new();

        public event Action<GameProfile>? ProfileCreated;
        public event Action? CancelRequested;

        public CreateProfileViewModel(IMojangService mojangService, IFabricService fabricService)
        {
            _mojangService = mojangService;
            _fabricService = fabricService;
            _ = LoadVersionsAsync();
        }

        private async Task LoadVersionsAsync()
        {
            IsLoadingVersions = true;
            try
            {
                _allReleases = await _mojangService.GetReleasesAsync();
                _allSnapshots = await _mojangService.GetSnapshotsAsync();
                UpdateFilteredVersions();
                SelectedVersion = _allReleases.FirstOrDefault();
            }
            catch { }
            finally
            {
                IsLoadingVersions = false;
            }
        }

        partial void OnShowSnapshotsChanged(bool value)
        {
            UpdateFilteredVersions();
        }

        partial void OnSearchVersionQueryChanged(string value)
        {
            UpdateFilteredVersions();
        }

        private void UpdateFilteredVersions()
        {
            var source = ShowSnapshots ? _allSnapshots : _allReleases;
            var q = SearchVersionQuery?.Trim();

            FilteredVersions.Clear();
            var matches = string.IsNullOrEmpty(q) 
                ? source 
                : source.Where(v => v.Id.Contains(q, StringComparison.OrdinalIgnoreCase));

            foreach (var v in matches)
            {
                FilteredVersions.Add(v);
            }

            if (SelectedVersion == null || !FilteredVersions.Contains(SelectedVersion))
            {
                SelectedVersion = FilteredVersions.FirstOrDefault();
            }
        }

        async partial void OnSelectedVersionChanged(MinecraftVersion? value)
        {
            if (value != null)
            {
                Name = $"Сборка {value.Id}" + (IsFabric ? " (Fabric)" : "");
                if (IsFabric)
                {
                    await LoadFabricLoadersAsync(value.Id);
                }
            }
        }

        async partial void OnIsFabricChanged(bool value)
        {
            if (SelectedVersion != null)
            {
                Name = $"Сборка {SelectedVersion.Id}" + (value ? " (Fabric)" : "");
                if (value)
                {
                    await LoadFabricLoadersAsync(SelectedVersion.Id);
                }
            }
        }

        private async Task LoadFabricLoadersAsync(string gameVersion)
        {
            IsLoadingLoaders = true;
            FabricLoaders.Clear();
            try
            {
                var loaders = await _fabricService.GetLoaderVersionsAsync(gameVersion);
                foreach (var l in loaders)
                {
                    FabricLoaders.Add(l);
                }
                SelectedFabricLoader = FabricLoaders.FirstOrDefault(l => l.Stable) ?? FabricLoaders.FirstOrDefault();
            }
            catch { }
            finally
            {
                IsLoadingLoaders = false;
            }
        }

        [RelayCommand]
        private void Create()
        {
            if (SelectedVersion == null) return;

            var profile = new GameProfile
            {
                Name = string.IsNullOrWhiteSpace(Name) ? $"Minecraft {SelectedVersion.Id}" : Name.Trim(),
                GameVersion = SelectedVersion.Id,
                IsSnapshot = SelectedVersion.IsSnapshot,
                LoaderType = IsFabric ? LoaderType.Fabric : LoaderType.Vanilla,
                LoaderVersion = IsFabric ? (SelectedFabricLoader?.Version ?? "0.16.10") : "",
                AllocatedRamMb = AllocatedRamMb
            };

            ProfileCreated?.Invoke(profile);
        }

        [RelayCommand]
        private void Cancel()
        {
            CancelRequested?.Invoke();
        }
    }

    public partial class ProfileCardItem : ObservableObject
    {
        private readonly IProfileManager _profileManager;
        public GameProfile Profile { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PlayButtonText))]
        [NotifyPropertyChangedFor(nameof(PlayButtonIcon))]
        [NotifyPropertyChangedFor(nameof(IsDangerButton))]
        [NotifyPropertyChangedFor(nameof(ShowProgressBar))]
        private bool _isLaunching;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PlayButtonText))]
        [NotifyPropertyChangedFor(nameof(PlayButtonIcon))]
        [NotifyPropertyChangedFor(nameof(IsDangerButton))]
        [NotifyPropertyChangedFor(nameof(ShowProgressBar))]
        private bool _isGameRunning;

        [ObservableProperty]
        private double _launchProgressPercent;

        [ObservableProperty]
        private string _launchStatusText = string.Empty;

        [ObservableProperty]
        private bool _isDragging;

        public bool IsInstalled => _profileManager.IsProfileInstalled(Profile);
        public bool IsDangerButton => IsLaunching || IsGameRunning;
        public bool ShowProgressBar => IsLaunching && !IsGameRunning;

        public string PlayButtonText
        {
            get
            {
                if (IsGameRunning) return "ЗАКРЫТЬ";
                if (IsLaunching) return "ОТМЕНА";
                return IsInstalled ? "ИГРАТЬ" : "УСТАНОВИТЬ";
            }
        }

        public string PlayButtonIcon
        {
            get
            {
                if (IsGameRunning || IsLaunching) return "IconX";
                return IsInstalled ? "IconPlay" : "IconDownload";
            }
        }

        public Avalonia.Media.Geometry? IconGeometry
        {
            get
            {
                var key = Profile.IconPreset switch
                {
                    "sword" => "IconSword",
                    "pickaxe" => "IconPickaxe",
                    "diamond" => "IconDiamond",
                    "creeper" => "IconShield",
                    "redstone" => "IconZap",
                    "book" => "IconFileText",
                    "portal" => "IconLayers",
                    "gear" => "IconSettings",
                    _ => "IconBoxes"
                };

                if (Avalonia.Application.Current != null && Avalonia.Application.Current.TryGetResource(key, null, out var res) && res is Avalonia.Media.Geometry g)
                {
                    return g;
                }
                return null;
            }
        }

        public string LastPlayedText => Profile.LastPlayedText;
        public string PlayTimeText => Profile.PlayTimeText;
        public bool IsServerProfile => Profile.IsServerProfile;
        public bool CanDelete => !Profile.IsServerProfile;

        public ProfileCardItem(GameProfile profile, IProfileManager profileManager)
        {
            Profile = profile;
            _profileManager = profileManager;
        }

        public void NotifyStateChanged()
        {
            OnPropertyChanged(nameof(IsInstalled));
            OnPropertyChanged(nameof(IsDangerButton));
            OnPropertyChanged(nameof(ShowProgressBar));
            OnPropertyChanged(nameof(PlayButtonText));
            OnPropertyChanged(nameof(PlayButtonIcon));
            OnPropertyChanged(nameof(IconGeometry));
            OnPropertyChanged(nameof(LastPlayedText));
            OnPropertyChanged(nameof(PlayTimeText));
            OnPropertyChanged(nameof(IsServerProfile));
            OnPropertyChanged(nameof(CanDelete));
        }
    }

    public partial class ProfilesViewModel : ObservableObject
    {
        private readonly IProfileManager _profileManager;
        private readonly IMojangService _mojangService;
        private readonly IFabricService _fabricService;
        private readonly IModrinthService _modrinthService;
        private readonly IImageCacheService _imageCacheService;
        private readonly IGameLauncherService _launcherService;
        private readonly IModpackImportService _modpackImportService;

        [ObservableProperty]
        private GameProfile? _selectedProfile;

        [ObservableProperty]
        private bool _isCreateDialogOpen;

        [ObservableProperty]
        private CreateProfileViewModel? _createDialogVm;

        [ObservableProperty]
        private bool _isDeleteDialogOpen;

        [ObservableProperty]
        private DeleteProfileViewModel? _deleteDialogVm;

        [ObservableProperty]
        private bool _isDuplicateDialogOpen;

        [ObservableProperty]
        private DuplicateProfileViewModel? _duplicateDialogVm;

        [ObservableProperty]
        private ProfileDetailsViewModel? _activeDetailsVm;

        [ObservableProperty]
        private bool _isViewingDetails;

        [ObservableProperty]
        private bool _isImportingModpack;

        [ObservableProperty]
        private double _importProgressPercent;

        [ObservableProperty]
        private string _importStatusText = string.Empty;

        [ObservableProperty]
        private bool _isLaunching;

        [ObservableProperty]
        private bool _isGameRunning;

        [ObservableProperty]
        private double _launchProgressPercent;

        [ObservableProperty]
        private string _launchStatusText = string.Empty;

        public bool HasNoProfiles => ProfileCards.Count == 0;

        public ObservableCollection<GameProfile> Profiles { get; } = new();
        public ObservableCollection<ProfileCardItem> ProfileCards { get; } = new();

        public event Action? PlayActiveRequested;
        public event Action? CancelLaunchRequested;
        public event Action? CloseGameRequested;
        public event Action? OpenModrinthCatalogRequested;

        public ProfilesViewModel(
            IProfileManager profileManager,
            IMojangService mojangService,
            IFabricService fabricService,
            IModrinthService? modrinthService = null,
            IImageCacheService? imageCacheService = null,
            IGameLauncherService? launcherService = null,
            IModpackImportService? modpackImportService = null)
        {
            _profileManager = profileManager;
            _mojangService = mojangService;
            _fabricService = fabricService;
            _modrinthService = modrinthService ?? new ModrinthService();
            _imageCacheService = imageCacheService ?? new ImageCacheService();
            _launcherService = launcherService ?? new GameLauncherService(mojangService, fabricService, new JavaService(), profileManager);
            _modpackImportService = modpackImportService ?? new ModpackImportService(profileManager);

            RefreshProfiles();
            _profileManager.ProfilesChanged += RefreshProfiles;
            _profileManager.ActiveProfileChanged += () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    SelectedProfile = _profileManager.ActiveProfile;
                    foreach (var c in ProfileCards) c.NotifyStateChanged();
                });
            };
        }

        public void RefreshProfiles()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var order = _profileManager.Config.ProfileOrder ?? new List<string>();
                var sorted = _profileManager.Profiles
                    .OrderBy(p =>
                    {
                        int idx = order.IndexOf(p.Id);
                        return idx == -1 ? int.MaxValue : idx;
                    })
                    .ToList();

                // Reconcile Profiles collection
                bool profilesOrderOrCountChanged = Profiles.Count != sorted.Count || 
                    !Profiles.Select(p => p.Id).SequenceEqual(sorted.Select(p => p.Id));

                if (profilesOrderOrCountChanged)
                {
                    Profiles.Clear();
                    foreach (var p in sorted)
                    {
                        Profiles.Add(p);
                    }
                }
                else
                {
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        Profiles[i] = sorted[i];
                    }
                }

                // Reconcile ProfileCards collection
                bool cardsOrderOrCountChanged = ProfileCards.Count != sorted.Count || 
                    !ProfileCards.Select(c => c.Profile.Id).SequenceEqual(sorted.Select(p => p.Id));

                if (cardsOrderOrCountChanged)
                {
                    ProfileCards.Clear();
                    foreach (var p in sorted)
                    {
                        ProfileCards.Add(new ProfileCardItem(p, _profileManager));
                    }
                }
                else
                {
                    // Existing cards match by ID and order; update them in place
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        ProfileCards[i].Profile = sorted[i];
                        ProfileCards[i].NotifyStateChanged();
                    }
                }

                SelectedProfile = _profileManager.ActiveProfile;
                OnPropertyChanged(nameof(HasNoProfiles));
            });
        }

        [RelayCommand]
        public void MoveProfileUp(ProfileCardItem item)
        {
            if (item == null) return;
            int idx = ProfileCards.IndexOf(item);
            if (idx > 0)
            {
                ProfileCards.Move(idx, idx - 1);
                SaveProfileOrder();
            }
        }

        [RelayCommand]
        public void MoveProfileDown(ProfileCardItem item)
        {
            if (item == null) return;
            int idx = ProfileCards.IndexOf(item);
            if (idx >= 0 && idx < ProfileCards.Count - 1)
            {
                ProfileCards.Move(idx, idx + 1);
                SaveProfileOrder();
            }
        }

        public void SaveProfileOrder()
        {
            _profileManager.Config.ProfileOrder = ProfileCards.Select(c => c.Profile.Id).ToList();
            _profileManager.SaveConfig();
        }

        public void UpdateLaunchProgress(bool isLaunching, bool isRunning, double percent, string status)
        {
            IsLaunching = isLaunching;
            IsGameRunning = isRunning;
            LaunchProgressPercent = percent;
            LaunchStatusText = status;

            var activeProfId = _profileManager.ActiveProfile?.Id;
            foreach (var c in ProfileCards)
            {
                if (c.Profile.Id == activeProfId)
                {
                    c.IsLaunching = isLaunching;
                    c.IsGameRunning = isRunning;
                    c.LaunchProgressPercent = percent;
                    c.LaunchStatusText = status;
                }
                else
                {
                    c.IsLaunching = false;
                    c.IsGameRunning = false;
                    c.LaunchProgressPercent = 0;
                    c.LaunchStatusText = string.Empty;
                }
                c.NotifyStateChanged();
            }

            ActiveDetailsVm?.UpdateLaunchProgress(isLaunching, isRunning, percent, status);
        }

        public async Task ImportMrpackFileAsync(string mrpackFilePath)
        {
            if (string.IsNullOrEmpty(mrpackFilePath)) return;

            var globalProgress = App.GlobalProgressInstance;
            globalProgress?.StartTask("Импорт модпака", "Чтение файлов .mrpack...", false);

            var progress = new Progress<(double percent, string status)>(p =>
            {
                globalProgress?.UpdateProgress(p.percent, p.status, p.status);
            });

            try
            {
                var profile = await _modpackImportService.ImportMrpackAsync(mrpackFilePath, progress);
                globalProgress?.FinishTask("Модпак успешно импортирован!");
                RefreshProfiles();
                OpenProfileDetails(profile);
            }
            catch (Exception ex)
            {
                globalProgress?.FinishTask($"Ошибка импорта: {ex.Message}");
            }
        }

        [RelayCommand]
        public void OpenProfileDetails(object? parameter)
        {
            GameProfile? p = null;
            if (parameter is ProfileCardItem card) p = card.Profile;
            else if (parameter is GameProfile prof) p = prof;
            else p = SelectedProfile;

            if (p != null)
            {
                _profileManager.SetActiveProfile(p);
                SelectedProfile = p;
                ActiveDetailsVm = new ProfileDetailsViewModel(p, _profileManager, _modrinthService, _imageCacheService, _launcherService, _modpackImportService);
                ActiveDetailsVm.UpdateLaunchProgress(IsLaunching, IsGameRunning, LaunchProgressPercent, LaunchStatusText);
                ActiveDetailsVm.BackRequested += () =>
                {
                    IsViewingDetails = false;
                    ActiveDetailsVm = null;
                    RefreshProfiles();
                };
                ActiveDetailsVm.PlayRequested += () => PlayActiveRequested?.Invoke();
                ActiveDetailsVm.CancelLaunchRequested += () => CancelLaunchRequested?.Invoke();
                ActiveDetailsVm.CloseGameRequested += () => CloseGameRequested?.Invoke();
                ActiveDetailsVm.DuplicateRequested += (prof2) => DuplicateProfile(prof2);
                ActiveDetailsVm.DeleteRequested += (prof2) => RequestDeleteProfile(prof2);
                IsViewingDetails = true;
            }
        }

        [RelayCommand]
        private void SelectProfile(GameProfile profile)
        {
            _profileManager.SetActiveProfile(profile);
            SelectedProfile = profile;
        }

        [RelayCommand]
        public void OpenCreateDialog()
        {
            CreateDialogVm = new CreateProfileViewModel(_mojangService, _fabricService);
            CreateDialogVm.ProfileCreated += (newProfile) =>
            {
                _profileManager.AddProfile(newProfile);
                IsCreateDialogOpen = false;
                OpenProfileDetails(newProfile);
            };
            CreateDialogVm.CancelRequested += () =>
            {
                IsCreateDialogOpen = false;
            };
            IsCreateDialogOpen = true;
        }

        [RelayCommand]
        private void OpenProfileFolder(object? parameter)
        {
            GameProfile? p = parameter is ProfileCardItem card ? card.Profile : parameter as GameProfile ?? SelectedProfile;
            if (p != null)
            {
                var dir = _profileManager.GetProfileGameDir(p);
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{dir}\"",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        [RelayCommand]
        private void OpenModsFolder(object? parameter)
        {
            GameProfile? p = parameter is ProfileCardItem card ? card.Profile : parameter as GameProfile ?? SelectedProfile;
            if (p != null)
            {
                var dir = _profileManager.GetProfileModsDir(p);
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{dir}\"",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        [RelayCommand]
        public void OpenModrinthCatalog()
        {
            OpenModrinthCatalogRequested?.Invoke();
        }

        [RelayCommand]
        public void RequestDeleteProfile(object? parameter)
        {
            GameProfile? p = parameter is ProfileCardItem card ? card.Profile : parameter as GameProfile ?? SelectedProfile;
            if (p != null)
            {
                if (p.IsServerProfile) return;

                DeleteDialogVm = new DeleteProfileViewModel(p, _profileManager);
                DeleteDialogVm.DeleteCompleted += () =>
                {
                    IsDeleteDialogOpen = false;
                    DeleteDialogVm = null;
                    if (IsViewingDetails && ActiveDetailsVm?.Profile.Id == p.Id)
                    {
                        IsViewingDetails = false;
                        ActiveDetailsVm = null;
                    }
                    RefreshProfiles();
                };
                DeleteDialogVm.CancelRequested += () =>
                {
                    IsDeleteDialogOpen = false;
                    DeleteDialogVm = null;
                };
                IsDeleteDialogOpen = true;
            }
        }

        [RelayCommand]
        public void DuplicateProfile(object? parameter)
        {
            GameProfile? p = parameter is ProfileCardItem card ? card.Profile : parameter as GameProfile ?? SelectedProfile;
            if (p != null)
            {
                DuplicateDialogVm = new DuplicateProfileViewModel(p, _profileManager);
                DuplicateDialogVm.ProfileDuplicated += (clone) =>
                {
                    IsDuplicateDialogOpen = false;
                    DuplicateDialogVm = null;
                    RefreshProfiles();
                    OpenProfileDetails(clone);
                };
                DuplicateDialogVm.CancelRequested += () =>
                {
                    IsDuplicateDialogOpen = false;
                    DuplicateDialogVm = null;
                };
                IsDuplicateDialogOpen = true;
            }
        }

        [RelayCommand]
        private void DeleteProfile(object? parameter)
        {
            RequestDeleteProfile(parameter);
        }

        [RelayCommand]
        private void PlayProfile(object? parameter)
        {
            if (parameter is ProfileCardItem card)
            {
                if (card.IsLaunching)
                {
                    CancelLaunchRequested?.Invoke();
                    return;
                }
                if (card.IsGameRunning)
                {
                    CloseGameRequested?.Invoke();
                    return;
                }
                _profileManager.SetActiveProfile(card.Profile);
                PlayActiveRequested?.Invoke();
                return;
            }

            if (parameter is GameProfile profile)
            {
                _profileManager.SetActiveProfile(profile);
            }
            PlayActiveRequested?.Invoke();
        }
    }
}
