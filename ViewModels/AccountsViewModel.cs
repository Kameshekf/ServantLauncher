using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServantLauncher.Models;
using ServantLauncher.Services;

namespace ServantLauncher.ViewModels
{
    public partial class AccountsViewModel : ObservableObject
    {
        private readonly IAccountService _accountService;
        private CancellationTokenSource? _authCts;

        [ObservableProperty]
        private AccountModel? _selectedAccount;

        [ObservableProperty]
        private string _offlineUsername = string.Empty;

        [ObservableProperty]
        private bool _isAuthenticatingMicrosoft;

        [ObservableProperty]
        private string _microsoftUserCode = string.Empty;

        [ObservableProperty]
        private string _microsoftAuthUrl = "https://microsoft.com/link";

        [ObservableProperty]
        private string _authStatusText = string.Empty;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private bool _isCodeCopied;

        public bool HasUserCode => !string.IsNullOrEmpty(MicrosoftUserCode);

        partial void OnMicrosoftUserCodeChanged(string value)
        {
            OnPropertyChanged(nameof(HasUserCode));
            IsCodeCopied = false;
        }

        public ObservableCollection<AccountModel> Accounts { get; } = new();

        public event Action? CloseRequested;

        public AccountsViewModel(IAccountService accountService)
        {
            _accountService = accountService;
            RefreshAccounts();
            _accountService.AccountsChanged += RefreshAccounts;
        }

        private void RefreshAccounts()
        {
            Accounts.Clear();
            foreach (var acc in _accountService.Accounts)
            {
                Accounts.Add(acc);
            }
            SelectedAccount = _accountService.ActiveAccount;
        }

        [RelayCommand]
        private void SelectAccount(AccountModel? account)
        {
            if (account != null)
            {
                _accountService.SetActiveAccount(account);
                SelectedAccount = account;
            }
        }

        [RelayCommand]
        private void RemoveAccount(AccountModel? account)
        {
            if (account != null && Accounts.Count > 1)
            {
                _accountService.RemoveAccount(account);
            }
        }

        [RelayCommand]
        private void AddOfflineAccount()
        {
            if (string.IsNullOrWhiteSpace(OfflineUsername)) return;

            ErrorMessage = string.Empty;
            var acc = _accountService.CreateOfflineAccount(OfflineUsername.Trim());
            OfflineUsername = string.Empty;
            SelectedAccount = acc;
            CloseRequested?.Invoke();
        }

        [RelayCommand]
        private async Task StartMicrosoftLoginAsync()
        {
            ErrorMessage = string.Empty;
            IsAuthenticatingMicrosoft = true;
            AuthStatusText = "Подключение к серверам Microsoft...";
            _authCts = new CancellationTokenSource();

            try
            {
                var deviceCode = await _accountService.StartMicrosoftAuthAsync();
                MicrosoftUserCode = deviceCode.UserCode;
                MicrosoftAuthUrl = deviceCode.VerificationUri;
                AuthStatusText = "Перейдите по ссылке и введите указанный код для входа.";

                // Auto open browser link
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = MicrosoftAuthUrl,
                        UseShellExecute = true
                    });
                }
                catch { }

                var progress = new Progress<string>(msg => AuthStatusText = msg);
                var acc = await _accountService.CompleteMicrosoftAuthAsync(deviceCode, progress, _authCts.Token);

                SelectedAccount = acc;
                IsAuthenticatingMicrosoft = false;
                ErrorMessage = string.Empty;
                CloseRequested?.Invoke();
            }
            catch (OperationCanceledException)
            {
                AuthStatusText = "Авторизация отменена.";
                IsAuthenticatingMicrosoft = false;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                AuthStatusText = $"Ошибка авторизации: {ex.Message}";
                IsAuthenticatingMicrosoft = false;
            }
        }

        [RelayCommand]
        private void CancelMicrosoftLogin()
        {
            _authCts?.Cancel();
            IsAuthenticatingMicrosoft = false;
            ErrorMessage = string.Empty;
        }

        [RelayCommand]
        private async Task CopyCodeToClipboardAsync()
        {
            if (string.IsNullOrEmpty(MicrosoftUserCode)) return;

            try
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                {
                    var clipboard = desktop.MainWindow.Clipboard;
                    if (clipboard != null)
                    {
                        await clipboard.SetTextAsync(MicrosoftUserCode);
                        IsCodeCopied = true;
                        _ = Task.Delay(3000).ContinueWith(_ => IsCodeCopied = false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to copy to clipboard: {ex.Message}");
            }
        }

        [RelayCommand]
        private void OpenBrowserAuth()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = MicrosoftAuthUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        [RelayCommand]
        private void CloseDialog()
        {
            CloseRequested?.Invoke();
        }
    }
}
