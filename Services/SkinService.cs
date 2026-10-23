using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using ServantLauncher.Models;

namespace ServantLauncher.Services
{
    public class CapeInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = "Без плаща";
        public string? Url { get; set; }
        public bool IsActive { get; set; }

        public override string ToString() => Name;
    }

    public class SkinItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        private string _name = "Скин";
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _filePath = string.Empty;
        public string FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        private string _model = "classic"; // "classic" or "slim"
        public string Model
        {
            get => _model;
            set => SetProperty(ref _model, value);
        }

        public string? CapeUrl { get; set; }
        
        private Bitmap? _previewBitmap;
        public Bitmap? PreviewBitmap
        {
            get => _previewBitmap;
            set => SetProperty(ref _previewBitmap, value);
        }

        public bool HasPreviewBitmap => _previewBitmap != null;

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        private bool _isDragging;
        public bool IsDragging
        {
            get => _isDragging;
            set => SetProperty(ref _isDragging, value);
        }
    }

    public interface ISkinService
    {
        List<SkinItem> GetLibrarySkins();
        Task<SkinItem> AddSkinToLibraryAsync(string pngFilePath, string name, string model = "classic", string? capeUrl = null);
        Task<bool> UpdateSkinInLibraryAsync(SkinItem item, string newName, string newModel, string? newPngFilePath = null);
        Task<bool> UploadSkinToMojangAsync(AccountModel account, string skinFilePath, string model = "classic", CancellationToken ct = default);
        Task<(string? SkinUrl, string Model, string? CapeUrl, List<CapeInfo> Capes)> FetchMojangProfileSkinAsync(AccountModel account, CancellationToken ct = default);
        Task<bool> EquipMojangCapeAsync(AccountModel account, string? capeId, CancellationToken ct = default);
        Task<string?> FetchCurrentSkinUrlAsync(string uuid, CancellationToken ct = default);
        Task<Bitmap?> RenderSkinBodyAsync(string? skinFilePath, string model = "classic", string? username = null, CancellationToken ct = default);
        Task<SkinItem> SaveSkinBitmapAsync(SkiaSharp.SKBitmap bitmap, string? existingFilePath, string skinName, string model);
        void DeleteSkinFromLibrary(SkinItem item);
    }

    public class SkinService : ISkinService
    {
        private readonly string _skinsDir;
        private readonly HttpClient _httpClient;
        private readonly IImageCacheService _imageCacheService;

        public SkinService(HttpClient? httpClient = null, IImageCacheService? imageCacheService = null)
        {
            _httpClient = httpClient ?? HttpHelper.CreateHttpClient();
            _imageCacheService = imageCacheService ?? new ImageCacheService(_httpClient);

            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".servantlauncher");
            _skinsDir = Path.Combine(appData, "skins");
            Directory.CreateDirectory(_skinsDir);
        }

        public static Bitmap? ExtractHeadBitmap(string skinPath, int targetSize = 64)
        {
            if (!File.Exists(skinPath)) return null;
            try
            {
                using var srcImg = new System.Drawing.Bitmap(skinPath);
                using var headBmp = new System.Drawing.Bitmap(8, 8, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = System.Drawing.Graphics.FromImage(headBmp))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    // Base head: x=8, y=8, w=8, h=8
                    g.DrawImage(srcImg, new System.Drawing.Rectangle(0, 0, 8, 8), new System.Drawing.Rectangle(8, 8, 8, 8), System.Drawing.GraphicsUnit.Pixel);
                    // Hat overlay: x=40, y=8, w=8, h=8
                    g.DrawImage(srcImg, new System.Drawing.Rectangle(0, 0, 8, 8), new System.Drawing.Rectangle(40, 8, 8, 8), System.Drawing.GraphicsUnit.Pixel);
                }

                using var scaledBmp = new System.Drawing.Bitmap(targetSize, targetSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = System.Drawing.Graphics.FromImage(scaledBmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(headBmp, new System.Drawing.Rectangle(0, 0, targetSize, targetSize));
                }

                using var ms = new MemoryStream();
                scaledBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                return new Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }

        public List<SkinItem> GetLibrarySkins()
        {
            var result = new List<SkinItem>();
            if (!Directory.Exists(_skinsDir)) return result;

            foreach (var file in Directory.GetFiles(_skinsDir, "*.png"))
            {
                var rawName = Path.GetFileNameWithoutExtension(file);
                var cleanName = rawName;
                var model = file.Contains("_slim") ? "slim" : "classic";

                var lastUnderscore = rawName.LastIndexOf('_');
                if (lastUnderscore > 0)
                {
                    var beforeLast = rawName.Substring(0, lastUnderscore);
                    var secondLast = beforeLast.LastIndexOf('_');
                    if (secondLast > 0 && (beforeLast.EndsWith("_classic") || beforeLast.EndsWith("_slim")))
                    {
                        cleanName = beforeLast.Substring(0, secondLast);
                    }
                    else if (rawName.EndsWith("_classic") || rawName.EndsWith("_slim"))
                    {
                        cleanName = beforeLast;
                    }
                }

                var item = new SkinItem
                {
                    FilePath = file,
                    Name = cleanName,
                    Model = model
                };

                _ = Task.Run(async () =>
                {
                    var bmp = await RenderSkinBodyAsync(file, model, cleanName);
                    if (bmp != null)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => item.PreviewBitmap = bmp);
                    }
                });

                result.Add(item);
            }

            return result;
        }

        public async Task<SkinItem> AddSkinToLibraryAsync(string pngFilePath, string name, string model = "classic", string? capeUrl = null)
        {
            if (!File.Exists(pngFilePath))
                throw new FileNotFoundException("Файл скина не найден.");

            var cleanName = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(pngFilePath) : name.Trim();
            var fileName = $"{cleanName}_{(model == "slim" ? "slim" : "classic")}_{Guid.NewGuid():N}.png";
            var destPath = Path.Combine(_skinsDir, fileName);

            File.Copy(pngFilePath, destPath, overwrite: true);

            var bmp = await RenderSkinBodyAsync(destPath, model, cleanName);

            return new SkinItem
            {
                FilePath = destPath,
                Name = cleanName,
                Model = model,
                CapeUrl = capeUrl,
                PreviewBitmap = bmp
            };
        }

        public async Task<bool> UpdateSkinInLibraryAsync(SkinItem item, string newName, string newModel, string? newPngFilePath = null)
        {
            try
            {
                var cleanName = string.IsNullOrWhiteSpace(newName) ? "Скин" : newName.Trim();
                var targetModel = newModel == "slim" ? "slim" : "classic";
                var oldPath = item.FilePath;

                string newPath;
                if (!string.IsNullOrEmpty(newPngFilePath) && File.Exists(newPngFilePath))
                {
                    var fileName = $"{cleanName}_{targetModel}_{Guid.NewGuid():N}.png";
                    newPath = Path.Combine(_skinsDir, fileName);
                    File.Copy(newPngFilePath, newPath, overwrite: true);

                    if (!string.IsNullOrEmpty(oldPath) && File.Exists(oldPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(oldPath); } catch { }
                    }
                }
                else if (File.Exists(oldPath))
                {
                    var fileName = $"{cleanName}_{targetModel}_{Guid.NewGuid():N}.png";
                    newPath = Path.Combine(_skinsDir, fileName);
                    if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(oldPath, newPath, overwrite: true);
                    }
                }
                else
                {
                    return false;
                }

                item.Name = cleanName;
                item.Model = targetModel;
                item.FilePath = newPath;

                var bmp = await RenderSkinBodyAsync(newPath, targetModel, cleanName);
                if (bmp != null)
                {
                    item.PreviewBitmap = bmp;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SkinService] UpdateSkinInLibraryAsync error: {ex.Message}");
                return false;
            }
        }

        public async Task<SkinItem> SaveSkinBitmapAsync(SKBitmap bitmap, string? existingFilePath, string skinName, string model)
        {
            var cleanName = string.IsNullOrWhiteSpace(skinName) ? "Скин" : skinName.Trim();
            string targetPath;
            if (!string.IsNullOrEmpty(existingFilePath) && File.Exists(existingFilePath))
            {
                targetPath = existingFilePath;
            }
            else
            {
                var fileName = $"{cleanName}_{(model == "slim" ? "slim" : "classic")}_{Guid.NewGuid():N}.png";
                targetPath = Path.Combine(_skinsDir, fileName);
            }

            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var fs = File.Open(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                data.SaveTo(fs);
            }

            var bustBmp = CompositeSkinBust(targetPath, model);
            return new SkinItem
            {
                FilePath = targetPath,
                Name = cleanName,
                Model = model,
                PreviewBitmap = bustBmp
            };
        }

        public async Task<bool> UploadSkinToMojangAsync(AccountModel account, string skinFilePath, string model = "classic", CancellationToken ct = default)
        {
            if (account.Type != AccountType.Microsoft || string.IsNullOrEmpty(account.AccessToken))
                throw new InvalidOperationException("Загрузка скина доступна только для лицензионных аккаунтов Microsoft.");

            if (!File.Exists(skinFilePath))
                throw new FileNotFoundException("Файл скина не найден.");

            var url = "https://api.minecraftservices.com/minecraft/profile/skins";
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(model.ToLowerInvariant()), "variant");

            var fileBytes = await File.ReadAllBytesAsync(skinFilePath, ct);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(fileContent, "file", Path.GetFileName(skinFilePath));

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = form
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);

            var resp = await _httpClient.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }

        public async Task<bool> EquipMojangCapeAsync(AccountModel account, string? capeId, CancellationToken ct = default)
        {
            if (account.Type != AccountType.Microsoft || string.IsNullOrEmpty(account.AccessToken))
                return false;

            try
            {
                if (string.IsNullOrEmpty(capeId))
                {
                    using var req = new HttpRequestMessage(HttpMethod.Delete, "https://api.minecraftservices.com/minecraft/profile/capes/active");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
                    var resp = await _httpClient.SendAsync(req, ct);
                    return resp.IsSuccessStatusCode;
                }
                else
                {
                    using var req = new HttpRequestMessage(HttpMethod.Put, "https://api.minecraftservices.com/minecraft/profile/capes/active")
                    {
                        Content = new StringContent($"{{\"capeId\":\"{capeId}\"}}", Encoding.UTF8, "application/json")
                    };
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
                    var resp = await _httpClient.SendAsync(req, ct);
                    return resp.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error equipping cape: {ex.Message}");
                return false;
            }
        }

        public async Task<(string? SkinUrl, string Model, string? CapeUrl, List<CapeInfo> Capes)> FetchMojangProfileSkinAsync(AccountModel account, CancellationToken ct = default)
        {
            var capesList = new List<CapeInfo>
            {
                new CapeInfo { Id = string.Empty, Name = "Без плаща", Url = null, IsActive = false }
            };

            if (account.Type == AccountType.Microsoft && !string.IsNullOrEmpty(account.AccessToken))
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);

                    var resp = await _httpClient.SendAsync(req, ct);
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync(ct);
                        var node = JsonNode.Parse(json);
                        var skinsArray = node?["skins"]?.AsArray();
                        var activeSkin = skinsArray?.FirstOrDefault(s => s?["state"]?.GetValue<string>() == "ACTIVE") ?? skinsArray?.FirstOrDefault();

                        var skinUrl = activeSkin?["url"]?.GetValue<string>();
                        var variant = activeSkin?["variant"]?.GetValue<string>()?.ToLowerInvariant() ?? "classic";

                        var capesArray = node?["capes"]?.AsArray();
                        string? activeCapeUrl = null;

                        if (capesArray != null)
                        {
                            foreach (var c in capesArray)
                            {
                                var id = c?["id"]?.GetValue<string>() ?? string.Empty;
                                var url = c?["url"]?.GetValue<string>();
                                var state = c?["state"]?.GetValue<string>() ?? "INACTIVE";
                                var alias = c?["alias"]?.GetValue<string>() ?? ("Плащ " + (id.Length > 6 ? id[..6] : id));
                                bool isActive = state.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase);

                                if (isActive)
                                {
                                    activeCapeUrl = url;
                                }

                                capesList.Add(new CapeInfo
                                {
                                    Id = id,
                                    Name = alias,
                                    Url = url,
                                    IsActive = isActive
                                });
                            }
                        }

                        if (string.IsNullOrEmpty(activeCapeUrl))
                        {
                            capesList[0].IsActive = true;
                        }

                        return (skinUrl, variant, activeCapeUrl, capesList);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error fetching Mojang profile skin: {ex.Message}");
                }
            }

            // Fallback to session server or username
            capesList[0].IsActive = true;
            if (!string.IsNullOrEmpty(account.Uuid))
            {
                var skinUrl = await FetchCurrentSkinUrlAsync(account.Uuid, ct);
                if (!string.IsNullOrEmpty(skinUrl))
                {
                    return (skinUrl, "classic", null, capesList);
                }
            }

            return ($"https://render.crafty.gg/2d/frontfull/{account.Username}", "classic", null, capesList);
        }

        public async Task<string?> FetchCurrentSkinUrlAsync(string uuid, CancellationToken ct = default)
        {
            try
            {
                var cleanUuid = uuid.Replace("-", "");
                var url = $"https://sessionserver.mojang.com/session/minecraft/profile/{cleanUuid}";
                var resp = await _httpClient.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) return null;

                var json = await resp.Content.ReadAsStringAsync(ct);
                var node = JsonNode.Parse(json);
                var prop = node?["properties"]?.AsArray()?.FirstOrDefault(p => p?["name"]?.GetValue<string>() == "textures");
                var base64 = prop?["value"]?.GetValue<string>();
                if (string.IsNullOrEmpty(base64)) return null;

                var decodedJson = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                var texNode = JsonNode.Parse(decodedJson);
                return texNode?["textures"]?["SKIN"]?["url"]?.GetValue<string>();
            }
            catch
            {
                return null;
            }
        }

        public async Task<Bitmap?> RenderSkinBodyAsync(string? skinFilePath, string model = "classic", string? username = null, CancellationToken ct = default)
        {
            // 1. If username or clean skin name is available, fetch from official Servant Skin API
            var lookupName = !string.IsNullOrWhiteSpace(username)
                ? username.Trim()
                : (!string.IsNullOrEmpty(skinFilePath) ? Path.GetFileNameWithoutExtension(skinFilePath).Split('_')[0] : null);

            if (!string.IsNullOrWhiteSpace(lookupName) && !lookupName.Contains(" ") && lookupName.Length <= 32 && !lookupName.Contains("."))
            {
                try
                {
                    var servantBodyUrl = $"https://servant-ru.online/api/profile/skin/body/{Uri.EscapeDataString(lookupName)}?size=380";
                    
                    // Try memory/disk cache first
                    var cachedBmp = await _imageCacheService.LoadImageAsync(servantBodyUrl);
                    if (cachedBmp != null) return cachedBmp;

                }
                catch { }
            }

            // 2. Render composite bust for local/saved skin files
            if (!string.IsNullOrEmpty(skinFilePath) && File.Exists(skinFilePath))
            {

                try
                {
                    return CompositeSkinBust(skinFilePath, model);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Skin composition error: {ex.Message}");
                    return new Bitmap(skinFilePath);
                }
            }

            return null;
        }

        public static string DetectSkinModel(string skinPath)
        {
            if (string.IsNullOrEmpty(skinPath) || !File.Exists(skinPath)) return "classic";
            try
            {
                using var bmp = new System.Drawing.Bitmap(skinPath);
                return DetectSkinModel(bmp);
            }
            catch
            {
                return "classic";
            }
        }

        public static string DetectSkinModel(System.Drawing.Bitmap bmp)
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
                    if (bmp.GetPixel(x, y).A == 0)
                        transparentPixels++;
                }
            }

            // Left arm 4th column (X: 46..47, Y: 52..63)
            for (int y = 52; y < 64; y++)
            {
                for (int x = 46; x < 48; x++)
                {
                    totalCheckPixels++;
                    if (bmp.GetPixel(x, y).A == 0)
                        transparentPixels++;
                }
            }

            return (transparentPixels >= totalCheckPixels - 2) ? "slim" : "classic";
        }

        public static Bitmap CompositeSkinBust(string skinPath, string model)
        {
            using var srcGdi = new System.Drawing.Bitmap(skinPath);
            int srcW = srcGdi.Width;
            int srcH = srcGdi.Height;

            const int scale = 10;
            const int targetW = 16 * scale;
            const int targetH = 18 * scale;

            using var destGdi = new System.Drawing.Bitmap(targetW, targetH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(destGdi))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                g.Clear(System.Drawing.Color.Transparent);

                bool isSlim = string.Equals(model, "slim", StringComparison.OrdinalIgnoreCase);

                void DrawPart(int srcX, int srcY, int w, int h, int dstX, int dstY)
                {
                    if (srcX + w > srcW || srcY + h > srcH) return;
                    var srcRect = new System.Drawing.Rectangle(srcX, srcY, w, h);
                    var dstRect = new System.Drawing.Rectangle(dstX * scale, dstY * scale, w * scale, h * scale);
                    g.DrawImage(srcGdi, dstRect, srcRect, System.Drawing.GraphicsUnit.Pixel);
                }

                // 1. Base Head: (8, 8, 8, 8) -> dst: (4, 0)
                DrawPart(8, 8, 8, 8, 4, 0);

                // 2. Torso (top 10 pixels): (20, 20, 8, 10) -> dst: (4, 8)
                DrawPart(20, 20, 8, 10, 4, 8);

                // 3. Right & Left Arms (top 10 pixels)
                if (isSlim)
                {
                    DrawPart(44, 20, 3, 10, 1, 8);
                    if (srcH >= 64) DrawPart(36, 52, 3, 10, 12, 8);
                    else DrawPart(44, 20, 3, 10, 12, 8);
                }
                else
                {
                    DrawPart(44, 20, 4, 10, 0, 8);
                    if (srcH >= 64) DrawPart(36, 52, 4, 10, 12, 8);
                    else DrawPart(44, 20, 4, 10, 12, 8);
                }

                // 4. Overlays (3D layers):
                // Hat: (40, 8, 8, 8) -> dst: (4, 0)
                DrawPart(40, 8, 8, 8, 4, 0);

                // Jacket: (20, 36, 8, 10) -> dst: (4, 8)
                if (srcH >= 64) DrawPart(20, 36, 8, 10, 4, 8);

                // Sleeves
                if (srcH >= 64)
                {
                    if (isSlim)
                    {
                        DrawPart(44, 36, 3, 10, 1, 8);
                        DrawPart(52, 52, 3, 10, 12, 8);
                    }
                    else
                    {
                        DrawPart(44, 36, 4, 10, 0, 8);
                        DrawPart(52, 52, 4, 10, 12, 8);
                    }
                }
            }

            using var ms = new MemoryStream();
            destGdi.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            return new Bitmap(ms);
        }

        public void DeleteSkinFromLibrary(SkinItem item)
        {
            try
            {
                if (File.Exists(item.FilePath)) File.Delete(item.FilePath);
            }
            catch { }
        }
    }
}