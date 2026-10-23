using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ServantLauncher.Controls
{
    public partial class HexColorPickerControl : UserControl
    {
        public static readonly StyledProperty<string> SelectedHexProperty =
            AvaloniaProperty.Register<HexColorPickerControl, string>(nameof(SelectedHex), "#7C3AED");

        public string SelectedHex
        {
            get => GetValue(SelectedHexProperty);
            set => SetValue(SelectedHexProperty, value);
        }

        private double _hue = 262; // 0..360
        private double _saturation = 0.75; // 0..1
        private double _value = 0.93; // 0..1
        private bool _isDraggingSv = false;
        private bool _isUpdating = false;

        public HexColorPickerControl()
        {
            InitializeComponent();
            SelectedHexProperty.Changed.AddClassHandler<HexColorPickerControl>((c, _) => c.OnSelectedHexPropertyChanged());
            Loaded += (s, e) => UpdateFromHex(SelectedHex);
        }

        private void OnSelectedHexPropertyChanged()
        {
            if (_isUpdating) return;
            UpdateFromHex(SelectedHex);
        }

        private void UpdateFromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return;
            try
            {
                if (!hex.StartsWith("#")) hex = "#" + hex;
                if (hex.Length != 7 && hex.Length != 9) return;

                var color = Color.Parse(hex);
                ColorToHsv(color, out _hue, out _saturation, out _value);

                _isUpdating = true;
                HueSlider.Value = _hue;
                UpdateUiForHsv();
                _isUpdating = false;
            }
            catch { }
        }

        private void OnHueSliderValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdating) return;
            _hue = e.NewValue;
            UpdateUiForHsv();
            NotifyColorChanged();
        }

        private void OnSvPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _isDraggingSv = true;
            e.Pointer.Capture(SvPanel);
            UpdateSvFromPointer(e.GetPosition(SvPanel));
        }

        private void OnSvPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isDraggingSv)
            {
                UpdateSvFromPointer(e.GetPosition(SvPanel));
            }
        }

        private void OnSvPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_isDraggingSv)
            {
                _isDraggingSv = false;
                e.Pointer.Capture(null);
            }
        }

        private void UpdateSvFromPointer(Point pos)
        {
            double w = SvPanel.Bounds.Width;
            double h = SvPanel.Bounds.Height;
            if (w <= 0 || h <= 0) return;

            _saturation = Math.Clamp(pos.X / w, 0.0, 1.0);
            _value = Math.Clamp(1.0 - (pos.Y / h), 0.0, 1.0);

            UpdateUiForHsv();
            NotifyColorChanged();
        }

        private void UpdateUiForHsv()
        {
            // 1. Pure Hue background for the 2D panel
            var pureHueColor = HsvToColor(_hue, 1.0, 1.0);
            HueBackgroundBorder.Background = new SolidColorBrush(pureHueColor);

            // 2. Compute final RGB color
            var currentColor = HsvToColor(_hue, _saturation, _value);
            var colorBrush = new SolidColorBrush(currentColor);
            ColorPreviewBorder.Background = colorBrush;

            // 3. Move circle indicator
            double w = SvPanel.Bounds.Width > 0 ? SvPanel.Bounds.Width : 200;
            double h = SvPanel.Bounds.Height > 0 ? SvPanel.Bounds.Height : 160;
            Canvas.SetLeft(SvSelector, _saturation * w);
            Canvas.SetTop(SvSelector, (1.0 - _value) * h);

            // 4. Hex input text
            string hexStr = $"#{currentColor.R:X2}{currentColor.G:X2}{currentColor.B:X2}";
            if (HexInputBox.Text?.ToUpperInvariant() != hexStr.ToUpperInvariant())
            {
                HexInputBox.Text = hexStr;
            }

            // 5. RGB display
            RgbText.Text = $"{currentColor.R}, {currentColor.G}, {currentColor.B}";

            // 6. CMYK display
            double r = currentColor.R / 255.0;
            double g = currentColor.G / 255.0;
            double b = currentColor.B / 255.0;
            double k = 1.0 - Math.Max(r, Math.Max(g, b));
            double c = k < 1.0 ? (1.0 - r - k) / (1.0 - k) : 0;
            double m = k < 1.0 ? (1.0 - g - k) / (1.0 - k) : 0;
            double y = k < 1.0 ? (1.0 - b - k) / (1.0 - k) : 0;
            CmykText.Text = $"{(int)(c*100)}%, {(int)(m*100)}%, {(int)(y*100)}%, {(int)(k*100)}%";

            // 7. HSV display
            HsvText.Text = $"{(int)_hue}°, {(int)(_saturation*100)}%, {(int)(_value*100)}%";

            // 8. HSL display
            double l = _value * (1.0 - _saturation / 2.0);
            double hslS = (l == 0 || l == 1) ? 0 : (_value - l) / Math.Min(l, 1 - l);
            HslText.Text = $"{(int)_hue}°, {(int)(hslS*100)}%, {(int)(l*100)}%";
        }

        private void NotifyColorChanged()
        {
            _isUpdating = true;
            var c = HsvToColor(_hue, _saturation, _value);
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            SelectedHex = hex;
            _isUpdating = false;
        }

        private void OnHexInputTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;
            var text = HexInputBox.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            if (!text.StartsWith("#")) text = "#" + text;

            if (text.Length == 7 && Color.TryParse(text, out var color))
            {
                _isUpdating = true;
                SelectedHex = text;
                ColorToHsv(color, out _hue, out _saturation, out _value);
                HueSlider.Value = _hue;
                UpdateUiForHsv();
                _isUpdating = false;
            }
        }

        private async void OnCopyHexClicked(object? sender, RoutedEventArgs e)
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard != null && !string.IsNullOrEmpty(SelectedHex))
            {
                await top.Clipboard.SetTextAsync(SelectedHex);
            }
        }

        public static Color HsvToColor(double h, double s, double v)
        {
            h = (h % 360 + 360) % 360;
            int hi = (int)Math.Floor(h / 60) % 6;
            double f = h / 60 - Math.Floor(h / 60);

            v *= 255;
            byte vByte = (byte)Math.Clamp((int)v, 0, 255);
            byte p = (byte)Math.Clamp((int)(v * (1 - s)), 0, 255);
            byte q = (byte)Math.Clamp((int)(v * (1 - f * s)), 0, 255);
            byte t = (byte)Math.Clamp((int)(v * (1 - (1 - f) * s)), 0, 255);

            return hi switch
            {
                0 => Color.FromRgb(vByte, t, p),
                1 => Color.FromRgb(q, vByte, p),
                2 => Color.FromRgb(p, vByte, t),
                3 => Color.FromRgb(p, q, vByte),
                4 => Color.FromRgb(t, p, vByte),
                _ => Color.FromRgb(vByte, p, q)
            };
        }

        public static void ColorToHsv(Color color, out double h, out double s, out double v)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            v = max;
            s = max == 0 ? 0 : delta / max;

            if (delta == 0)
            {
                h = 0;
            }
            else if (max == r)
            {
                h = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                h = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                h = 60 * (((r - g) / delta) + 4);
            }

            if (h < 0) h += 360;
        }
    }
}
