using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace ServantLauncher.Controls
{
    /// <summary>
    /// Pure C# and SkiaSharp 3D Minecraft Skin Renderer with 100% true alpha transparency,
    /// interactive mouse drag (360° rotation), mouse wheel zoom, idle breathing animation,
    /// support for Classic (4px) and Slim (3px) models, and outer overlay layers.
    /// </summary>
    public class MinecraftSkin3DControl : Control
    {
        public static readonly StyledProperty<string?> SkinSourceProperty =
            AvaloniaProperty.Register<MinecraftSkin3DControl, string?>(nameof(SkinSource));

        public static readonly StyledProperty<SKBitmap?> DirectSkinBitmapProperty =
            AvaloniaProperty.Register<MinecraftSkin3DControl, SKBitmap?>(nameof(DirectSkinBitmap));

        public static readonly StyledProperty<string> ModelProperty =
            AvaloniaProperty.Register<MinecraftSkin3DControl, string>(nameof(Model), "classic");

        public static readonly StyledProperty<string?> CapeSourceProperty =
            AvaloniaProperty.Register<MinecraftSkin3DControl, string?>(nameof(CapeSource));

        public static readonly StyledProperty<bool> IsAnimatedProperty =
            AvaloniaProperty.Register<MinecraftSkin3DControl, bool>(nameof(IsAnimated), true);

        public static readonly StyledProperty<bool> AutoDetectModelProperty =
            AvaloniaProperty.Register<MinecraftSkin3DControl, bool>(nameof(AutoDetectModel), defaultValue: true);

        public string? SkinSource
        {
            get => GetValue(SkinSourceProperty);
            set => SetValue(SkinSourceProperty, value);
        }

        public SKBitmap? DirectSkinBitmap
        {
            get => GetValue(DirectSkinBitmapProperty);
            set => SetValue(DirectSkinBitmapProperty, value);
        }

        public string Model
        {
            get => GetValue(ModelProperty);
            set => SetValue(ModelProperty, value);
        }

        public bool AutoDetectModel
        {
            get => GetValue(AutoDetectModelProperty);
            set => SetValue(AutoDetectModelProperty, value);
        }

        public string? CapeSource
        {
            get => GetValue(CapeSourceProperty);
            set => SetValue(CapeSourceProperty, value);
        }

        public bool IsAnimated
        {
            get => GetValue(IsAnimatedProperty);
            set => SetValue(IsAnimatedProperty, value);
        }

        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10),
            DefaultRequestHeaders = { { "User-Agent", "ServantLauncher/1.0" } }
        };

        private SKBitmap? _skinBitmap;
        private SKBitmap? _capeBitmap;
        private static SKBitmap? _defaultSteveBitmap;

        private WriteableBitmap? _renderBitmap;
        private readonly DispatcherTimer _animTimer;
        private float _animTime;

        // Interactive camera angles (horizontal-only rotation)
        private float _yaw = -25.0f;     // Default nice 3/4 angle
        private const float _pitch = 0.0f; // Level upright camera (no forward tilt)
        private const float _zoom = 1.0f;  // Fixed character scale (no zooming)

        private bool _isDragging;
        private Point _dragStartPoint;
        private float _dragStartYaw;

        private CancellationTokenSource? _skinLoadCts;
        private CancellationTokenSource? _capeLoadCts;

        static MinecraftSkin3DControl()
        {
            AffectsRender<MinecraftSkin3DControl>(SkinSourceProperty, ModelProperty, CapeSourceProperty, IsAnimatedProperty, DirectSkinBitmapProperty);

            SkinSourceProperty.Changed.AddClassHandler<MinecraftSkin3DControl>((c, _) => c.OnSkinSourceChanged());
            DirectSkinBitmapProperty.Changed.AddClassHandler<MinecraftSkin3DControl>((c, _) => c.InvalidateVisual());
            ModelProperty.Changed.AddClassHandler<MinecraftSkin3DControl>((c, _) => c.InvalidateVisual());
            CapeSourceProperty.Changed.AddClassHandler<MinecraftSkin3DControl>((c, _) => c.OnCapeSourceChanged());
            IsVisibleProperty.Changed.AddClassHandler<MinecraftSkin3DControl>((c, _) => c.UpdateAnimationState());
        }

        public MinecraftSkin3DControl()
        {
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.SizeWestEast);

            _animTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (s, e) =>
            {
                if (!IsEffectivelyVisible || !IsVisible || !IsAnimated) return;
                _animTime += 0.05f;
                InvalidateVisual();
            });

            // Prepare default Steve skin
            if (_defaultSteveBitmap == null)
            {
                _defaultSteveBitmap = GenerateDefaultSteveSkin();
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            UpdateAnimationState();
            OnSkinSourceChanged();
            OnCapeSourceChanged();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _animTimer.Stop();
            base.OnDetachedFromVisualTree(e);
        }

        private void UpdateAnimationState()
        {
            if (IsEffectivelyVisible && IsVisible && IsAnimated)
            {
                if (!_animTimer.IsEnabled) _animTimer.Start();
            }
            else
            {
                _animTimer.Stop();
            }
        }

        private void OnSkinSourceChanged()
        {
            _skinLoadCts?.Cancel();
            _skinLoadCts = new CancellationTokenSource();
            var token = _skinLoadCts.Token;

            var src = SkinSource;
            if (string.IsNullOrWhiteSpace(src))
            {
                _skinBitmap = null;
                InvalidateVisual();
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    SKBitmap? loaded = null;
                    if (src.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                    {
                        var commaIdx = src.IndexOf(',');
                        if (commaIdx >= 0)
                        {
                            var base64 = src[(commaIdx + 1)..];
                            var bytes = Convert.FromBase64String(base64);
                            using var ms = new MemoryStream(bytes);
                            loaded = SKBitmap.Decode(ms);
                        }
                    }
                    else if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        var bytes = await _httpClient.GetByteArrayAsync(src, token);
                        using var ms = new MemoryStream(bytes);
                        loaded = SKBitmap.Decode(ms);
                    }
                    else if (File.Exists(src))
                    {
                        using var stream = File.OpenRead(src);
                        loaded = SKBitmap.Decode(stream);
                    }

                    if (loaded != null)
                    {
                        if (loaded.Height == 32)
                        {
                            loaded = ExpandLegacy32Skin(loaded);
                        }

                        var detectedModel = DetectSkinModel(loaded);

                        Dispatcher.UIThread.Post(() =>
                        {
                            if (token.IsCancellationRequested)
                            {
                                loaded.Dispose();
                                return;
                            }
                            _skinBitmap?.Dispose();
                            _skinBitmap = loaded;
                            if (AutoDetectModel)
                            {
                                SetCurrentValue(ModelProperty, detectedModel);
                            }
                            InvalidateVisual();
                        });
                    }
                }
                catch { }
            }, token);
        }

        private void OnCapeSourceChanged()
        {
            _capeLoadCts?.Cancel();
            _capeLoadCts = new CancellationTokenSource();
            var token = _capeLoadCts.Token;

            var src = CapeSource;
            if (string.IsNullOrWhiteSpace(src))
            {
                _capeBitmap = null;
                InvalidateVisual();
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    SKBitmap? loaded = null;
                    if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        var bytes = await _httpClient.GetByteArrayAsync(src, token);
                        using var ms = new MemoryStream(bytes);
                        loaded = SKBitmap.Decode(ms);
                    }
                    else if (File.Exists(src))
                    {
                        using var stream = File.OpenRead(src);
                        loaded = SKBitmap.Decode(stream);
                    }

                    if (loaded != null)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (token.IsCancellationRequested)
                            {
                                loaded.Dispose();
                                return;
                            }
                            _capeBitmap?.Dispose();
                            _capeBitmap = loaded;
                            InvalidateVisual();
                        });
                    }
                }
                catch { }
            }, token);
        }

        #region Pointer Interaction (Horizontal Rotation Only, No Zoom)

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var current = e.GetCurrentPoint(this);
            if (current.Properties.IsLeftButtonPressed)
            {
                _isDragging = true;
                _dragStartPoint = current.Position;
                _dragStartYaw = _yaw;
                e.Pointer.Capture(this);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_isDragging)
            {
                var pos = e.GetCurrentPoint(this).Position;
                var dx = (float)(pos.X - _dragStartPoint.X);

                // Horizontal-only rotation
                _yaw = _dragStartYaw + dx * 0.75f;

                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_isDragging)
            {
                _isDragging = false;
                e.Pointer.Capture(null);
                e.Handled = true;
            }
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _isDragging = false;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            // Scaling / zooming is disabled per user request
        }

        #endregion

        #region 3D Rendering Pipeline

        private struct FaceQuad
        {
            public SKPoint P0, P1, P2, P3;
            public SKPoint UV0, UV1, UV2, UV3;
            public SKColor Color;
            public float CenterZ;
            public SKBitmap Texture;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            int width = (int)Math.Max(32, Bounds.Width);
            int height = (int)Math.Max(32, Bounds.Height);

            if (_renderBitmap == null || _renderBitmap.PixelSize.Width != width || _renderBitmap.PixelSize.Height != height)
            {
                _renderBitmap?.Dispose();
                _renderBitmap = new WriteableBitmap(
                    new PixelSize(width, height),
                    new Avalonia.Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul);
            }

            var skin = DirectSkinBitmap ?? _skinBitmap ?? _defaultSteveBitmap!;
            bool isSlim = string.Equals(Model, "slim", StringComparison.OrdinalIgnoreCase);

            using (var fb = _renderBitmap.Lock())
            {
                var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info, fb.Address, fb.RowBytes);
                var canvas = surface.Canvas;

                // 100% True Transparent Background!
                canvas.Clear(SKColors.Transparent);

                // Subtle animation angles
                float breatheY = IsAnimated ? MathF.Sin(_animTime * 2.0f) * 0.35f : 0f;
                float armSwing = IsAnimated ? MathF.Sin(_animTime * 2.0f) * 3.5f : 0f;
                float legSwing = IsAnimated ? MathF.Cos(_animTime * 2.0f) * 1.5f : 0f;
                float headNod = IsAnimated ? MathF.Sin(_animTime * 1.0f) * 1.2f : 0f;

                float radYaw = _yaw * MathF.PI / 180.0f;
                float radPitch = _pitch * MathF.PI / 180.0f;

                float cosYaw = MathF.Cos(radYaw);
                float sinYaw = MathF.Sin(radYaw);
                float cosPitch = MathF.Cos(radPitch);
                float sinPitch = MathF.Sin(radPitch);

                float centerX = width * 0.5f;
                float centerY = height * 0.52f;
                float scale = (_zoom * MathF.Min(width, height)) / 36.0f;

                // Realistic soft floor shadow beneath feet with radial gradient falloff
                Vector3 floorPos = TransformVertex(new Vector3(0, -16f, 0), cosYaw, sinYaw, cosPitch, sinPitch);
                float shadowScale = 120.0f / (120.0f - floorPos.Z);
                float shadowX = centerX + floorPos.X * scale * shadowScale;
                float shadowY = centerY - floorPos.Y * scale * shadowScale;

                float shadowRadiusX = 6.8f * scale * shadowScale;
                float shadowRadiusY = 2.5f * scale * shadowScale;

                if (shadowRadiusX > 1f && shadowRadiusY > 1f)
                {
                    // 1. Soft ambient outer shadow
                    using (var shadowPaint = new SKPaint
                    {
                        IsAntialias = true,
                        Style = SKPaintStyle.Fill
                    })
                    {
                        using var outerShader = SKShader.CreateRadialGradient(
                            new SKPoint(0, 0),
                            1.0f,
                            new[]
                            {
                                new SKColor(0, 0, 0, 95),
                                new SKColor(0, 0, 0, 50),
                                new SKColor(0, 0, 0, 15),
                                new SKColor(0, 0, 0, 0)
                            },
                            new[] { 0.0f, 0.45f, 0.8f, 1.0f },
                            SKShaderTileMode.Clamp);

                        canvas.Save();
                        canvas.Translate(shadowX, shadowY);
                        canvas.Scale(shadowRadiusX, shadowRadiusY);
                        shadowPaint.Shader = outerShader;
                        canvas.DrawCircle(0, 0, 1.0f, shadowPaint);
                        canvas.Restore();
                    }

                    // 2. Direct foot contact shadow (ambient occlusion)
                    float contactRadiusX = 4.4f * scale * shadowScale;
                    float contactRadiusY = 1.3f * scale * shadowScale;
                    using (var contactPaint = new SKPaint
                    {
                        IsAntialias = true,
                        Style = SKPaintStyle.Fill
                    })
                    {
                        using var contactShader = SKShader.CreateRadialGradient(
                            new SKPoint(0, 0),
                            1.0f,
                            new[]
                            {
                                new SKColor(0, 0, 0, 75),
                                new SKColor(0, 0, 0, 30),
                                new SKColor(0, 0, 0, 0)
                            },
                            new[] { 0.0f, 0.55f, 1.0f },
                            SKShaderTileMode.Clamp);

                        canvas.Save();
                        canvas.Translate(shadowX, shadowY);
                        canvas.Scale(contactRadiusX, contactRadiusY);
                        contactPaint.Shader = contactShader;
                        canvas.DrawCircle(0, 0, 1.0f, contactPaint);
                        canvas.Restore();
                    }
                }

                // Collect and project all 3D faces
                var faces = new List<FaceQuad>(128);
                float armW = isSlim ? 3.0f : 4.0f;

                // Directional light vector (top-left-front)
                var lightDir = Vector3.Normalize(new Vector3(-0.35f, 0.75f, 0.55f));

                // 1. HEAD & HAT
                AddBoxFaces(faces, skin,
                    min: new Vector3(-4f, 8f + breatheY, -4f),
                    max: new Vector3(4f, 16f + breatheY, 4f),
                    uvMap: new UVMap(8, 0, 16, 0, 8, 8, 24, 8, 0, 8, 16, 8, 8, 8, 8),
                    cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir,
                    headNod * MathF.PI / 180f, 0f, 0f, new Vector3(0, 8f + breatheY, 0));

                AddBoxFaces(faces, skin,
                    min: new Vector3(-4.5f, 7.5f + breatheY, -4.5f),
                    max: new Vector3(4.5f, 16.5f + breatheY, 4.5f),
                    uvMap: new UVMap(40, 0, 48, 0, 40, 8, 56, 8, 32, 8, 48, 8, 8, 8, 8),
                    cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir,
                    headNod * MathF.PI / 180f, 0f, 0f, new Vector3(0, 8f + breatheY, 0));

                // 2. TORSO & JACKET
                AddBoxFaces(faces, skin,
                    min: new Vector3(-4f, -4f + breatheY * 0.5f, -2f),
                    max: new Vector3(4f, 8f + breatheY, 2f),
                    uvMap: new UVMap(20, 16, 28, 16, 20, 20, 32, 20, 16, 20, 28, 20, 8, 12, 4),
                    cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir);

                AddBoxFaces(faces, skin,
                    min: new Vector3(-4.3f, -4.2f + breatheY * 0.5f, -2.3f),
                    max: new Vector3(4.3f, 8.2f + breatheY, 2.3f),
                    uvMap: new UVMap(20, 32, 28, 32, 20, 36, 32, 36, 16, 36, 28, 36, 8, 12, 4),
                    cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir);

                // 3. RIGHT ARM & SLEEVE
                float rightArmRot = (armSwing) * MathF.PI / 180.0f;
                AddBoxFaces(faces, skin,
                    min: new Vector3(-4f - armW, -4f + breatheY, -2f),
                    max: new Vector3(-4f, 8f + breatheY, 2f),
                    uvMap: new UVMap(44, 16, 44 + (int)armW, 16, 44, 20, 48 + (int)armW, 20, 40, 20, 44 + (int)armW, 20, (int)armW, 12, 4),
                    cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir,
                    rightArmRot, 0f, 0f, new Vector3(-4f - armW * 0.5f, 7f + breatheY, 0));

                AddBoxFaces(faces, skin,
                    min: new Vector3(-4.3f - armW, -4.3f + breatheY, -2.3f),
                    max: new Vector3(-3.8f, 8.3f + breatheY, 2.3f),
                    uvMap: new UVMap(44, 32, 44 + (int)armW, 32, 44, 36, 48 + (int)armW, 36, 40, 36, 44 + (int)armW, 36, (int)armW, 12, 4),
                    cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir,
                    rightArmRot, 0f, 0f, new Vector3(-4f - armW * 0.5f, 7f + breatheY, 0));

                // 4. LEFT ARM & SLEEVE
                float leftArmRot = (-armSwing) * MathF.PI / 180.0f;
                AddBoxFaces(faces, skin,
                    min: new Vector3(4f, -4f + breatheY, -2f),
                    max: new Vector3(4f + armW, 8f + breatheY, 2f),
                    uvMap: new UVMap(36, 48, 36 + (int)armW, 48, 36, 52, 40 + (int)armW, 52, 32, 52, 36 + (int)armW, 52, (int)armW, 12, 4),
                    cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir,
                    leftArmRot, 0f, 0f, new Vector3(4f + armW * 0.5f, 7f + breatheY, 0));

                AddBoxFaces(faces, skin,
                    min: new Vector3(3.8f, -4.3f + breatheY, -2.3f),
                    max: new Vector3(4.3f + armW, 8.3f + breatheY, 2.3f),
                    uvMap: new UVMap(52, 48, 52 + (int)armW, 48, 52, 52, 56 + (int)armW, 52, 48, 52, 52 + (int)armW, 52, (int)armW, 12, 4),
                    cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir,
                    leftArmRot, 0f, 0f, new Vector3(4f + armW * 0.5f, 7f + breatheY, 0));

                // 5. RIGHT LEG & PANTS
                float rightLegRot = (legSwing) * MathF.PI / 180.0f;
                AddBoxFaces(faces, skin,
                    min: new Vector3(-4f, -16f, -2f),
                    max: new Vector3(0f, -4f, 2f),
                    uvMap: new UVMap(4, 16, 8, 16, 4, 20, 12, 20, 0, 20, 8, 20, 4, 12, 4),
                    cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir,
                    rightLegRot, 0f, 0f, new Vector3(-2f, -4f, 0));

                AddBoxFaces(faces, skin,
                    min: new Vector3(-4.3f, -16.2f, -2.3f),
                    max: new Vector3(0.3f, -3.8f, 2.3f),
                    uvMap: new UVMap(4, 32, 8, 32, 4, 36, 12, 36, 0, 36, 8, 36, 4, 12, 4),
                    cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir,
                    rightLegRot, 0f, 0f, new Vector3(-2f, -4f, 0));

                // 6. LEFT LEG & PANTS
                float leftLegRot = (-legSwing) * MathF.PI / 180.0f;
                AddBoxFaces(faces, skin,
                    min: new Vector3(0f, -16f, -2f),
                    max: new Vector3(4f, -4f, 2f),
                    uvMap: new UVMap(20, 48, 24, 48, 20, 52, 28, 52, 16, 52, 24, 52, 4, 12, 4),
                    cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir,
                    leftLegRot, 0f, 0f, new Vector3(2f, -4f, 0));

                AddBoxFaces(faces, skin,
                    min: new Vector3(-0.3f, -16.2f, -2.3f),
                    max: new Vector3(4.3f, -3.8f, 2.3f),
                    uvMap: new UVMap(4, 48, 8, 48, 4, 52, 12, 52, 0, 52, 8, 52, 4, 12, 4),
                    cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir,
                    leftLegRot, 0f, 0f, new Vector3(2f, -4f, 0));

                // 7. CAPE (if present)
                if (_capeBitmap != null)
                {
                    float capeTilt = 14.0f * MathF.PI / 180.0f;
                    AddBoxFaces(faces, _capeBitmap,
                        min: new Vector3(-5f, -8f + breatheY, -3f),
                        max: new Vector3(5f, 8f + breatheY, -2.2f),
                        uvMap: new UVMap(1, 0, 11, 0, 1, 1, 12, 1, 0, 1, 11, 1, 10, 16, 1),
                        cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir,
                        capeTilt, 0f, 0f, new Vector3(0, 8f + breatheY, -2f));
                }

                // Sort all faces by CenterZ ascending (Painter's algorithm: draw furthest back faces first)
                faces.Sort((a, b) => a.CenterZ.CompareTo(b.CenterZ));

                // Render quads with SkiaSharp
                using var skinShader = SKShader.CreateBitmap(skin, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
                using var skinPaint = new SKPaint
                {
                    Shader = skinShader,
                    FilterQuality = SKFilterQuality.None, // Crisp authentic Minecraft pixels!
                    IsAntialias = false
                };

                SKShader? capeShader = null;
                SKPaint? capePaint = null;
                if (_capeBitmap != null)
                {
                    capeShader = SKShader.CreateBitmap(_capeBitmap, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
                    capePaint = new SKPaint
                    {
                        Shader = capeShader,
                        FilterQuality = SKFilterQuality.None,
                        IsAntialias = false
                    };
                }

                var posBuf = new SKPoint[6];
                var uvBuf = new SKPoint[6];
                var colBuf = new SKColor[6];

                foreach (var face in faces)
                {
                    posBuf[0] = face.P0;
                    posBuf[1] = face.P1;
                    posBuf[2] = face.P2;
                    posBuf[3] = face.P0;
                    posBuf[4] = face.P2;
                    posBuf[5] = face.P3;

                    uvBuf[0] = face.UV0;
                    uvBuf[1] = face.UV1;
                    uvBuf[2] = face.UV2;
                    uvBuf[3] = face.UV0;
                    uvBuf[4] = face.UV2;
                    uvBuf[5] = face.UV3;

                    colBuf[0] = face.Color;
                    colBuf[1] = face.Color;
                    colBuf[2] = face.Color;
                    colBuf[3] = face.Color;
                    colBuf[4] = face.Color;
                    colBuf[5] = face.Color;

                    var p = (face.Texture == _capeBitmap && capePaint != null) ? capePaint : skinPaint;
                    canvas.DrawVertices(SKVertexMode.Triangles, posBuf, uvBuf, colBuf, p);
                }

                capePaint?.Dispose();
                capeShader?.Dispose();
            }

            // Draw to Avalonia visual context
            context.DrawImage(_renderBitmap, new Rect(0, 0, Bounds.Width, Bounds.Height));
        }

        private struct UVMap
        {
            public int TopU, TopV;
            public int BottomU, BottomV;
            public int FrontU, FrontV;
            public int BackU, BackV;
            public int RightU, RightV;
            public int LeftU, LeftV;
            public int W, H, D;

            public UVMap(int topU, int topV, int botU, int botV, int frontU, int frontV, int backU, int backV, int rightU, int rightV, int leftU, int leftV, int w, int h, int d)
            {
                TopU = topU; TopV = topV;
                BottomU = botU; BottomV = botV;
                FrontU = frontU; FrontV = frontV;
                BackU = backU; BackV = backV;
                RightU = rightU; RightV = rightV;
                LeftU = leftU; LeftV = leftV;
                W = w; H = h; D = d;
            }
        }

        private static void AddBoxFaces(
            List<FaceQuad> faces,
            SKBitmap texture,
            Vector3 min, Vector3 max,
            UVMap uvMap,
            float cosYaw, float sinYaw,
            float cosPitch, float sinPitch,
            float centerX, float centerY, float scale,
            Vector3 lightDir,
            float localRotX = 0f, float localRotY = 0f, float localRotZ = 0f,
            Vector3 pivot = default)
        {
            // 8 Box Vertices
            Vector3 p000 = new(min.X, min.Y, min.Z);
            Vector3 p100 = new(max.X, min.Y, min.Z);
            Vector3 p110 = new(max.X, max.Y, min.Z);
            Vector3 p010 = new(min.X, max.Y, min.Z);
            Vector3 p001 = new(min.X, min.Y, max.Z);
            Vector3 p101 = new(max.X, min.Y, max.Z);
            Vector3 p111 = new(max.X, max.Y, max.Z);
            Vector3 p011 = new(min.X, max.Y, max.Z);

            if (localRotX != 0f || localRotY != 0f || localRotZ != 0f)
            {
                p000 = RotateAroundPivot(p000, pivot, localRotX, localRotY, localRotZ);
                p100 = RotateAroundPivot(p100, pivot, localRotX, localRotY, localRotZ);
                p110 = RotateAroundPivot(p110, pivot, localRotX, localRotY, localRotZ);
                p010 = RotateAroundPivot(p010, pivot, localRotX, localRotY, localRotZ);
                p001 = RotateAroundPivot(p001, pivot, localRotX, localRotY, localRotZ);
                p101 = RotateAroundPivot(p101, pivot, localRotX, localRotY, localRotZ);
                p111 = RotateAroundPivot(p111, pivot, localRotX, localRotY, localRotZ);
                p011 = RotateAroundPivot(p011, pivot, localRotX, localRotY, localRotZ);
            }

            // FRONT (+Z)
            AddFace(faces, texture, p011, p111, p101, p001, new Vector3(0, 0, 1),
                uvMap.FrontU, uvMap.FrontV, uvMap.W, uvMap.H,
                cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir, localRotX, localRotY, localRotZ);

            // BACK (-Z)
            AddFace(faces, texture, p110, p010, p000, p100, new Vector3(0, 0, -1),
                uvMap.BackU, uvMap.BackV, uvMap.W, uvMap.H,
                cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir, localRotX, localRotY, localRotZ);

            // TOP (+Y)
            AddFace(faces, texture, p010, p110, p111, p011, new Vector3(0, 1, 0),
                uvMap.TopU, uvMap.TopV, uvMap.W, uvMap.D,
                cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir, localRotX, localRotY, localRotZ);

            // BOTTOM (-Y)
            AddFace(faces, texture, p001, p101, p100, p000, new Vector3(0, -1, 0),
                uvMap.BottomU, uvMap.BottomV, uvMap.W, uvMap.D,
                cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir, localRotX, localRotY, localRotZ);

            // RIGHT (-X) (Viewer's left when looking straight at player)
            AddFace(faces, texture, p010, p011, p001, p000, new Vector3(-1, 0, 0),
                uvMap.RightU, uvMap.RightV, uvMap.D, uvMap.H,
                cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir, localRotX, localRotY, localRotZ);

            // LEFT (+X) (Viewer's right when looking straight at player)
            AddFace(faces, texture, p111, p110, p100, p101, new Vector3(1, 0, 0),
                uvMap.LeftU, uvMap.LeftV, uvMap.D, uvMap.H,
                cosYaw, sinYaw, cosPitch, sinPitch, centerX, centerY, scale, lightDir, localRotX, localRotY, localRotZ);
        }

        private static void AddFace(
            List<FaceQuad> faces,
            SKBitmap texture,
            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
            Vector3 normal,
            int u, int v, int w, int h,
            float cosYaw, float sinYaw,
            float cosPitch, float sinPitch,
            float centerX, float centerY, float scale,
            Vector3 lightDir,
            float localRotX, float localRotY, float localRotZ)
        {
            if (localRotX != 0f || localRotY != 0f || localRotZ != 0f)
            {
                normal = RotateVector(normal, localRotX, localRotY, localRotZ);
            }

            Vector3 rotNorm = TransformVertex(normal, cosYaw, sinYaw, cosPitch, sinPitch);
            // Backface culling: if normal points away from viewer, cull face!
            if (rotNorm.Z <= 0.0f) return;

            // Skip fully transparent quads (e.g. empty outer sleeves/jackets)
            if (IsFaceFullyTransparent(texture, u, v, w, h)) return;

            Vector3 tv0 = TransformVertex(v0, cosYaw, sinYaw, cosPitch, sinPitch);
            Vector3 tv1 = TransformVertex(v1, cosYaw, sinYaw, cosPitch, sinPitch);
            Vector3 tv2 = TransformVertex(v2, cosYaw, sinYaw, cosPitch, sinPitch);
            Vector3 tv3 = TransformVertex(v3, cosYaw, sinYaw, cosPitch, sinPitch);

            const float camDist = 120.0f;
            float s0 = scale * (camDist / (camDist - tv0.Z));
            float s1 = scale * (camDist / (camDist - tv1.Z));
            float s2 = scale * (camDist / (camDist - tv2.Z));
            float s3 = scale * (camDist / (camDist - tv3.Z));

            SKPoint p0 = new(centerX + tv0.X * s0, centerY - tv0.Y * s0);
            SKPoint p1 = new(centerX + tv1.X * s1, centerY - tv1.Y * s1);
            SKPoint p2 = new(centerX + tv2.X * s2, centerY - tv2.Y * s2);
            SKPoint p3 = new(centerX + tv3.X * s3, centerY - tv3.Y * s3);

            // Directional lighting with soft ambient base
            float dot = Math.Max(0f, rotNorm.X * lightDir.X + rotNorm.Y * lightDir.Y + rotNorm.Z * lightDir.Z);
            byte lit = (byte)(255 * (0.65f + 0.35f * dot));
            SKColor litColor = new(lit, lit, lit, 255);

            float centerZ = (tv0.Z + tv1.Z + tv2.Z + tv3.Z) * 0.25f;

            faces.Add(new FaceQuad
            {
                P0 = p0, P1 = p1, P2 = p2, P3 = p3,
                UV0 = new SKPoint(u, v),
                UV1 = new SKPoint(u + w, v),
                UV2 = new SKPoint(u + w, v + h),
                UV3 = new SKPoint(u, v + h),
                Color = litColor,
                CenterZ = centerZ,
                Texture = texture
            });
        }

        private static bool IsFaceFullyTransparent(SKBitmap texture, int u, int v, int w, int h)
        {
            int startX = Math.Max(0, u);
            int startY = Math.Max(0, v);
            int endX = Math.Min(texture.Width, u + w);
            int endY = Math.Min(texture.Height, v + h);

            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    if (texture.GetPixel(x, y).Alpha > 0)
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Automatically detects whether a Minecraft skin is Classic (Steve, 4px arms) or Slim (Alex, 3px arms).
        /// In standard 64x64 skins, the 4th column of pixels in Alex arms (X: 54..55 for right arm, X: 46..47 for left arm)
        /// is completely transparent (alpha = 0).
        /// </summary>
        public static string DetectSkinModel(SKBitmap bmp)
        {
            if (bmp == null || bmp.Width < 64 || bmp.Height < 64) return "classic";

            int totalCheckPixels = 0;
            int transparentPixels = 0;

            // Right arm 4th column (X: 54..55, Y: 20..31)
            for (int y = 20; y < 32; y++)
            {
                for (int x = 54; x < 56; x++)
                {
                    totalCheckPixels++;
                    if (bmp.GetPixel(x, y).Alpha == 0)
                        transparentPixels++;
                }
            }

            // Left arm 4th column (X: 46..47, Y: 52..63)
            for (int y = 52; y < 64; y++)
            {
                for (int x = 46; x < 48; x++)
                {
                    totalCheckPixels++;
                    if (bmp.GetPixel(x, y).Alpha == 0)
                        transparentPixels++;
                }
            }

            return (transparentPixels >= totalCheckPixels - 2) ? "slim" : "classic";
        }

        private static Vector3 TransformVertex(Vector3 v, float cosYaw, float sinYaw, float cosPitch, float sinPitch)
        {
            // Pitch around X
            float y1 = v.Y * cosPitch - v.Z * sinPitch;
            float z1 = v.Y * sinPitch + v.Z * cosPitch;
            float x1 = v.X;

            // Yaw around Y
            float x2 = x1 * cosYaw + z1 * sinYaw;
            float z2 = -x1 * sinYaw + z1 * cosYaw;
            float y2 = y1;

            return new Vector3(x2, y2, z2);
        }

        private static Vector3 RotateAroundPivot(Vector3 v, Vector3 pivot, float rotX, float rotY, float rotZ)
        {
            var p = v - pivot;
            p = RotateVector(p, rotX, rotY, rotZ);
            return p + pivot;
        }

        private static Vector3 RotateVector(Vector3 v, float rx, float ry, float rz)
        {
            if (rx != 0f)
            {
                float cx = MathF.Cos(rx), sx = MathF.Sin(rx);
                v = new Vector3(v.X, v.Y * cx - v.Z * sx, v.Y * sx + v.Z * cx);
            }
            if (ry != 0f)
            {
                float cy = MathF.Cos(ry), sy = MathF.Sin(ry);
                v = new Vector3(v.X * cy + v.Z * sy, v.Y, -v.X * sy + v.Z * cy);
            }
            if (rz != 0f)
            {
                float cz = MathF.Cos(rz), sz = MathF.Sin(rz);
                v = new Vector3(v.X * cz - v.Y * sz, v.X * sz + v.Y * cz, v.Z);
            }
            return v;
        }

        #endregion

        #region Skin Processing (Legacy Expansion & Default Steve)

        private static SKBitmap ExpandLegacy32Skin(SKBitmap legacy)
        {
            var expanded = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var canvas = new SKCanvas(expanded);
            canvas.Clear(SKColors.Transparent);

            // Copy top 32 rows
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.None };
            canvas.DrawBitmap(legacy, new SKRect(0, 0, 64, 32), new SKRect(0, 0, 64, 32), paint);

            // Copy Right Leg (0..16, 16..32) to Left Leg (16..32, 48..64)
            CopySubRect(legacy, 0, 16, 16, 16, expanded, 16, 48);

            // Copy Right Arm (40..56, 16..32) to Left Arm (32..48, 48..64)
            CopySubRect(legacy, 40, 16, 16, 16, expanded, 32, 48);

            legacy.Dispose();
            return expanded;
        }

        private static void CopySubRect(SKBitmap src, int sx, int sy, int w, int h, SKBitmap dst, int dx, int dy)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    dst.SetPixel(dx + x, dy + y, src.GetPixel(sx + x, sy + y));
                }
            }
        }

        public static SKBitmap GenerateDefaultSteveSkin()
        {
            var bmp = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(SKColors.Transparent);

            using var paint = new SKPaint { Style = SKPaintStyle.Fill };

            // Skin peach tone
            paint.Color = new SKColor(219, 177, 148);
            canvas.DrawRect(0, 0, 64, 64, paint);

            // Steve Cyan Shirt
            paint.Color = new SKColor(0, 166, 166);
            canvas.DrawRect(16, 16, 24, 16, paint); // Torso
            canvas.DrawRect(40, 16, 16, 6, paint);   // Right shoulder
            canvas.DrawRect(32, 48, 16, 6, paint);   // Left shoulder

            // Steve Blue Jeans
            paint.Color = new SKColor(55, 51, 123);
            canvas.DrawRect(0, 16, 16, 12, paint);   // Right leg
            canvas.DrawRect(16, 48, 16, 12, paint);  // Left leg

            // Steve Hair & Beard (Dark Brown)
            paint.Color = new SKColor(73, 51, 28);
            canvas.DrawRect(8, 0, 16, 8, paint);     // Head top
            canvas.DrawRect(8, 8, 8, 3, paint);      // Forehead
            canvas.DrawRect(0, 8, 8, 8, paint);      // Right hair
            canvas.DrawRect(16, 8, 8, 8, paint);     // Left hair
            canvas.DrawRect(24, 8, 8, 8, paint);     // Back hair
            canvas.DrawRect(10, 13, 4, 1, paint);    // Mouth/beard

            // Steve Blue Eyes
            paint.Color = new SKColor(43, 66, 154);
            bmp.SetPixel(9, 11, paint.Color);
            bmp.SetPixel(14, 11, paint.Color);

            // Steve White Eye Whites
            paint.Color = SKColors.White;
            bmp.SetPixel(8, 11, paint.Color);
            bmp.SetPixel(15, 11, paint.Color);

            // Shoes (Dark Gray)
            paint.Color = new SKColor(70, 70, 70);
            canvas.DrawRect(0, 28, 16, 4, paint);
            canvas.DrawRect(16, 60, 16, 4, paint);

            return bmp;
        }

        public void Invalidate3D()
        {
            InvalidateVisual();
        }

        #endregion
    }
}
