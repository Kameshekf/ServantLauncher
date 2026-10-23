using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ServantLauncher.ViewModels;

namespace ServantLauncher.Views
{
    public partial class ImageCropDialog : UserControl
    {
        private bool _isDragging;
        private Point _lastPoint;

        public ImageCropDialog()
        {
            InitializeComponent();
        }

        private void OnCropPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _isDragging = true;
                _lastPoint = e.GetPosition(this);
                e.Pointer.Capture(sender as Control);
                e.Handled = true;
            }
        }

        private void OnCropPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isDragging && DataContext is ImageCropViewModel vm)
            {
                var currentPoint = e.GetPosition(this);
                var delta = currentPoint - _lastPoint;
                _lastPoint = currentPoint;

                vm.PanBy(delta.X, delta.Y);
                e.Handled = true;
            }
        }

        private void OnCropPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                e.Pointer.Capture(null);
                e.Handled = true;
            }
        }

        private void OnCropPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (DataContext is ImageCropViewModel vm)
            {
                double zoomDelta = e.Delta.Y * 0.1;
                vm.Zoom = System.Math.Clamp(vm.Zoom + zoomDelta, 0.2, 5.0);
                e.Handled = true;
            }
        }
    }
}
