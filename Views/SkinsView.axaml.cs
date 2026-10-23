using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ServantLauncher.Services;
using ServantLauncher.ViewModels;

namespace ServantLauncher.Views
{
    public partial class SkinsView : UserControl
    {
        private SkinItem? _pendingSkin;
        private SkinItem? _draggedSkin;
        private Point _startPoint;
        private Point _grabOffset;
        private bool _isDraggingSkin;
        private DateTime _lastSwapTime = DateTime.MinValue;

        public SkinsView()
        {
            InitializeComponent();
        }

        private void OnSkinDragPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) return;

            if (sender is Visual v && v.DataContext is SkinItem skin)
            {
                var cardBorder = FindCardBorderFromVisual(v);
                _grabOffset = cardBorder != null ? e.GetPosition(cardBorder) : new Point(70, 160);
                _startPoint = point.Position;
                _pendingSkin = skin;

                e.Pointer.Capture(this);
                e.Handled = true;
            }
        }

        private void OnRootPointerMoved(object? sender, PointerEventArgs e)
        {
            var curPos = e.GetPosition(this);

            if (_pendingSkin != null && !_isDraggingSkin)
            {
                var diff = curPos - _startPoint;
                if (Math.Abs(diff.X) > 6 || Math.Abs(diff.Y) > 6)
                {
                    _isDraggingSkin = true;
                    _draggedSkin = _pendingSkin;
                    _draggedSkin.IsDragging = true;

                    SetupFloatingSkinCard(_draggedSkin);
                    UpdateFloatingCardPosition(curPos);
                    FloatingSkinCard.IsVisible = true;
                }
            }

            if (_isDraggingSkin && _draggedSkin != null && DataContext is SkinsViewModel vm)
            {
                UpdateFloatingCardPosition(curPos);

                if ((DateTime.UtcNow - _lastSwapTime).TotalMilliseconds >= 120)
                {
                    var hit = this.InputHitTest(curPos);
                    if (hit is Visual v)
                    {
                        var targetSkin = FindSkinFromVisual(v);
                        if (targetSkin != null && targetSkin != _draggedSkin)
                        {
                            int oldIdx = vm.LibrarySkins.IndexOf(_draggedSkin);
                            int newIdx = vm.LibrarySkins.IndexOf(targetSkin);
                            if (oldIdx >= 0 && newIdx >= 0 && oldIdx != newIdx)
                            {
                                vm.LibrarySkins.Move(oldIdx, newIdx);
                                _lastSwapTime = DateTime.UtcNow;
                            }
                        }
                    }
                }

                e.Handled = true;
            }
        }

        private void OnRootPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            EndDrag(e.Pointer);
            e.Handled = true;
        }

        private void OnRootPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            EndDrag(null);
        }

        private void EndDrag(IPointer? pointer)
        {
            _pendingSkin = null;
            if (_isDraggingSkin)
            {
                _isDraggingSkin = false;
                FloatingSkinCard.IsVisible = false;

                if (_draggedSkin != null)
                {
                    _draggedSkin.IsDragging = false;
                    _draggedSkin = null;
                }

                if (DataContext is SkinsViewModel vm)
                {
                    vm.SaveSkinOrder();
                }
            }

            pointer?.Capture(null);
        }

        private void SetupFloatingSkinCard(SkinItem skin)
        {
            FloatingSkinName.Text = skin.Name;
            FloatingSkinPreview.Source = skin.PreviewBitmap;
        }

        private void UpdateFloatingCardPosition(Point mousePos)
        {
            Canvas.SetLeft(FloatingSkinCard, mousePos.X - _grabOffset.X);
            Canvas.SetTop(FloatingSkinCard, mousePos.Y - _grabOffset.Y);
        }

        private static Border? FindCardBorderFromVisual(Visual? visual)
        {
            Visual? curr = visual;
            while (curr != null)
            {
                if (curr is Border b && b.DataContext is SkinItem)
                {
                    return b;
                }
                curr = curr.GetVisualParent();
            }
            return null;
        }

        private static SkinItem? FindSkinFromVisual(Visual? visual)
        {
            Visual? curr = visual;
            while (curr != null)
            {
                if (curr is Control ctrl && ctrl.DataContext is SkinItem skin)
                {
                    return skin;
                }
                curr = curr.GetVisualParent();
            }
            return null;
        }

        private async void OnAddSkinClicked(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите файл скина Minecraft (.png)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } }
                }
            });

            if (files.Count > 0 && DataContext is SkinsViewModel vm)
            {
                var path = files[0].Path.LocalPath;
                if (File.Exists(path))
                {
                    vm.OpenAddSkinDialog(path);
                }
            }
        }

        private async void OnChooseNewSkinFileClicked(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите новый файл текстуры скина (.png)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } }
                }
            });

            if (files.Count > 0 && DataContext is SkinsViewModel vm)
            {
                var path = files[0].Path.LocalPath;
                if (File.Exists(path))
                {
                    vm.SetEditSkinNewFile(path);
                }
            }
        }
    }
}