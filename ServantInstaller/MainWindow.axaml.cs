using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace ServantInstaller
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        public MainWindow(InstallerViewModel vm) : this()
        {
            DataContext = vm;
            vm.CloseRequested += Close;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.GetCurrentPoint(this).Position.Y < 56 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }

        private async void OnBrowseInstallDirClicked(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Выберите папку для установки Servant Launcher",
                AllowMultiple = false
            });

            if (folders.Count > 0 && DataContext is InstallerViewModel vm)
            {
                vm.InstallDirectory = folders[0].Path.LocalPath;
            }
        }
    }
}
