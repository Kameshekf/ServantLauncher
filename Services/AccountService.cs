using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ServantLauncher.Models;

namespace ServantLauncher.Services
{
    public class DeviceCodeResponse
    {
        public string DeviceCode { get; set; } = string.Empty;
        public string UserCode { get; set; } = string.Empty;
        public string VerificationUri { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public int Interval { get; set; } = 5;
    }

    public interface IAccountService
    {
        List<AccountModel> Accounts { get; }
        AccountModel? ActiveAccount { get; }
        event Action? AccountsChanged;

        void LoadAccounts();
        void SaveAccounts();
        void SetActiveAccount(AccountModel account);
        void RemoveAccount(AccountModel account);
        AccountModel CreateOfflineAccount(string username);

        Task<DeviceCodeResponse> StartMicrosoftAuthAsync();
        Task<AccountModel> CompleteMicrosoftAuthAsync(DeviceCodeResponse deviceCode, IProgress<string>? progress = null, CancellationToken ct = default);
    }

    public class AccountService : IAccountService
    {
        private const string ClientId = "00000000402b5328"; // Standard public Minecraft Xbox Live client ID
        private const string Scope = "service::user.auth.xboxlive.com::MBI_SSL";
        private readonly HttpClient _httpClient;
        private readonly IImageCacheService _imageCacheService;
        private readonly string _storagePath;

        public List<AccountModel> Accounts { get; private set; } = new();
        public AccountModel? ActiveAccount { get; private set; }
        public event Action? AccountsChanged;

        public AccountService(HttpClient? httpClient = null, IImageCacheService? imageCacheService = null)
        {
            _httpClient = httpClient ?? HttpHelper.CreateHttpClient();
            _imageCacheService = imageCacheService ?? new ImageCacheService(_httpClient);

            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".servantlauncher");
            Directory.CreateDirectory(appData);
            _storagePath = Path.Combine(appData, "accounts.dat");

            LoadAccounts();
        }

        public void LoadAccounts()
        {
            try
            {
                if (File.Exists(_storagePath))
                {
                    var fileBytes = File.ReadAllBytes(_storagePath);
                    string json;
                    if (OperatingSystem.IsWindows())
                    {
                        var decryptedBytes = ProtectedData.Unprotect(fileBytes, null, DataProtectionScope.CurrentUser);
                        json = Encoding.UTF8.GetString(decryptedBytes);
                    }
                    else
                    {
                        json = Encoding.UTF8.GetString(fileBytes);
                    }

                    var list = JsonSerializer.Deserialize<List<AccountModel>>(json);
                    if (list != null)
                    {
                        Accounts = list;
                        ActiveAccount = Accounts.FirstOrDefault();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not load encrypted accounts: {ex.Message}");
            }

            if (Accounts.Count == 0)
            {
                // Default starter offline account
                var defaultAcc = CreateOfflineAccount("Player");
                ActiveAccount = defaultAcc;
            }

            foreach (var acc in Accounts)
            {
                LoadAvatarForAccount(acc);
            }
        }

        private void LoadAvatarForAccount(AccountModel acc)
        {
            if (string.IsNullOrEmpty(acc.Username)) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    // 1. Check local skins directory
                    var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".servantlauncher");
                    var skinsDir = Path.Combine(appData, "skins");
                    if (Directory.Exists(skinsDir))
                    {
                        var localSkin = Directory.GetFiles(skinsDir, $"{acc.Username}*.png").FirstOrDefault();
                        if (localSkin != null)
                        {
                            var localHead = SkinService.ExtractHeadBitmap(localSkin, 64);
                            if (localHead != null)
                            {
                                Avalonia.Threading.Dispatcher.UIThread.Post(() => acc.HeadBitmap = localHead);
                                return;
                            }
                        }
                    }

                    // 2. Try mc-heads.net
                    var bmp = await _imageCacheService.LoadImageAsync(acc.AvatarUrl);
                    if (bmp != null)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => acc.HeadBitmap = bmp);
                        return;
                    }

