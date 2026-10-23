using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ServantLauncher.Models;
using ServantLauncher.Services;

namespace ServantLauncher.Controls
{
    public class SlideshowBackgroundControl : Control
    {
        private readonly List<Bitmap> _bitmaps = new();
        private readonly List<Bitmap> _gifFrames = new();
        private int _gifCurrentFrame = 0;
        private readonly DispatcherTimer _gifTimer;
        private Bitmap? _singleBitmap;
        private int _currentIndex = 0;
        private int _nextIndex = 0;
        private double _transitionProgress = 1.0;
        private readonly DispatcherTimer _slideTimer;
        private readonly DispatcherTimer _animTimer;
        private bool _isGameRunning;
        private IThemeService? _themeService;

        public static readonly DirectProperty<SlideshowBackgroundControl, IThemeService?> ThemeServiceProperty =
            AvaloniaProperty.RegisterDirect<SlideshowBackgroundControl, IThemeService?>(
                nameof(ThemeService),
                o => o.ThemeService,
                (o, v) => o.ThemeService = v);

        public IThemeService? ThemeService
        {
            get => _themeService;
            set
            {
                if (_themeService != null)
                {
                    _themeService.ThemeChanged -= OnThemeChanged;
                    _themeService.GameRunningStateChanged -= OnGameRunningStateChanged;
                }

                SetAndRaise(ThemeServiceProperty, ref _themeService, value);

                if (_themeService != null)
                {
                    _themeService.ThemeChanged += OnThemeChanged;
                    _themeService.GameRunningStateChanged += OnGameRunningStateChanged;
                    ReloadFromTheme();
                }
            }
        }

        public SlideshowBackgroundControl()
        {
            ClipToBounds = true;

            _gifTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Render, (s, e) =>
            {
                if (_isGameRunning) return;
                if (_gifFrames.Count > 1)
                {
                    _gifCurrentFrame = (_gifCurrentFrame + 1) % _gifFrames.Count;
                    InvalidateVisual();
                }
            });

            _slideTimer = new DispatcherTimer(TimeSpan.FromSeconds(8), DispatcherPriority.Background, (s, e) =>
            {
                if (_isGameRunning) return;
                if (_bitmaps.Count > 1)
                {
                    _nextIndex = (_currentIndex + 1) % _bitmaps.Count;
                    _transitionProgress = 0.0;
                }
            });

            _animTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(25), DispatcherPriority.Render, (s, e) =>
            {
                if (_isGameRunning) return;
                if (_transitionProgress < 1.0)
                {
                    _transitionProgress += 0.05;
                    if (_transitionProgress >= 1.0)
                    {
                        _transitionProgress = 1.0;
                        _currentIndex = _nextIndex;
                    }
                    InvalidateVisual();
                }
            });

