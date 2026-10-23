using System;
using System.Net;
using System.Net.Http;

namespace ServantLauncher.Services
{
    public static class HttpHelper
    {
        public static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                UseProxy = false,
                AutomaticDecompression = DecompressionMethods.All
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(3)
            };

            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "servant_launcher/1.2.0");
            client.DefaultRequestHeaders.Add("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
            return client;
        }
    }
}
