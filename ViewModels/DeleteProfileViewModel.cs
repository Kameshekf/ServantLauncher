using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServantLauncher.Models;
using ServantLauncher.Services;

namespace ServantLauncher.ViewModels
{
    public partial class DeleteProfileViewModel : ObservableObject
    {
        private readonly IProfileManager _profileManager;

        public GameProfile TargetProfile { get; }

        [ObservableProperty]
        private bool _isDeleteConfirmed;

        public event Action? DeleteCompleted;
        public event Action? CancelRequested;

        public DeleteProfileViewModel(GameProfile targetProfile, IProfileManager profileManager)
        {
            TargetProfile = targetProfile;
            _profileManager = profileManager;
        }

        [RelayCommand]
        private void ConfirmDelete()
        {
            if (!IsDeleteConfirmed) return;

            _profileManager.DeleteProfile(TargetProfile);
            DeleteCompleted?.Invoke();
        }

        [RelayCommand]
        private void Cancel()
        {
            CancelRequested?.Invoke();
        }
    }
}