                    // 3. Fallback to minotar.net
                    var fallbackUrl = $"https://minotar.net/avatar/{Uri.EscapeDataString(acc.Username)}/64";
                    var fallbackBmp = await _imageCacheService.LoadImageAsync(fallbackUrl);
                    if (fallbackBmp != null)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => acc.HeadBitmap = fallbackBmp);
                    }
                }
                catch { }
            });
        }

        public void SaveAccounts()
        {
            try
            {
                var json = JsonSerializer.Serialize(Accounts);
                var rawBytes = Encoding.UTF8.GetBytes(json);
                if (OperatingSystem.IsWindows())
                {
                    var encryptedBytes = ProtectedData.Protect(rawBytes, null, DataProtectionScope.CurrentUser);
                    File.WriteAllBytes(_storagePath, encryptedBytes);
                }
                else
                {
                    File.WriteAllBytes(_storagePath, rawBytes);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not save encrypted accounts: {ex.Message}");
            }

            AccountsChanged?.Invoke();
        }

        public void SetActiveAccount(AccountModel account)
        {
            if (Accounts.Contains(account))
            {
                ActiveAccount = account;
                SaveAccounts();
            }
        }

        public void RemoveAccount(AccountModel account)
        {
            Accounts.Remove(account);
            if (ActiveAccount == account)
            {
                ActiveAccount = Accounts.FirstOrDefault();
            }
            SaveAccounts();
        }

        public AccountModel CreateOfflineAccount(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                username = "Player";

            username = username.Trim();

            // Find existing
            var existing = Accounts.FirstOrDefault(a => a.Type == AccountType.Offline && string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                ActiveAccount = existing;
                SaveAccounts();
                return existing;
            }

            // Generate deterministic offline UUID from player name
            var uuid = GenerateOfflinePlayerUuid(username);

            var acc = new AccountModel
            {
                Username = username,
                Uuid = uuid,
                Type = AccountType.Offline
            };

            Accounts.Add(acc);
            ActiveAccount = acc;
            LoadAvatarForAccount(acc);
            SaveAccounts();
            return acc;
        }

        private static string GenerateOfflinePlayerUuid(string username)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
            // Set version to 3 (name-based) and variant to IETF
            hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

            var sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2"));
                if (i == 3 || i == 5 || i == 7 || i == 9)
                    sb.Append('-');
            }
            return sb.ToString();
        }

        public async Task<DeviceCodeResponse> StartMicrosoftAuthAsync()
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", ClientId },
                { "scope", Scope },
                { "response_type", "device_code" }
            });

            var resp = await _httpClient.PostAsync("https://login.live.com/oauth20_connect.srf", content);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);

            return new DeviceCodeResponse
            {
                DeviceCode = node?["device_code"]?.GetValue<string>() ?? "",
                UserCode = node?["user_code"]?.GetValue<string>() ?? "",
                VerificationUri = node?["verification_uri"]?.GetValue<string>() ?? "https://microsoft.com/link",
                ExpiresIn = node?["expires_in"]?.GetValue<int>() ?? 900,
                Interval = node?["interval"]?.GetValue<int>() ?? 5
            };
        }

        public async Task<AccountModel> CompleteMicrosoftAuthAsync(DeviceCodeResponse deviceCode, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            var logPath = Path.Combine(Path.GetDirectoryName(_storagePath)!, "launcher.log");
            void LogDebug(string msg)
            {
                try
                {
                    var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [Auth] {msg}{Environment.NewLine}";
                    File.AppendAllText(logPath, line);
                }
                catch { }
            }

            LogDebug("Начало завершения Microsoft OAuth авторизации...");
            progress?.Report("Ожидание подтверждения в браузере...");

            string? msAccessToken = null;
            string? msRefreshToken = null;

            int delay = Math.Max(3, deviceCode.Interval);

            // Step 1: Poll Microsoft OAuth
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(delay * 1000, ct);

                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "client_id", ClientId },
                    { "device_code", deviceCode.DeviceCode },
                    { "grant_type", "urn:ietf:params:oauth:grant-type:device_code" }
                });

                var resp = await _httpClient.PostAsync("https://login.live.com/oauth20_token.srf", form, ct);
                var json = await resp.Content.ReadAsStringAsync(ct);
                var node = JsonNode.Parse(json);

                if (resp.IsSuccessStatusCode && node != null)
                {
                    msAccessToken = node["access_token"]?.GetValue<string>();
                    msRefreshToken = node["refresh_token"]?.GetValue<string>();
                    LogDebug("Успешно получен токен Microsoft Live.");
                    break;
                }

                var error = node?["error"]?.GetValue<string>();
                if (error == "authorization_pending")
                {
                    progress?.Report("Ожидание ввода кода на сайте Microsoft...");
                    continue;
                }
                if (error == "slow_down")
                {
                    delay += 5;
                    continue;
                }

                LogDebug($"Ошибка опроса токена Microsoft: {error} (JSON: {json})");
                throw new InvalidOperationException($"Ошибка Microsoft OAuth: {error}");
            }

            if (string.IsNullOrEmpty(msAccessToken))
                throw new InvalidOperationException("Не удалось получить токен доступа Microsoft.");

            // Step 2: Xbox Live Authentication
            progress?.Report("Авторизация в Xbox Live...");
            LogDebug("Запрос аутентификации в Xbox Live (user.auth.xboxlive.com)...");

            string xblJson = string.Empty;
            HttpResponseMessage? xblResp = null;

            var rpsTicketsToTry = new List<string>
            {
                msAccessToken.StartsWith("d=", StringComparison.OrdinalIgnoreCase) ? msAccessToken : $"d={msAccessToken}",
                msAccessToken
            };

            foreach (var ticket in rpsTicketsToTry)
            {
                var xblReqObj = new
                {
                    Properties = new
                    {
                        AuthMethod = "RPS",
                        SiteName = "user.auth.xboxlive.com",
                        RpsTicket = ticket
                    },
                    RelyingParty = "http://auth.xboxlive.com",
                    TokenType = "JWT"
                };

                using var xblReqMsg = new HttpRequestMessage(HttpMethod.Post, "https://user.auth.xboxlive.com/user/authenticate")
                {
                    Content = new StringContent(JsonSerializer.Serialize(xblReqObj), Encoding.UTF8, "application/json")
                };
                xblReqMsg.Headers.Accept.Clear();
                xblReqMsg.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                xblReqMsg.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");

                xblResp = await _httpClient.SendAsync(xblReqMsg, ct);
                xblJson = await xblResp.Content.ReadAsStringAsync(ct);

                if (xblResp.IsSuccessStatusCode)
                {
                    LogDebug("Успешная аутентификация в Xbox Live!");
                    break;
                }
                else
                {
                    LogDebug($"Попытка аутентификации Xbox Live вернула: HTTP {xblResp.StatusCode}, {xblJson}");
                }
            }

            if (xblResp == null || !xblResp.IsSuccessStatusCode)
            {
                LogDebug($"Итоговая ошибка Xbox Live: HTTP {xblResp?.StatusCode}, {xblJson}");
                throw new InvalidOperationException($"Ошибка Xbox Live: {xblResp?.StatusCode}. Проверьте соединение или наличие профиля Xbox.");
            }

            var xblNode = JsonNode.Parse(xblJson);
            var xblToken = xblNode?["Token"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Некорректный ответ Xbox Live (отсутствует Token).");
            var uhs = xblNode?["DisplayClaims"]?["xui"]?[0]?["uhs"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Не удалось получить UserHash (uhs) от Xbox.");

            // Step 3: XSTS Token
            progress?.Report("Получение XSTS токена безопасности...");
            LogDebug("Запрос XSTS токена для rp://api.minecraftservices.com/...");
            var xstsReqObj = new
            {
                Properties = new
                {
                    SandboxId = "RETAIL",
                    UserTokens = new[] { xblToken }
                },
                RelyingParty = "rp://api.minecraftservices.com/",
                TokenType = "JWT"
            };

            using var xstsReqMsg = new HttpRequestMessage(HttpMethod.Post, "https://xsts.auth.xboxlive.com/xsts/authorize")
            {
                Content = new StringContent(JsonSerializer.Serialize(xstsReqObj), Encoding.UTF8, "application/json")
            };
            xstsReqMsg.Headers.Accept.Clear();
            xstsReqMsg.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            xstsReqMsg.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");

            var xstsResp = await _httpClient.SendAsync(xstsReqMsg, ct);
            var xstsJson = await xstsResp.Content.ReadAsStringAsync(ct);
            if (!xstsResp.IsSuccessStatusCode)
            {
                LogDebug($"Ошибка XSTS: HTTP {xstsResp.StatusCode}, Body: {xstsJson}");
                var xstsErrorNode = JsonNode.Parse(xstsJson);
                var xerr = xstsErrorNode?["XErr"]?.GetValue<long>() ?? 0;
                if (xerr == 2148916233)
                    throw new InvalidOperationException("У вашего аккаунта Microsoft нет профиля Xbox. Зайдите на xbox.com и создайте тег игрока.");
                if (xerr == 2148916238)
                    throw new InvalidOperationException("Детский аккаунт: требуется подтверждение родителей через Microsoft Family.");
                if (xerr == 2148916235)
                    throw new InvalidOperationException("Xbox Live недоступен в регионе вашей учетной записи.");

                throw new InvalidOperationException($"Ошибка безопасности XSTS (Код: {xerr}): {xstsErrorNode?["Message"]?.GetValue<string>() ?? "Неизвестная ошибка"}");
            }

            var xstsNode = JsonNode.Parse(xstsJson);
            var xstsToken = xstsNode?["Token"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Некорректный XSTS токен.");

            // Step 4: Login with Xbox in Minecraft Services
            progress?.Report("Вход в сервисы Minecraft...");
            LogDebug("Запрос токена Minecraft Services (login_with_xbox)...");
            var mcReq = new
            {
                identityToken = $"XBL3.0 x={uhs};{xstsToken}"
            };

            var mcResp = await _httpClient.PostAsJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", mcReq, ct);
            var mcJson = await mcResp.Content.ReadAsStringAsync(ct);
            if (!mcResp.IsSuccessStatusCode)
            {
                LogDebug($"Ошибка входа в Minecraft Services: HTTP {mcResp.StatusCode}, Body: {mcJson}");
                throw new InvalidOperationException($"Ошибка входа в сервисы Minecraft: {mcResp.StatusCode}");
            }

            var mcNode = JsonNode.Parse(mcJson);
            var mcAccessToken = mcNode?["access_token"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Не получен access_token сервисов Minecraft.");

            // Step 5: Fetch Minecraft Profile
            progress?.Report("Получение профиля игрока...");
            LogDebug("Запрос профиля игрока Minecraft (/minecraft/profile)...");
            var reqMsg = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
            reqMsg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcAccessToken);

            var profileResp = await _httpClient.SendAsync(reqMsg, ct);
            var profileJson = await profileResp.Content.ReadAsStringAsync(ct);
            if (!profileResp.IsSuccessStatusCode)
            {
                LogDebug($"Ошибка профиля Minecraft: HTTP {profileResp.StatusCode}, Body: {profileJson}");
                if (profileResp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    throw new InvalidOperationException("На данном Microsoft аккаунте нет купленной лицензии Minecraft Java Edition или еще не создан никнейм на minecraft.net.");
                }
                throw new InvalidOperationException($"Ошибка получения профиля игрока: {profileResp.StatusCode}");
            }

            var profileNode = JsonNode.Parse(profileJson);
            var username = profileNode?["name"]?.GetValue<string>() ?? "Player";
            var rawUuid = profileNode?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
            var formattedUuid = rawUuid.Length == 32
                ? $"{rawUuid.Substring(0, 8)}-{rawUuid.Substring(8, 4)}-{rawUuid.Substring(12, 4)}-{rawUuid.Substring(16, 4)}-{rawUuid.Substring(20)}"
                : rawUuid;

            LogDebug($"Успешный вход игрока {username} (UUID: {formattedUuid})");

            var existing = Accounts.FirstOrDefault(a => a.Type == AccountType.Microsoft && a.Uuid == formattedUuid);
            if (existing != null)
            {
                existing.Username = username;
                existing.AccessToken = mcAccessToken;
                existing.RefreshToken = msRefreshToken ?? "";
                existing.ExpiresAt = DateTime.UtcNow.AddHours(24);
                ActiveAccount = existing;
                LoadAvatarForAccount(existing);
                SaveAccounts();
                return existing;
            }

            var account = new AccountModel
            {
                Username = username,
                Uuid = formattedUuid,
                Type = AccountType.Microsoft,
                AccessToken = mcAccessToken,
                RefreshToken = msRefreshToken ?? "",
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };

            Accounts.Add(account);
            ActiveAccount = account;
            LoadAvatarForAccount(account);
            SaveAccounts();
            return account;
        }
    }
}
