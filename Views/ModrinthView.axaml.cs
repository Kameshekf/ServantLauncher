using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ServantLauncher.ViewModels;

namespace ServantLauncher.Views
{
    public partial class ModrinthView : UserControl
    {
        public ModrinthView()
        {
            InitializeComponent();
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
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

        private void OnDrop(object? sender, DragEventArgs e)
        {
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

                if (paths.Count > 0 && DataContext is ModrinthViewModel vm)
                {
                    vm.AddFiles(paths);
                    e.Handled = true;
                }
            }
        }

        private void OnCatalogTabClicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is ModrinthViewModel vm)
            {
                vm.SelectedTabIndex = 0;
            }
        }

        private void OnInstalledTabClicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is ModrinthViewModel vm)
            {
                vm.SelectedTabIndex = 1;
            }
        }
    }
}
