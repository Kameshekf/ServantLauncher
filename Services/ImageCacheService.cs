using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace ServantLauncher.Services
{
    public interface IImageCacheService
    {
        Task<Bitmap?> LoadImageAsync(string? url);
    }

    public class ImageCacheService : IImageCacheService
    {
        private static readonly ConcurrentDictionary<string, Bitmap> _memoryCache = new();
        private readonly HttpClient _httpClient;
        private readonly string _cacheDir;

        public ImageCacheService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? HttpHelper.CreateHttpClient();

            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".servantlauncher", "cache", "images");
            Directory.CreateDirectory(appData);
            _cacheDir = appData;
        }

        public async Task<Bitmap?> LoadImageAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            if (_memoryCache.TryGetValue(url, out var cachedBitmap))
            {
                return cachedBitmap;
            }

            try
            {
                // Check local file cache
                var hash = GetStringHash(url);
                var filePath = Path.Combine(_cacheDir, hash + ".img");

                byte[] bytes;
                if (File.Exists(filePath))
                {
                    bytes = await File.ReadAllBytesAsync(filePath);
                }
                else
                {
                    bytes = await _httpClient.GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(filePath, bytes);
                }

                using var ms = new MemoryStream(bytes);
                var bitmap = new Bitmap(ms);
                _memoryCache[url] = bitmap;
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load image from {url}: {ex.Message}");
                return null;
            }
        }

        private static string GetStringHash(string text)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(hashBytes)[..16];
        }
    }
}
