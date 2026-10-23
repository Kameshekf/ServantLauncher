using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace ServantLauncher.Controls
{
    public class DotGridControl : Control
    {
        private readonly DispatcherTimer _timer;
        private double _time;
        private Point _mousePos = new(-1000, -1000);
        private Point _targetMousePos = new(-1000, -1000);

        public static readonly StyledProperty<double> DotSizeProperty =
            AvaloniaProperty.Register<DotGridControl, double>(nameof(DotSize), 1.5);

        public static readonly StyledProperty<double> GapProperty =
            AvaloniaProperty.Register<DotGridControl, double>(nameof(Gap), 26.0);

        public static readonly StyledProperty<Color> DotColorProperty =
            AvaloniaProperty.Register<DotGridControl, Color>(nameof(DotColor), Color.FromArgb(40, 168, 85, 247));

        public static readonly StyledProperty<Color> GlowColorProperty =
            AvaloniaProperty.Register<DotGridControl, Color>(nameof(GlowColor), Color.FromArgb(220, 192, 132, 252));

        public static readonly StyledProperty<double> GlowRadiusProperty =
            AvaloniaProperty.Register<DotGridControl, double>(nameof(GlowRadius), 160.0);

        public double DotSize
        {
            get => GetValue(DotSizeProperty);
            set => SetValue(DotSizeProperty, value);
        }

        public double Gap
        {
            get => GetValue(GapProperty);
            set => SetValue(GapProperty, value);
        }

        public Color DotColor
        {
            get => GetValue(DotColorProperty);
            set => SetValue(DotColorProperty, value);
        }

        public Color GlowColor
        {
            get => GetValue(GlowColorProperty);
            set => SetValue(GlowColorProperty, value);
        }

        public double GlowRadius
        {
            get => GetValue(GlowRadiusProperty);
            set => SetValue(GlowRadiusProperty, value);
        }

        public DotGridControl()
        {
            ClipToBounds = true;
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (s, e) =>
            {
                _time += 0.04;
                _mousePos = new Point(
                    _mousePos.X + (_targetMousePos.X - _mousePos.X) * 0.15,
                    _mousePos.Y + (_targetMousePos.Y - _mousePos.Y) * 0.15
                );
                InvalidateVisual();
            });
            _timer.Start();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            _targetMousePos = e.GetPosition(this);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _targetMousePos = new Point(-1000, -1000);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            // Background fill
            var bgBrush = new SolidColorBrush(Color.FromRgb(9, 8, 14));
            context.FillRectangle(bgBrush, new Rect(0, 0, bounds.Width, bounds.Height));

            double gap = Gap;
            double baseSize = DotSize;
            double radius = GlowRadius;

            int cols = (int)Math.Ceiling(bounds.Width / gap) + 1;
            int rows = (int)Math.Ceiling(bounds.Height / gap) + 1;

            var dotBrush = new SolidColorBrush(DotColor);
            var glowBrush = new SolidColorBrush(GlowColor);

            for (int i = 0; i < cols; i++)
            {
                for (int j = 0; j < rows; j++)
                {
                    double x = i * gap;
                    double y = j * gap;

                    double dx = x - _mousePos.X;
                    double dy = y - _mousePos.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    double wave = Math.Sin(_time + (i + j) * 0.15) * 0.5 + 0.5;

                    if (dist < radius)
                    {
                        double factor = 1.0 - (dist / radius);
                        double size = baseSize + factor * 2.5;

                        // Brighter glow dot
                        byte alpha = (byte)Math.Clamp((int)(DotColor.A + factor * (GlowColor.A - DotColor.A)), 0, 255);
                        var activeBrush = new SolidColorBrush(Color.FromArgb(alpha, GlowColor.R, GlowColor.G, GlowColor.B));
                        context.DrawEllipse(activeBrush, null, new Point(x, y), size, size);
                    }
                    else
                    {
                        double size = baseSize + wave * 0.4;
                        context.DrawEllipse(dotBrush, null, new Point(x, y), size, size);
                    }
                }
            }
        }
    }
}
