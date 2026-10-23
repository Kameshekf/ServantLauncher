using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AvaloniaBmp = Avalonia.Media.Imaging.Bitmap;

namespace ServantLauncher.ViewModels
{
    public partial class ImageCropViewModel : ObservableObject
    {
        private readonly string _targetSavePath;

        [ObservableProperty]
        private string _sourceFilePath = string.Empty;

        [ObservableProperty]
        private AvaloniaBmp? _sourceBitmap;

        [ObservableProperty]
        private AvaloniaBmp? _previewBitmap;

        [ObservableProperty]
        private double _imageNaturalWidth;

        [ObservableProperty]
        private double _imageNaturalHeight;

        [ObservableProperty]
        private double _zoom = 1.0;

        [ObservableProperty]
        private double _offsetX = 0;

        [ObservableProperty]
        private double _offsetY = 0;

        [ObservableProperty]
        private double _cropBoxSize = 220;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public event Action<string>? CropCompleted;
        public event Action? CancelRequested;

        public ImageCropViewModel(string sourceFilePath, string targetSavePath)
        {
            _sourceFilePath = sourceFilePath;
            _targetSavePath = targetSavePath;
            LoadSourceImage();
        }

        private void LoadSourceImage()
        {
            if (!File.Exists(SourceFilePath)) return;

            try
            {
                byte[] bytes = File.ReadAllBytes(SourceFilePath);
                using (var ms = new MemoryStream(bytes))
                {
                    SourceBitmap = new AvaloniaBmp(ms);
                }
                ImageNaturalWidth = SourceBitmap.PixelSize.Width;
                ImageNaturalHeight = SourceBitmap.PixelSize.Height;
                
                double minDimension = Math.Min(ImageNaturalWidth, ImageNaturalHeight);
                if (minDimension > 0)
                {
                    Zoom = CropBoxSize / minDimension;
                }
                else
                {
                    Zoom = 1.0;
                }
                OffsetX = (CropBoxSize - ImageNaturalWidth * Zoom) / 2.0;
                OffsetY = (CropBoxSize - ImageNaturalHeight * Zoom) / 2.0;

                UpdatePreview();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка: {ex.Message}";
            }
        }

        partial void OnZoomChanged(double value)
        {
            ClampOffsets();
            UpdatePreview();
        }

        partial void OnOffsetXChanged(double value)
        {
            UpdatePreview();
        }

        partial void OnOffsetYChanged(double value)
        {
            UpdatePreview();
        }

        public void PanBy(double deltaX, double deltaY)
        {
            OffsetX += deltaX;
            OffsetY += deltaY;
            ClampOffsets();
        }

        private void ClampOffsets()
        {
            double displayedW = ImageNaturalWidth * Zoom;
            double displayedH = ImageNaturalHeight * Zoom;

            if (displayedW >= CropBoxSize)
            {
                OffsetX = Math.Min(0, Math.Max(CropBoxSize - displayedW, OffsetX));
            }
            else
            {
                OffsetX = (CropBoxSize - displayedW) / 2.0;
            }

            if (displayedH >= CropBoxSize)
            {
                OffsetY = Math.Min(0, Math.Max(CropBoxSize - displayedH, OffsetY));
            }
            else
            {
                OffsetY = (CropBoxSize - displayedH) / 2.0;
            }
        }

        public void UpdatePreview()
        {
            if (!OperatingSystem.IsWindows() || !File.Exists(SourceFilePath)) return;

            try
            {
                byte[] bytes = File.ReadAllBytes(SourceFilePath);
                using var inMs = new MemoryStream(bytes);
                using var srcBmp = new System.Drawing.Bitmap(inMs);
                
                double scale = Zoom;
                if (scale <= 0.001) scale = 0.001;

                double srcX = -OffsetX / scale;
                double srcY = -OffsetY / scale;
                double srcW = CropBoxSize / scale;
                double srcH = CropBoxSize / scale;

                int ix = Math.Max(0, (int)Math.Round(srcX));
                int iy = Math.Max(0, (int)Math.Round(srcY));
                int iw = Math.Min(srcBmp.Width - ix, Math.Max(1, (int)Math.Round(srcW)));
                int ih = Math.Min(srcBmp.Height - iy, Math.Max(1, (int)Math.Round(srcH)));

                int outSize = 128;
                using var previewBmp = new System.Drawing.Bitmap(outSize, outSize, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(previewBmp);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(System.Drawing.Color.Transparent);

                g.DrawImage(srcBmp, new Rectangle(0, 0, outSize, outSize), new Rectangle(ix, iy, iw, ih), GraphicsUnit.Pixel);

                using var ms = new MemoryStream();
                previewBmp.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                PreviewBitmap = new AvaloniaBmp(ms);
            }
            catch { }
        }

        [RelayCommand]
        public async Task SaveCropAsync()
        {
            if (!File.Exists(SourceFilePath)) return;

            StatusMessage = "Сохранение иконки...";
            try
            {
                await Task.Run(() =>
                {
                    byte[] bytes = File.ReadAllBytes(SourceFilePath);
                    using var inMs = new MemoryStream(bytes);
                    using var srcBmp = new System.Drawing.Bitmap(inMs);
                    double scale = Zoom;
                    if (scale <= 0.001) scale = 0.001;

                    double srcX = -OffsetX / scale;
                    double srcY = -OffsetY / scale;
                    double srcW = CropBoxSize / scale;
                    double srcH = CropBoxSize / scale;

                    int ix = Math.Max(0, (int)Math.Round(srcX));
                    int iy = Math.Max(0, (int)Math.Round(srcY));
                    int iw = Math.Min(srcBmp.Width - ix, Math.Max(1, (int)Math.Round(srcW)));
                    int ih = Math.Min(srcBmp.Height - iy, Math.Max(1, (int)Math.Round(srcH)));

                    int outSize = 512;
                    using var destBmp = new System.Drawing.Bitmap(outSize, outSize, PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(destBmp);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(System.Drawing.Color.Transparent);

                    g.DrawImage(srcBmp, new Rectangle(0, 0, outSize, outSize), new Rectangle(ix, iy, iw, ih), GraphicsUnit.Pixel);

                    var dir = Path.GetDirectoryName(_targetSavePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    destBmp.Save(_targetSavePath, ImageFormat.Png);
                });

                CropCompleted?.Invoke(_targetSavePath);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка при сохранении: {ex.Message}";
            }
        }

        [RelayCommand]
        public void Reset()
        {
            LoadSourceImage();
        }

        [RelayCommand]
        public void Cancel()
        {
            CancelRequested?.Invoke();
        }
    }
}
