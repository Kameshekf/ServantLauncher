using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ServantLauncher.ViewModels;

namespace ServantLauncher.Views
{
    public partial class SettingsView : UserControl
    {
        public event Action? CloseRequested;

        public SettingsView()
        {
            InitializeComponent();
        }

        private async void OnBrowseJavaClicked(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите исполняемый файл Java (javaw.exe / java.exe)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Java Executable (*.exe)")
                    {
                        Patterns = new[] { "javaw.exe", "java.exe", "*.exe" }
                    }
                }
            });

            if (files.Count > 0 && DataContext is SettingsViewModel vm)
            {
                vm.CustomJavaPath = files[0].Path.LocalPath;
                vm.CheckJavaSettings();
            }
        }

        private async void OnAddSlideshowImagesClicked(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите изображения для слайдшоу",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Изображения (*.png;*.jpg;*.jpeg;*.webp)")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" }
                    }
                }
            });

            if (files.Count > 0 && DataContext is SettingsViewModel vm)
            {
                foreach (var f in files)
                {
                    var path = f.Path.LocalPath;
                    if (!vm.Theme.SlideshowImages.Contains(path))
                    {
                        vm.Theme.SlideshowImages.Add(path);
                    }
                }
                vm.SaveTheme();
            }
        }

        private async void OnSelectSingleImageClicked(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите фоновое изображение или GIF",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Изображения (*.png;*.jpg;*.jpeg;*.gif;*.webp)")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp" }
                    }
                }
            });

            if (files.Count > 0 && DataContext is SettingsViewModel vm)
            {
                vm.Theme.SingleImagePath = files[0].Path.LocalPath;
                vm.SaveTheme();
            }
        }

        private void OnCloseClicked(object? sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke();
        }
    }
}
