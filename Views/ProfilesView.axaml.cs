using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ServantLauncher.ViewModels;

namespace ServantLauncher.Views
{
    public partial class ProfilesView : UserControl
    {
        private ProfileCardItem? _pendingCard;
        private ProfileCardItem? _draggedCard;
        private Point _startPoint;
        private Point _grabOffset;
        private bool _isDraggingCard;
        private DateTime _lastSwapTime = DateTime.MinValue;

        public ProfilesView()
        {
            InitializeComponent();
        }

        private void OnProfileDragPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) return;

            if (sender is Visual v && v.DataContext is ProfileCardItem cardItem)
            {
                var cardBorder = FindCardBorderFromVisual(v);
                _grabOffset = cardBorder != null ? e.GetPosition(cardBorder) : new Point(90, 250);
                _startPoint = point.Position;
                _pendingCard = cardItem;

                e.Pointer.Capture(this);
                e.Handled = true;
            }
        }

        private void OnRootPointerMoved(object? sender, PointerEventArgs e)
        {
            var curPos = e.GetPosition(this);

            if (_pendingCard != null && !_isDraggingCard)
            {
                var diff = curPos - _startPoint;
                if (Math.Abs(diff.X) > 6 || Math.Abs(diff.Y) > 6)
                {
                    _isDraggingCard = true;
                    _draggedCard = _pendingCard;
                    _draggedCard.IsDragging = true;

                    SetupFloatingProfileCard(_draggedCard);
                    UpdateFloatingCardPosition(curPos);
                    FloatingProfileCard.IsVisible = true;
                }
            }

            if (_isDraggingCard && _draggedCard != null && DataContext is ProfilesViewModel vm)
            {
                UpdateFloatingCardPosition(curPos);

                if ((DateTime.UtcNow - _lastSwapTime).TotalMilliseconds >= 120)
                {
                    var hit = this.InputHitTest(curPos);
                    if (hit is Visual v)
                    {
                        var targetCard = FindCardFromVisual(v);
                        if (targetCard != null && targetCard != _draggedCard)
                        {
                            int oldIdx = vm.ProfileCards.IndexOf(_draggedCard);
                            int newIdx = vm.ProfileCards.IndexOf(targetCard);
                            if (oldIdx >= 0 && newIdx >= 0 && oldIdx != newIdx)
                            {
                                vm.ProfileCards.Move(oldIdx, newIdx);
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
            _pendingCard = null;
            if (_isDraggingCard)
            {
                _isDraggingCard = false;
                FloatingProfileCard.IsVisible = false;

                if (_draggedCard != null)
                {
                    _draggedCard.IsDragging = false;
                    _draggedCard = null;
                }

                if (DataContext is ProfilesViewModel vm)
                {
                    vm.SaveProfileOrder();
                }
            }

            pointer?.Capture(null);
        }

        private void SetupFloatingProfileCard(ProfileCardItem card)
        {
            FloatingProfileName.Text = card.Profile.Name;
            FloatingProfileVersion.Text = card.Profile.GameVersion;
            FloatingProfileLoader.Text = card.IsServerProfile ? "СЕРВЕР" : card.Profile.LoaderType.ToString();

            if (card.Profile.HasCustomIcon && card.Profile.IconBitmap != null)
            {
                FloatingProfileIcon.Source = card.Profile.IconBitmap;
                FloatingProfileIcon.IsVisible = true;
                FloatingProfileDefaultIconBorder.IsVisible = false;
            }
            else
            {
                FloatingProfileIcon.IsVisible = false;
                FloatingProfileDefaultIconBorder.IsVisible = true;
                FloatingProfileDefaultIcon.Data = card.IconGeometry;
            }
        }

        private void UpdateFloatingCardPosition(Point mousePos)
        {
            Canvas.SetLeft(FloatingProfileCard, mousePos.X - _grabOffset.X);
            Canvas.SetTop(FloatingProfileCard, mousePos.Y - _grabOffset.Y);
        }

        private static Border? FindCardBorderFromVisual(Visual? visual)
        {
            Visual? curr = visual;
            while (curr != null)
            {
                if (curr is Border b && b.Classes.Contains("glass-card") && b.DataContext is ProfileCardItem)
                {
                    return b;
                }
                curr = curr.GetVisualParent();
            }
            return null;
        }

        private static ProfileCardItem? FindCardFromVisual(Visual? visual)
        {
            Visual? curr = visual;
            while (curr != null)
            {
                if (curr is Control ctrl && ctrl.DataContext is ProfileCardItem card)
                {
                    return card;
                }
                curr = curr.GetVisualParent();
            }
            return null;
        }

        private async void OnImportMrpackClicked(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите файл сборки / модпака Modrinth (.mrpack)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Modrinth Modpack (*.mrpack)") { Patterns = new[] { "*.mrpack" } }
                }
            });

            if (files.Count > 0 && DataContext is ProfilesViewModel vm)
            {
                var path = files[0].Path.LocalPath;
                if (File.Exists(path))
                {
                    await vm.ImportMrpackFileAsync(path);
                }
            }
        }
    }
}
