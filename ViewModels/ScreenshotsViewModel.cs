using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServantLauncher.Models;
using ServantLauncher.Services;

namespace ServantLauncher.ViewModels
{
    public class ProfileFilterOption
    {
        public string DisplayName { get; }
        public GameProfile? Profile { get; }

        public ProfileFilterOption(string displayName, GameProfile? profile)
        {
            DisplayName = displayName;
            Profile = profile;
        }

        public override string ToString() => DisplayName;
    }

    public partial class ScreenshotsViewModel : ObservableObject
    {
        private readonly IScreenshotService _screenshotService;
        private readonly IProfileManager _profileManager;

        private List<ScreenshotItem> _allLoadedScreenshots = new();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        partial void OnSearchQueryChanged(string value) => ApplyFiltersAndGrouping();

        [ObservableProperty]
        private int _selectedSortIndex = 0; // 0 = Сначала новые, 1 = Сначала старые, 2 = По названию

        partial void OnSelectedSortIndexChanged(int value) => ApplyFiltersAndGrouping();

        [ObservableProperty]
        private ProfileFilterOption? _selectedProfileFilter;

        partial void OnSelectedProfileFilterChanged(ProfileFilterOption? value) => ApplyFiltersAndGrouping();

        public ObservableCollection<ProfileFilterOption> ProfileFilterOptions { get; } = new();
        public ObservableCollection<ScreenshotGroup> Groups { get; } = new();

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _isEmpty;

        [ObservableProperty]
        private int _totalCount;

        [ObservableProperty]
        private ScreenshotItem? _selectedScreenshot;

        [ObservableProperty]
        private bool _isPreviewOpen;

        public bool IsScopedToProfile { get; }
        public GameProfile? ScopedProfile { get; }

        public ScreenshotsViewModel(IScreenshotService screenshotService, IProfileManager profileManager, GameProfile? scopedProfile = null)
        {
            _screenshotService = screenshotService;
            _profileManager = profileManager;
            ScopedProfile = scopedProfile;
            IsScopedToProfile = scopedProfile != null;

            SetupProfileFilters();
            _ = LoadScreenshotsAsync();
        }

        private void SetupProfileFilters()
        {
            ProfileFilterOptions.Clear();
            if (IsScopedToProfile && ScopedProfile != null)
            {
                var opt = new ProfileFilterOption(ScopedProfile.Name, ScopedProfile);
                ProfileFilterOptions.Add(opt);
                SelectedProfileFilter = opt;
            }
            else
            {
                var allOption = new ProfileFilterOption("Все сборки", null);
                ProfileFilterOptions.Add(allOption);
                foreach (var p in _profileManager.Profiles)
                {
                    ProfileFilterOptions.Add(new ProfileFilterOption(p.Name, p));
                }
                SelectedProfileFilter = allOption;
            }
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            await LoadScreenshotsAsync();
        }

        public async Task LoadScreenshotsAsync()
        {
            IsLoading = true;
            try
            {
                if (IsScopedToProfile && ScopedProfile != null)
                {
                    _allLoadedScreenshots = await _screenshotService.GetProfileScreenshotsAsync(ScopedProfile);
                }
                else
                {
                    _allLoadedScreenshots = await _screenshotService.GetAllScreenshotsAsync(_profileManager.Profiles);
                }

                Dispatcher.UIThread.Post(ApplyFiltersAndGrouping);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyFiltersAndGrouping()
        {
            var query = _allLoadedScreenshots.AsEnumerable();

            // Profile filter
            if (SelectedProfileFilter?.Profile != null)
            {
                query = query.Where(s => s.ProfileId == SelectedProfileFilter.Profile.Id);
            }

            // Search filter
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var q = SearchQuery.Trim();
                query = query.Where(s => s.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                         s.ProfileName.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            // Sorting
            query = SelectedSortIndex switch
            {
                1 => query.OrderBy(s => s.CreatedAt),
                2 => query.OrderBy(s => s.FileName),
                _ => query.OrderByDescending(s => s.CreatedAt)
            };

            var filteredList = query.ToList();
            TotalCount = filteredList.Count;
            IsEmpty = TotalCount == 0;

            Groups.Clear();

            // Group by DateGroup
            var grouped = filteredList.GroupBy(s => s.DateGroup);
            foreach (var g in grouped)
            {
                Groups.Add(new ScreenshotGroup(g.Key, g));
            }
        }

        [RelayCommand]
        private void OpenPreview(ScreenshotItem? item)
        {
            if (item == null) return;
            SelectedScreenshot = item;
            IsPreviewOpen = true;
        }

        [RelayCommand]
        private void ClosePreview()
        {
            IsPreviewOpen = false;
            SelectedScreenshot = null;
        }

        [RelayCommand]
        private void NextPreview()
        {
            if (SelectedScreenshot == null) return;
            var currentList = Groups.SelectMany(g => g.Items).ToList();
            var index = currentList.IndexOf(SelectedScreenshot);
            if (index >= 0 && index < currentList.Count - 1)
            {
                SelectedScreenshot = currentList[index + 1];
            }
        }

        [RelayCommand]
        private void PreviousPreview()
        {
            if (SelectedScreenshot == null) return;
            var currentList = Groups.SelectMany(g => g.Items).ToList();
            var index = currentList.IndexOf(SelectedScreenshot);
            if (index > 0)
            {
                SelectedScreenshot = currentList[index - 1];
            }
        }

        [RelayCommand]
        private void DeleteScreenshot(ScreenshotItem? item)
        {
            if (item == null) return;

            if (_screenshotService.DeleteScreenshot(item.FilePath))
            {
                if (SelectedScreenshot == item)
                {
                    ClosePreview();
                }

                _allLoadedScreenshots.Remove(item);
                ApplyFiltersAndGrouping();
            }
        }

        [RelayCommand]
        private void OpenInExplorer(ScreenshotItem? item)
        {
            if (item == null) return;
            _screenshotService.OpenInExplorer(item.FilePath);
        }

        [RelayCommand]
        private void OpenScreenshotsFolder()
        {
            if (SelectedProfileFilter?.Profile != null)
            {
                var dir = System.IO.Path.Combine(_profileManager.GetProfileGameDir(SelectedProfileFilter.Profile), "screenshots");
                System.IO.Directory.CreateDirectory(dir);
                _screenshotService.OpenInExplorer(dir);
            }
            else if (ScopedProfile != null)
            {
                var dir = System.IO.Path.Combine(_profileManager.GetProfileGameDir(ScopedProfile), "screenshots");
                System.IO.Directory.CreateDirectory(dir);
                _screenshotService.OpenInExplorer(dir);
            }
            else
            {
                _screenshotService.OpenInExplorer(_profileManager.InstancesDir);
            }
        }
    }
}
