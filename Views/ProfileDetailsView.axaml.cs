using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ServantLauncher.ViewModels;

namespace ServantLauncher.Views
{
    public partial class ProfileDetailsView : UserControl
    {
        public ProfileDetailsView()
        {
            InitializeComponent();
            AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            AddHandler(DragDrop.DropEvent, OnDrop);
        }

        private void OnDragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                e.DragEffects = DragDropEffects.Copy;
                var overlay = this.FindControl<Border>("ContentDropOverlay");
                if (overlay != null && DataContext is ProfileDetailsViewModel vm && vm.SelectedTabIndex == 0 && !vm.IsSearchingResources)
                {
                    overlay.IsVisible = true;
                }
            }
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private void OnDragLeave(object? sender, DragEventArgs e)
        {
            var overlay = this.FindControl<Border>("ContentDropOverlay");
            if (overlay != null)
            {
                overlay.IsVisible = false;
            }
        }

        private void OnDrop(object? sender, DragEventArgs e)
        {
            var overlay = this.FindControl<Border>("ContentDropOverlay");
            if (overlay != null)
            {
                overlay.IsVisible = false;
            }

            if (e.Data.Contains(DataFormats.Files))
            {
                var paths = new List<string>();

                var files = e.Data.GetFiles();
                if (files != null)
                {
                    foreach (var item in files)
                    {
                        try
                        {
                            var p = item.Path.LocalPath;
                            if (!string.IsNullOrEmpty(p)) paths.Add(p);
                        }
                        catch { }
                    }
                }

                if (paths.Count == 0 && e.Data.Get(DataFormats.Files) is IEnumerable<string> stringPaths)
                {
                    paths.AddRange(stringPaths.Where(p => !string.IsNullOrEmpty(p)));
                }

                if (paths.Count > 0 && DataContext is ProfileDetailsViewModel vm)
                {
                    vm.AddFiles(paths);
                    e.Handled = true;
                }
            }
        }

        private void OnTab0Clicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is ProfileDetailsViewModel vm) vm.SelectedTabIndex = 0;
        }

        private void OnTab1Clicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is ProfileDetailsViewModel vm) vm.SelectedTabIndex = 1;
        }

        private void OnTab2Clicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is ProfileDetailsViewModel vm) vm.SelectedTabIndex = 2;
        }

        private void OnTab3Clicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is ProfileDetailsViewModel vm) vm.SelectedTabIndex = 3;
        }

        private void OnTab4Clicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is ProfileDetailsViewModel vm) vm.SelectedTabIndex = 4;
        }

        private void OnTab5Clicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is ProfileDetailsViewModel vm) vm.SelectedTabIndex = 5;
        }

        private async void OnAddFilesClicked(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Добавить моды, наборы ресурсов или шейдеры",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Пакеты Minecraft (*.jar, *.zip)")
                    {
                        Patterns = new[] { "*.jar", "*.zip" }
                    }
                }
            });

            if (files.Count > 0 && DataContext is ProfileDetailsViewModel vm)
            {
                var paths = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(files, f => f.Path.LocalPath));
                vm.AddFiles(paths);
            }
        }

        private async void OnUploadCustomImageClicked(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Выберите изображение для сборки",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Изображения (*.png, *.jpg, *.webp, *.bmp)")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" }
                    }
                }
            });

            if (files.Count > 0 && DataContext is ProfileDetailsViewModel vm)
            {
                var path = files[0].Path.LocalPath;
                if (System.IO.File.Exists(path))
                {
                    await vm.SetCustomImageAsync(path);
                }
            }
        }

        private async void OnChangeIconClicked(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Выберите картинку для сборки",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Изображения")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" }
                    }
                }
            });

            if (files.Count > 0 && DataContext is ProfileDetailsViewModel vm)
            {
                var path = files[0].Path.LocalPath;
                if (System.IO.File.Exists(path))
                {
                    vm.StartChangeIcon(path);
                }
            }
        }

        private async void OnExportMrpackClicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not ProfileDetailsViewModel vm) return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var cleanName = System.Text.RegularExpressions.Regex.Replace(vm.Profile.Name, @"[^\w\-_\.]+", "-").Trim('-');
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Экспорт сборки в Modrinth .mrpack",
                SuggestedFileName = $"{cleanName}-{vm.Profile.GameVersion}.mrpack",
                DefaultExtension = "mrpack",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Modrinth Modpack (*.mrpack)")
                    {
                        Patterns = new[] { "*.mrpack" }
                    }
                }
            });

            if (file != null)
            {
                var path = file.Path.LocalPath;
                if (!string.IsNullOrEmpty(path))
                {
                    await vm.ExportMrpackCustomPathAsync(path);
                }
            }
        }
    }
}
