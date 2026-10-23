using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using ServantLauncher.ViewModels;
using ServantLauncher.Views;

namespace ServantLauncher
{
    public partial class MainWindow : Window
    {
        private TrayIcon? _trayIcon;
        private ModalOverlayWindow? _activeModalWindow;

        public MainWindow()
        {
            InitializeComponent();
            SetupTrayIcon();
        }

        public MainWindow(MainViewModel viewModel) : this()
        {
            DataContext = viewModel;

            viewModel.MinimizeRequested += () => WindowState = WindowState.Minimized;
            viewModel.MaximizeRequested += () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            viewModel.CloseAppRequested += () =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_trayIcon != null)
                    {
                        _trayIcon.IsVisible = false;
                        _trayIcon.Dispose();
                    }
                    Close();
                });
            };

            viewModel.HideToTrayRequested += () =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(Hide);
            };

            viewModel.RestoreFromTrayRequested += () =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                });
            };

            viewModel.PropertyChanged += OnMainViewModelPropertyChanged;
            viewModel.SkinsVm.PropertyChanged += OnSkinsViewModelPropertyChanged;
        }

        private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => OnMainViewModelPropertyChanged(sender, e));
                return;
            }

            if (DataContext is not MainViewModel vm) return;

            if (e.PropertyName == nameof(MainViewModel.IsAccountsDialogOpen))
            {
                SyncModal(vm.IsAccountsDialogOpen, () => new AccountsDialog { DataContext = vm.AccountsVm }, () => vm.IsAccountsDialogOpen = false);
            }
            else if (e.PropertyName == nameof(MainViewModel.IsSettingsDialogOpen))
            {
                SyncModal(vm.IsSettingsDialogOpen, () => new SettingsView { DataContext = vm.SettingsVm }, () => vm.IsSettingsDialogOpen = false);
            }
            else if (e.PropertyName == nameof(MainViewModel.IsCrashDialogOpen))
            {
                SyncModal(vm.IsCrashDialogOpen, () => new CrashDialog { DataContext = vm.CrashDialogVm }, () => vm.IsCrashDialogOpen = false);
            }
        }

        private void OnSkinsViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => OnSkinsViewModelPropertyChanged(sender, e));
                return;
            }

            if (DataContext is not MainViewModel vm) return;

            if (e.PropertyName == nameof(SkinsViewModel.IsAddSkinDialogOpen))
            {
                SyncModal(vm.SkinsVm.IsAddSkinDialogOpen, () => new AddSkinDialog { DataContext = vm.SkinsVm }, () => vm.SkinsVm.IsAddSkinDialogOpen = false);
            }
        }

        private void SyncModal(bool isOpen, Func<Control> createContent, Action onClosed)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (isOpen)
                {
                    if (_activeModalWindow != null)
                    {
                        _activeModalWindow.Close();
                        _activeModalWindow = null;
                    }

                    var content = createContent();
                    _activeModalWindow = new ModalOverlayWindow(this, content, () =>
                    {
                        _activeModalWindow = null;
                        onClosed();
                    });
                    _activeModalWindow.Show(this);
                }
                else
                {
                    if (_activeModalWindow != null)
                    {
                        var modal = _activeModalWindow;
                        _activeModalWindow = null;
                        modal.Close();
                    }
                }
            });
        }

        private void SetupTrayIcon()
        {
            try
            {
                var iconUri = new Uri("avares://ServantLauncher/Assets/Images/server-icon-rounded.png");
                var icon = new WindowIcon(AssetLoader.Open(iconUri));
                Icon = icon;

                _trayIcon = new TrayIcon
                {
                    Icon = icon,
                    ToolTipText = "Servant Launcher",
                    IsVisible = true
                };

                var menu = new NativeMenu();
                var openItem = new NativeMenuItem("Открыть Servant Launcher");
                openItem.Click += (s, e) =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                };

                var exitItem = new NativeMenuItem("Выход");
                exitItem.Click += (s, e) =>
                {
                    if (_trayIcon != null)
                    {
                        _trayIcon.IsVisible = false;
                        _trayIcon.Dispose();
                    }
                    Close();
                };

                menu.Add(openItem);
                menu.Add(new NativeMenuItemSeparator());
                menu.Add(exitItem);

                _trayIcon.Menu = menu;
                _trayIcon.Clicked += (s, e) =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                };
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (_activeModalWindow != null)
            {
                _activeModalWindow.Close();
                _activeModalWindow = null;
            }
            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.GetCurrentPoint(this).Position.Y < 72 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }

        private void OnHomeTabClicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm) vm.SelectedPageIndex = 0;
        }

        private void OnProfilesTabClicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm) vm.SelectedPageIndex = 1;
        }

        private void OnModrinthTabClicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm) vm.SelectedPageIndex = 2;
        }

        private void OnSkinsTabClicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm) vm.SelectedPageIndex = 3;
        }

        private void OnScreenshotsTabClicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedPageIndex = 4;
                _ = vm.ScreenshotsVm.LoadScreenshotsAsync();
            }
        }
    }

    public class ModalOverlayWindow : Window
    {
        private readonly Window _ownerWindow;
        private readonly Action? _onDismiss;
        private bool _isClosing;

        public ModalOverlayWindow(Window owner, Control content, Action? onDismiss = null)
        {
            _ownerWindow = owner;
            _onDismiss = onDismiss;
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SystemDecorations = SystemDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None };
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            CanResize = false;

            SyncBounds();

            _ownerWindow.PositionChanged += OnOwnerMoved;
            _ownerWindow.PropertyChanged += OnOwnerPropertyChanged;

            var rootGrid = new Grid();
            var dimBackdrop = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(176, 0, 0, 0)),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
            };
            rootGrid.Children.Add(dimBackdrop);

            content.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            content.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            rootGrid.Children.Add(content);

            Content = rootGrid;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            SyncBounds();
        }

        private void OnOwnerMoved(object? sender, EventArgs e) => SyncBounds();

        private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == BoundsProperty || e.Property == WindowStateProperty)
            {
                SyncBounds();
            }
        }

        private void SyncBounds()
        {
            if (_ownerWindow == null || !_ownerWindow.IsVisible) return;
            Position = _ownerWindow.Position;
            Width = Math.Max(200, _ownerWindow.Bounds.Width);
            Height = Math.Max(200, _ownerWindow.Bounds.Height);

            if (_ownerWindow.WindowState == WindowState.Minimized && WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Minimized;
            }
            else if (_ownerWindow.WindowState != WindowState.Minimized && WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (!_isClosing)
            {
                _isClosing = true;
                _ownerWindow.PositionChanged -= OnOwnerMoved;
                _ownerWindow.PropertyChanged -= OnOwnerPropertyChanged;
                _onDismiss?.Invoke();
            }
            base.OnClosed(e);
        }
    }
}
