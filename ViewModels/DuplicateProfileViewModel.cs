using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServantLauncher.Models;
using ServantLauncher.Services;

namespace ServantLauncher.ViewModels
{
    public partial class DuplicateProfileViewModel : ObservableObject
    {
        private readonly IProfileManager _profileManager;

        public GameProfile SourceProfile { get; }

        [ObservableProperty]
        private string _newName;

        [ObservableProperty]
        private bool _copyMods = true;

        [ObservableProperty]
        private bool _copyConfigs = true;

        [ObservableProperty]
        private bool _copySaves = false;

        [ObservableProperty]
        private bool _copyShaderPacks = true;

        [ObservableProperty]
        private bool _copyOptions = true;

        public event Action<GameProfile>? ProfileDuplicated;
        public event Action? CancelRequested;

        public DuplicateProfileViewModel(GameProfile sourceProfile, IProfileManager profileManager)
        {
            SourceProfile = sourceProfile;
            _profileManager = profileManager;
            _newName = $"{sourceProfile.Name} (Копия)";
        }

        [RelayCommand]
        public void Duplicate()
        {
            var clone = _profileManager.DuplicateProfileWithOptions(
                SourceProfile,
                NewName,
                CopyMods,
                CopyConfigs,
                CopySaves,
                CopyShaderPacks,
                CopyOptions);

            ProfileDuplicated?.Invoke(clone);
        }

        [RelayCommand]
        public void Cancel()
        {
            CancelRequested?.Invoke();
        }
    }
}