            // Try resolving theme service from App
            if (App.ThemeServiceInstance != null)
            {
                ThemeService = App.ThemeServiceInstance;
            }
            else
            {
                LoadDefaultScreenshots();
                _slideTimer.Start();
                _animTimer.Start();
            }
        }

        private void OnThemeChanged(ThemeConfig config)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ReloadFromTheme();
                InvalidateVisual();
            });
        }

        private void OnGameRunningStateChanged(bool isRunning)
        {
            _isGameRunning = isRunning;
            Dispatcher.UIThread.Post(() =>
            {
                var cfg = _themeService?.CurrentTheme;
                if (_isGameRunning)
                {
                    // OPTIMIZATION: Stop all background timers when Minecraft is running
                    _slideTimer.Stop();
                    _animTimer.Stop();
                    _gifTimer.Stop();
                }
                else
                {
                    if (_gifFrames.Count > 1) _gifTimer.Start();
                    if (_bitmaps.Count > 1) { _slideTimer.Start(); _animTimer.Start(); }
                }
            });
        }

        public void ReloadFromTheme()
        {
            if (_themeService == null) return;

            var cfg = _themeService.CurrentTheme;

            _slideTimer.Stop();
            _gifTimer.Stop();
            _slideTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(cfg.SlideshowIntervalSeconds, 3, 60));

            // Clean up old bitmaps and gif frames
            foreach (var b in _bitmaps) { try { b.Dispose(); } catch { } }
            _bitmaps.Clear();
            foreach (var f in _gifFrames) { try { f.Dispose(); } catch { } }
            _gifFrames.Clear();
            _gifCurrentFrame = 0;
            _singleBitmap?.Dispose();
            _singleBitmap = null;

            if (cfg.BackgroundMode == BackgroundMode.None)
            {
                // Pure launcher background mode - no media loaded
            }
            else if (cfg.BackgroundMode == BackgroundMode.SingleImage || cfg.BackgroundMode == BackgroundMode.AnimatedGif)
            {
                if (!string.IsNullOrEmpty(cfg.CustomBackgroundImagePath) && File.Exists(cfg.CustomBackgroundImagePath))
                {
                    try
                    {
                        var ext = Path.GetExtension(cfg.CustomBackgroundImagePath).ToLowerInvariant();
                        if (ext == ".gif")
                        {
                            using var img = System.Drawing.Image.FromFile(cfg.CustomBackgroundImagePath);
                            var dimension = new System.Drawing.Imaging.FrameDimension(img.FrameDimensionsList[0]);
                            int frameCount = img.GetFrameCount(dimension);
                            for (int i = 0; i < frameCount; i++)
                            {
                                img.SelectActiveFrame(dimension, i);
                                using var ms = new MemoryStream();
                                img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                ms.Position = 0;
                                _gifFrames.Add(new Bitmap(ms));
                            }
                            if (_gifFrames.Count > 1 && !_isGameRunning)
                            {
                                _gifTimer.Start();
                            }
                        }
                        else
                        {
                            using var stream = File.OpenRead(cfg.CustomBackgroundImagePath);
                            _singleBitmap = new Bitmap(stream);
                        }
                    }
                    catch { }
                }
            }
            else if (cfg.BackgroundMode == BackgroundMode.CustomSlideshow)
            {
                if (cfg.SlideshowImages != null && cfg.SlideshowImages.Count > 0)
                {
                    foreach (var path in cfg.SlideshowImages)
                    {
                        if (File.Exists(path))
                        {
                            try
                            {
                                using var stream = File.OpenRead(path);
                                _bitmaps.Add(new Bitmap(stream));
                            }
                            catch { }
                        }
                    }
                }

                if (!_isGameRunning && _bitmaps.Count > 1)
                {
                    _slideTimer.Start();
                    _animTimer.Start();
                }
            }
            else if (cfg.BackgroundMode == BackgroundMode.Slideshow) // Скриншоты с сервера
            {
                LoadDefaultScreenshots();

                if (!_isGameRunning && _bitmaps.Count > 1)
                {
                    _slideTimer.Start();
                    _animTimer.Start();
                }
            }

            _currentIndex = 0;
            _nextIndex = 0;
            _transitionProgress = 1.0;
            InvalidateVisual();
        }

        private void LoadDefaultScreenshots()
        {
            var searchPaths = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".servantlauncher", "server_screenshots"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Images", "Screenshots"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Images", "Renders")
            };

            var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in searchPaths)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        foreach (var f in Directory.GetFiles(dir, "*.*"))
                        {
                            var ext = Path.GetExtension(f).ToLowerInvariant();
                            if ((ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp") && loadedPaths.Add(f))
                            {
                                try
                                {
                                    using var stream = File.OpenRead(f);
                                    _bitmaps.Add(new Bitmap(stream));
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            var cfg = _themeService?.CurrentTheme;
            Color bgColor = Color.FromRgb(9, 8, 14);
            if (cfg != null && !string.IsNullOrEmpty(cfg.BgDarkHex))
            {
                try { bgColor = Color.Parse(cfg.BgDarkHex); } catch { }
            }

            // 1. Base Dark Fill
            context.FillRectangle(new SolidColorBrush(bgColor), new Rect(0, 0, bounds.Width, bounds.Height));

            // If None mode, stop here and only render solid theme color + subtle glow!
            if (cfg != null && cfg.BackgroundMode == BackgroundMode.None)
            {
                return;
            }

            // 2. Image Render (Single or Slideshow)
            if (cfg != null && (cfg.BackgroundMode == BackgroundMode.SingleImage || cfg.BackgroundMode == BackgroundMode.AnimatedGif))
            {
                if (_gifFrames.Count > 0)
                {
                    var gifBmp = _gifFrames[_gifCurrentFrame % _gifFrames.Count];
                    DrawScaledImage(context, gifBmp, bounds, 1.0);
                }
                else if (_singleBitmap != null)
                {
                    DrawScaledImage(context, _singleBitmap, bounds, 1.0);
                }
            }
            else if (_bitmaps.Count > 0 && (cfg == null || cfg.BackgroundMode == BackgroundMode.Slideshow || cfg.BackgroundMode == BackgroundMode.CustomSlideshow))
            {
                var currentBmp = _bitmaps[_currentIndex % _bitmaps.Count];
                DrawScaledImage(context, currentBmp, bounds, 1.0 - (_transitionProgress < 1.0 ? _transitionProgress * 0.5 : 0.0));

                if (_transitionProgress < 1.0 && _nextIndex < _bitmaps.Count)
                {
                    var nextBmp = _bitmaps[_nextIndex];
                    DrawScaledImage(context, nextBmp, bounds, _transitionProgress);
                }
            }

            // 3. Dark Dimming & Blur Overlay
            int dimPercent = cfg?.DimOverlayPercent ?? 45;
            byte overlayAlpha = (byte)Math.Clamp((int)(dimPercent * 2.55), 40, 255);

            var overlayBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(Color.FromArgb(overlayAlpha, bgColor.R, bgColor.G, bgColor.B), 0.0),
                    new GradientStop(Color.FromArgb((byte)Math.Min(255, overlayAlpha + 15), bgColor.R, bgColor.G, bgColor.B), 0.5),
                    new GradientStop(Color.FromArgb((byte)Math.Min(255, overlayAlpha + 25), bgColor.R, bgColor.G, bgColor.B), 1.0)
                }
            };
            context.FillRectangle(overlayBrush, new Rect(0, 0, bounds.Width, bounds.Height));

            // 4. Corner Accent Ambient Glow
            Color accentColor = Color.FromRgb(124, 58, 237);
            if (cfg != null && !string.IsNullOrEmpty(cfg.AccentColorHex))
            {
                try { accentColor = Color.Parse(cfg.AccentColorHex); } catch { }
            }

            var glowBrush = new RadialGradientBrush
            {
                Center = new RelativePoint(0.85, 0.15, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.85, 0.15, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(Color.FromArgb(45, accentColor.R, accentColor.G, accentColor.B), 0.0),
                    new GradientStop(Color.FromArgb(0, accentColor.R, accentColor.G, accentColor.B), 1.0)
                }
            };
            context.FillRectangle(glowBrush, new Rect(0, 0, bounds.Width, bounds.Height));
        }

        private static void DrawScaledImage(DrawingContext context, Bitmap bmp, Rect bounds, double opacity)
        {
            if (bmp.Size.Width <= 0 || bmp.Size.Height <= 0) return;

            double bmpRatio = bmp.Size.Width / bmp.Size.Height;
            double viewRatio = bounds.Width / bounds.Height;

            double destW, destH;
            if (viewRatio > bmpRatio)
            {
                destW = bounds.Width;
                destH = bounds.Width / bmpRatio;
            }
            else
            {
                destH = bounds.Height;
                destW = bounds.Height * bmpRatio;
            }

            double destX = (bounds.Width - destW) / 2.0;
            double destY = (bounds.Height - destH) / 2.0;

            var destRect = new Rect(destX, destY, destW, destH);
            var srcRect = new Rect(0, 0, bmp.Size.Width, bmp.Size.Height);

            using (context.PushOpacity(Math.Clamp(opacity, 0.0, 1.0)))
            {
                context.DrawImage(bmp, srcRect, destRect);
            }
        }
    }
}

