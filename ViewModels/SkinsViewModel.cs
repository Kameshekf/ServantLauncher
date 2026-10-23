using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServantLauncher.Models;
using ServantLauncher.Services;

namespace ServantLauncher.ViewModels
{
    public partial class SkinsViewModel : ObservableObject
    {
        private readonly ISkinService _skinService;
        private readonly IAccountService _accountService;

        [ObservableProperty]
        private SkinItem? _selectedSkin;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveModel))]
        private bool _isSlimModel;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveSkinSource))]
        private string? _mojangSkinUrl;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveCapeSource))]
        private string? _mojangCapeUrl;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveCapeSource))]
        private CapeInfo? _selectedCape;

        public ObservableCollection<CapeInfo> AvailableCapes { get; } = new();

        public string ActiveModel
        {
            get => IsSlimModel ? "slim" : "classic";
            set
            {
                bool slim = string.Equals(value, "slim", StringComparison.OrdinalIgnoreCase);
                if (IsSlimModel != slim)
                {
                    IsSlimModel = slim;
                }
            }
        }

        public string? ActiveSkinSource
        {
            get
            {
                if (SelectedSkin != null && !string.IsNullOrEmpty(SelectedSkin.FilePath) && File.Exists(SelectedSkin.FilePath))
                {
                    return SelectedSkin.FilePath;
                }
                if (!string.IsNullOrEmpty(MojangSkinUrl))
                {
                    return MojangSkinUrl;
                }
                var username = ActiveAccount?.Username;
                if (!string.IsNullOrEmpty(username))
                {
                    return $"https://mc-heads.net/skin/{username}";
                }
                return null;
            }
        }

        public string? ActiveCapeSource
        {
            get
            {
                if (SelectedCape != null)
                {
                    return SelectedCape.Url;
                }
                if (SelectedSkin != null && !string.IsNullOrEmpty(SelectedSkin.CapeUrl))
                {
                    return SelectedSkin.CapeUrl;
                }
                return MojangCapeUrl;
            }
        }

        public bool IsMojangSkinActive => SelectedSkin == null;

        public AccountModel? ActiveAccount => _accountService.ActiveAccount;
        public bool IsMicrosoftAccount => ActiveAccount?.Type == AccountType.Microsoft;

        public ObservableCollection<SkinItem> LibrarySkins { get; } = new();

        private readonly IProfileManager? _profileManager;

        // Edit Skin Preset Dialog Modal
        [ObservableProperty]
        private bool _isEditSkinDialogOpen;

        [ObservableProperty]
        private SkinItem? _editingSkinItem;

        [ObservableProperty]
        private string _editSkinName = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EditSkinFileName))]
        private string _editSkinFilePath = string.Empty;

        public string EditSkinFileName => string.IsNullOrEmpty(EditSkinFilePath) ? "Файл не выбран" : Path.GetFileName(EditSkinFilePath);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EditSkinModelText))]
        private bool _isEditSkinSlim;

        public string EditSkinModelText => IsEditSkinSlim ? "Slim (3px)" : "Classic (4px)";

        [ObservableProperty]
        private Bitmap? _editSkinPreviewBitmap;

        // Add Skin Dialog Modal
        [ObservableProperty]
        private bool _isAddSkinDialogOpen;

        [ObservableProperty]
        private string _newSkinFilePath = string.Empty;

        [ObservableProperty]
        private string _newSkinName = string.Empty;

        [ObservableProperty]
        private bool _isNewSkinSlim;

        [ObservableProperty]
        private CapeInfo? _newSkinSelectedCape;

        [ObservableProperty]
        private Bitmap? _newSkinPreviewBitmap;

        public SkinsViewModel(ISkinService skinService, IAccountService accountService, IProfileManager? profileManager = null)
        {
            _skinService = skinService;
            _accountService = accountService;
            _profileManager = profileManager;

            _accountService.AccountsChanged += () =>
            {
                OnPropertyChanged(nameof(ActiveAccount));
                OnPropertyChanged(nameof(IsMicrosoftAccount));
                _ = LoadCurrentSkinAsync();
            };

            RefreshLibrary();
            _ = LoadCurrentSkinAsync();
        }

        public void RefreshLibrary()
        {
            LibrarySkins.Clear();
            var skins = _skinService.GetLibrarySkins();
            if (_profileManager?.Config.SkinOrder is { Count: > 0 } order)
            {
                skins = skins.OrderBy(s =>
                {
                    var fileName = Path.GetFileName(s.FilePath);
                    int idx = order.IndexOf(fileName);
                    return idx == -1 ? int.MaxValue : idx;
                }).ToList();
            }

            foreach (var s in skins)
            {
                LibrarySkins.Add(s);
            }
            NotifySkinPropsChanged();
        }

        public void SaveSkinOrder()
        {
            if (_profileManager != null)
            {
                _profileManager.Config.SkinOrder = LibrarySkins.Select(s => Path.GetFileName(s.FilePath)).ToList();
                _profileManager.SaveConfig();
            }
        }

        public void ReorderSkin(int fromIdx, int toIdx)
        {
            if (fromIdx >= 0 && fromIdx < LibrarySkins.Count && toIdx >= 0 && toIdx < LibrarySkins.Count && fromIdx != toIdx)
            {
                LibrarySkins.Move(fromIdx, toIdx);
                SaveSkinOrder();
            }
        }

        [RelayCommand]
        public void OpenEditSkinDialog(SkinItem? skin)
        {
            var target = skin ?? SelectedSkin;
            if (target == null) return;

            EditingSkinItem = target;
            EditSkinName = target.Name;
            EditSkinFilePath = target.FilePath;
            IsEditSkinSlim = string.Equals(target.Model, "slim", StringComparison.OrdinalIgnoreCase);

            try
            {
                if (File.Exists(target.FilePath))
                {
                    EditSkinPreviewBitmap = SkinService.CompositeSkinBust(target.FilePath, IsEditSkinSlim ? "slim" : "classic");
                }
                else
                {
                    EditSkinPreviewBitmap = target.PreviewBitmap;
                }
            }
            catch
            {
                EditSkinPreviewBitmap = target.PreviewBitmap;
            }

            IsEditSkinDialogOpen = true;
        }

        partial void OnIsEditSkinSlimChanged(bool value)
        {
            UpdateEditSkinPreview();
        }

        public void SetEditSkinNewFile(string filePath)
        {
            if (!File.Exists(filePath)) return;
            EditSkinFilePath = filePath;
            var detected = SkinService.DetectSkinModel(filePath);
            IsEditSkinSlim = string.Equals(detected, "slim", StringComparison.OrdinalIgnoreCase);
            UpdateEditSkinPreview();
        }

        private void UpdateEditSkinPreview()
        {
            if (!string.IsNullOrEmpty(EditSkinFilePath) && File.Exists(EditSkinFilePath))
            {
                try
                {
                    EditSkinPreviewBitmap = SkinService.CompositeSkinBust(EditSkinFilePath, IsEditSkinSlim ? "slim" : "classic");
                }
                catch { }
            }
        }

        [RelayCommand]
        public async Task ConfirmEditSkinAsync()
        {
            if (EditingSkinItem == null)
            {
                IsEditSkinDialogOpen = false;
                return;
            }

            try
            {
                var newName = string.IsNullOrWhiteSpace(EditSkinName) ? EditingSkinItem.Name : EditSkinName.Trim();
                var newModel = IsEditSkinSlim ? "slim" : "classic";
                var newFile = EditSkinFilePath != EditingSkinItem.FilePath ? EditSkinFilePath : null;

                bool updated = await _skinService.UpdateSkinInLibraryAsync(EditingSkinItem, newName, newModel, newFile);
                if (updated)
                {
                    if (SelectedSkin == EditingSkinItem)
                    {
                        IsSlimModel = IsEditSkinSlim;
                        NotifySkinPropsChanged();
                    }
                    SaveSkinOrder();
                    StatusMessage = $"Скин '{EditingSkinItem.Name}' успешно обновлен!";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка обновления: {ex.Message}";
            }
            finally
            {
                IsEditSkinDialogOpen = false;
                EditingSkinItem = null;
            }
        }

        [RelayCommand]
        public void CancelEditSkin()
        {
            IsEditSkinDialogOpen = false;
            EditingSkinItem = null;
        }

        public async Task LoadCurrentSkinAsync()
        {
            var acc = ActiveAccount;
            if (acc == null) return;

            try
            {
                IsLoading = true;
                StatusMessage = "Загрузка скина и плащей из профиля...";
                var (skinUrl, variant, capeUrl, capes) = await _skinService.FetchMojangProfileSkinAsync(acc);
                
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    MojangSkinUrl = skinUrl;
                    MojangCapeUrl = capeUrl;

                    AvailableCapes.Clear();
                    foreach (var c in capes)
                    {
                        AvailableCapes.Add(c);
                    }

                    SelectedCape = AvailableCapes.FirstOrDefault(c => c.Url == capeUrl) 
                                   ?? AvailableCapes.FirstOrDefault(c => c.IsActive) 
                                   ?? AvailableCapes.FirstOrDefault();

                    if (SelectedSkin == null)
                    {
                        IsSlimModel = string.Equals(variant, "slim", StringComparison.OrdinalIgnoreCase);
                    }
                    NotifySkinPropsChanged();
                    StatusMessage = IsMicrosoftAccount 
                        ? "Активный скин и плащи синхронизированы с Microsoft." 
                        : $"Скин загружен для игрока {acc.Username}.";
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка загрузки скина: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void NotifySkinPropsChanged()
        {
            OnPropertyChanged(nameof(ActiveSkinSource));
            OnPropertyChanged(nameof(ActiveCapeSource));
            OnPropertyChanged(nameof(ActiveModel));
            OnPropertyChanged(nameof(IsMojangSkinActive));
        }

        partial void OnSelectedSkinChanged(SkinItem? value)
        {
            if (value != null)
            {
                if (!string.IsNullOrEmpty(value.FilePath) && File.Exists(value.FilePath))
                {
                    var detected = SkinService.DetectSkinModel(value.FilePath);
                    IsSlimModel = string.Equals(detected, "slim", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    IsSlimModel = string.Equals(value.Model, "slim", StringComparison.OrdinalIgnoreCase);
                }

                if (!string.IsNullOrEmpty(value.CapeUrl))
                {
                    SelectedCape = AvailableCapes.FirstOrDefault(c => c.Url == value.CapeUrl) ?? SelectedCape;
                }
                StatusMessage = $"Выбран локальный скин: {value.Name}";
            }
            else
            {
                StatusMessage = "Отображается официальный скин из аккаунта.";
            }
            NotifySkinPropsChanged();
        }

        partial void OnIsSlimModelChanged(bool value)
        {
            if (SelectedSkin != null)
            {
                SelectedSkin.Model = value ? "slim" : "classic";
            }
            NotifySkinPropsChanged();
        }

        partial void OnSelectedCapeChanged(CapeInfo? value)
        {
            NotifySkinPropsChanged();
        }

        [RelayCommand]
        public void SelectSkin(SkinItem? skin)
        {
            SelectedSkin = skin;
        }

        [RelayCommand]
        public void ResetToMojangSkin()
        {
            SelectedSkin = null;
            _ = LoadCurrentSkinAsync();
        }

        [RelayCommand]
        public async Task ApplySkinAsync()
        {
            var acc = ActiveAccount;
            if (acc == null || acc.Type != AccountType.Microsoft)
            {
                StatusMessage = "Для смены скина в игре необходим вход через Microsoft аккаунт.";
                return;
            }

            if (SelectedSkin == null || !File.Exists(SelectedSkin.FilePath))
            {
                StatusMessage = "Выберите скин из галереи или добавьте новый .png файл.";
                return;
            }

            StatusMessage = "Загрузка скина и плаща на серверы Minecraft Services...";
            try
            {
                var model = IsSlimModel ? "slim" : "classic";
                var success = await _skinService.UploadSkinToMojangAsync(acc, SelectedSkin.FilePath, model);
                
                if (SelectedCape != null)
                {
                    await _skinService.EquipMojangCapeAsync(acc, SelectedCape.Id);
                }

                if (success)
                {
                    StatusMessage = "Скин и плащ успешно обновлены в вашем аккаунте Microsoft!";
                    await LoadCurrentSkinAsync();
                }
                else
                {
                    StatusMessage = "Ошибка при загрузке скина на серверы Mojang.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка: {ex.Message}";
            }
        }

        partial void OnIsNewSkinSlimChanged(bool value)
        {
            if (!string.IsNullOrEmpty(NewSkinFilePath) && File.Exists(NewSkinFilePath))
            {
                try
                {
                    NewSkinPreviewBitmap = SkinService.CompositeSkinBust(NewSkinFilePath, value ? "slim" : "classic");
                }
                catch { }
            }
        }

        public void OpenAddSkinDialog(string filePath)
        {
            if (!File.Exists(filePath)) return;

            NewSkinFilePath = filePath;
            NewSkinName = Path.GetFileNameWithoutExtension(filePath);
            var autoDetected = SkinService.DetectSkinModel(filePath);
            IsNewSkinSlim = string.Equals(autoDetected, "slim", StringComparison.OrdinalIgnoreCase);
            NewSkinSelectedCape = SelectedCape ?? AvailableCapes.FirstOrDefault();
            
            try
            {
                NewSkinPreviewBitmap = SkinService.CompositeSkinBust(filePath, IsNewSkinSlim ? "slim" : "classic");
            }
            catch
            {
                try { NewSkinPreviewBitmap = new Bitmap(filePath); } catch { NewSkinPreviewBitmap = null; }
            }

            IsAddSkinDialogOpen = true;
        }

        [RelayCommand]
        public async Task ConfirmAddSkinAsync()
        {
            if (string.IsNullOrWhiteSpace(NewSkinFilePath) || !File.Exists(NewSkinFilePath))
            {
                IsAddSkinDialogOpen = false;
                return;
            }

            try
            {
                var model = IsNewSkinSlim ? "slim" : "classic";
                var item = await _skinService.AddSkinToLibraryAsync(NewSkinFilePath, NewSkinName, model, NewSkinSelectedCape?.Url);
                LibrarySkins.Add(item);
                SelectedSkin = item;
                if (NewSkinSelectedCape != null)
                {
                    SelectedCape = NewSkinSelectedCape;
                }
                StatusMessage = $"Скин '{item.Name}' добавлен в вашу галерею!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка добавления скина: {ex.Message}";
            }
            finally
            {
                IsAddSkinDialogOpen = false;
            }
        }

        [RelayCommand]
        public void CancelAddSkin()
        {
            IsAddSkinDialogOpen = false;
        }

        [RelayCommand]
        public void DeleteSkin(SkinItem? skin)
        {
            var target = skin ?? SelectedSkin;
            if (target != null)
            {
                _skinService.DeleteSkinFromLibrary(target);
                LibrarySkins.Remove(target);
                if (SelectedSkin == target)
                {
                    SelectedSkin = LibrarySkins.FirstOrDefault();
                }
                StatusMessage = "Скин удален из галереи.";
                NotifySkinPropsChanged();
            }
        }
    }
}