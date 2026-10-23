using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ServantLauncher.Services
{
    public class JavaInstallation
    {
        public string Path { get; set; } = string.Empty;
        public int MajorVersion { get; set; }
        public string VersionString { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public bool Is64Bit { get; set; } = true;

        public string DisplayName => $"Java {MajorVersion} ({Vendor}) - {Path}";
        public override string ToString() => DisplayName;
    }

    public interface IJavaService
    {
        List<JavaInstallation> FindInstalledJavas();
        string GetRecommendedJavaPath(string minecraftVersion);
        int GetRequiredJavaVersion(string minecraftVersion);
        bool ValidateJavaPath(string path, out int majorVersion);
        Task<string> EnsureJavaInstalledAsync(string minecraftVersion, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default);
        Task<string> DownloadAndInstallJavaAsync(int majorVersion, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default);
    }

    public class JavaService : IJavaService
    {
        private List<JavaInstallation>? _cachedInstallations;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        public List<JavaInstallation> FindInstalledJavas()
        {
            if (_cachedInstallations != null) return _cachedInstallations;

            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<JavaInstallation>();

            void TryAdd(string javaExe)
            {
                if (!File.Exists(javaExe) || !found.Add(javaExe)) return;

                if (ValidateJavaPath(javaExe, out int major))
                {
                    result.Add(new JavaInstallation
                    {
                        Path = javaExe,
                        MajorVersion = major,
                        VersionString = $"{major}",
                        Vendor = javaExe.Contains("Adoptium", StringComparison.OrdinalIgnoreCase) || javaExe.Contains("temurin", StringComparison.OrdinalIgnoreCase) ? "Eclipse Temurin" :
                                 javaExe.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ? "Microsoft OpenJDK" :
                                 javaExe.Contains("BellSoft", StringComparison.OrdinalIgnoreCase) ? "BellSoft Liberica" :
                                 javaExe.Contains("Zulu", StringComparison.OrdinalIgnoreCase) ? "Azul Zulu" :
                                 javaExe.Contains("ServantLauncher", StringComparison.OrdinalIgnoreCase) ? "Servant OpenJDK" :
                                 javaExe.Contains("Oracle", StringComparison.OrdinalIgnoreCase) ? "Oracle" : "System JRE",
                        Is64Bit = true
                    });
                }
            }

            // 0. Check Servant Launcher Runtimes directory
            var launcherRuntimes = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ServantLauncher", "runtimes"),
                Path.Combine(AppContext.BaseDirectory, "runtimes")
            };

            foreach (var rDir in launcherRuntimes)
            {
                if (Directory.Exists(rDir))
                {
                    try
                    {
                        foreach (var sub in Directory.GetDirectories(rDir))
                        {
                            TryAdd(Path.Combine(sub, "bin", "javaw.exe"));
                            TryAdd(Path.Combine(sub, "bin", "java.exe"));
                        }
                    }
                    catch { }
                }
            }

            // 1. Check JAVA_HOME
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                TryAdd(Path.Combine(javaHome, "bin", "javaw.exe"));
                TryAdd(Path.Combine(javaHome, "bin", "java.exe"));
            }

            // 2. Check PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    TryAdd(Path.Combine(dir, "javaw.exe"));
                    TryAdd(Path.Combine(dir, "java.exe"));
                }
            }

            // 3. Scan Common Program Files Directories
            var searchRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
            };

            var vendors = new[]
            {
                "Java", "Eclipse Adoptium", "Eclipse Foundation", "Microsoft", "BellSoft", "Zulu", "Semeru", "Amazon Corretto"
            };

            foreach (var root in searchRoots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                foreach (var vendor in vendors)
                {
                    var vendorDir = Path.Combine(root, vendor);
                    if (Directory.Exists(vendorDir))
                    {
                        try
                        {
                            foreach (var jdkDir in Directory.GetDirectories(vendorDir))
                            {
                                TryAdd(Path.Combine(jdkDir, "bin", "javaw.exe"));
                                TryAdd(Path.Combine(jdkDir, "bin", "java.exe"));
                            }
                        }
                        catch { }
                    }
                }
            }

            // 4. Scan Mojang official launcher runtimes
            var mojangRuntimes = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "runtime"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.4297127D64C5C_8wekyb3d8bbwe", "LocalCache", "Local", "runtime")
            };

            foreach (var r in mojangRuntimes)
            {
                if (Directory.Exists(r))
                {
                    try
                    {
                        foreach (var sub in Directory.GetDirectories(r, "*", SearchOption.AllDirectories))
                        {
                            TryAdd(Path.Combine(sub, "bin", "javaw.exe"));
                            TryAdd(Path.Combine(sub, "bin", "java.exe"));
                            TryAdd(Path.Combine(sub, "javaw.exe"));
                        }
                    }
                    catch { }
                }
            }

            // If none found, fallback to system 'javaw.exe'
            if (result.Count == 0)
            {
                result.Add(new JavaInstallation
                {
                    Path = "javaw",
                    MajorVersion = 21,
                    VersionString = "21 (System default)",
                    Vendor = "Default"
                });
            }

            _cachedInstallations = result.OrderByDescending(j => j.MajorVersion).ToList();
            return _cachedInstallations;
        }

        public string GetRecommendedJavaPath(string minecraftVersion)
        {
            var javas = FindInstalledJavas();
            int requiredMajor = GetRequiredJavaVersion(minecraftVersion);

            var match = javas.FirstOrDefault(j => j.MajorVersion == requiredMajor && j.Path != "javaw" && File.Exists(j.Path))
                     ?? (requiredMajor < 25 ? javas.FirstOrDefault(j => j.MajorVersion >= requiredMajor && j.MajorVersion < 25 && j.Path != "javaw" && File.Exists(j.Path)) : null)
                     ?? javas.FirstOrDefault(j => j.Path != "javaw" && File.Exists(j.Path))
                     ?? javas.FirstOrDefault();

            return match?.Path ?? "javaw";
        }

        public bool ValidateJavaPath(string path, out int majorVersion)
        {
            majorVersion = 0;
            try
            {
                if (!File.Exists(path) && !string.Equals(path, "javaw", StringComparison.OrdinalIgnoreCase) && !string.Equals(path, "java", StringComparison.OrdinalIgnoreCase))
                    return false;

                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "-version",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;

                var output = proc.StandardError.ReadToEnd();
                proc.WaitForExit(3000);

                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("version", StringComparison.OrdinalIgnoreCase))
                    {
                        var firstQuote = line.IndexOf('"');
                        var secondQuote = line.IndexOf('"', firstQuote + 1);
                        if (firstQuote != -1 && secondQuote != -1)
                        {
                            var ver = line.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                            if (ver.StartsWith("1."))
                            {
                                var sub = ver.Substring(2, 1);
                                if (int.TryParse(sub, out int v8)) { majorVersion = v8; return true; }
                            }
                            else
                            {
                                var dot = ver.IndexOf('.');
                                var majorStr = dot > 0 ? ver.Substring(0, dot) : ver;
                                if (int.TryParse(majorStr, out int vModern)) { majorVersion = vModern; return true; }
                            }
                        }
                    }
                }
                majorVersion = 21;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public int GetRequiredJavaVersion(string minecraftVersion)
        {
            if (string.IsNullOrWhiteSpace(minecraftVersion)) return 21;

            if (minecraftVersion.StartsWith("26") || minecraftVersion.StartsWith("25"))
            {
                return 25;
            }
            if (minecraftVersion.StartsWith("1.21") || minecraftVersion.StartsWith("1.20.5") || minecraftVersion.StartsWith("1.20.6"))
            {
                return 21;
            }
            if (minecraftVersion.StartsWith("1.18") || minecraftVersion.StartsWith("1.19") || minecraftVersion.StartsWith("1.20"))
            {
                return 17;
            }
            if (minecraftVersion.StartsWith("1.17"))
            {
                return 17;
            }
            return 8;
        }

        public async Task<string> EnsureJavaInstalledAsync(string minecraftVersion, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default)
        {
            int requiredMajor = GetRequiredJavaVersion(minecraftVersion);

            // 1. Check if an installed runtime meets the requirement
            var installed = FindInstalledJavas();
            var validMatch = installed.FirstOrDefault(j => j.MajorVersion == requiredMajor && j.Path != "javaw" && File.Exists(j.Path))
                          ?? (requiredMajor < 25 ? installed.FirstOrDefault(j => j.MajorVersion >= requiredMajor && j.MajorVersion < 25 && j.Path != "javaw" && File.Exists(j.Path)) : null);

            if (validMatch != null && ValidateJavaPath(validMatch.Path, out int verifiedVer) && verifiedVer >= requiredMajor)
            {
                return validMatch.Path;
            }

            // 2. Check local ServantLauncher runtimes directory
            var localRuntimesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ServantLauncher", "runtimes", $"java-{requiredMajor}");
            var localJavaExe = Path.Combine(localRuntimesDir, "bin", "javaw.exe");
            if (File.Exists(localJavaExe) && ValidateJavaPath(localJavaExe, out int localVer) && localVer >= requiredMajor)
            {
                return localJavaExe;
            }

            // 3. Auto-download required Java runtime
            progress?.Report((5, $"Подготовка среды Java {requiredMajor} (OpenJDK)..."));
            return await DownloadAndInstallJavaAsync(requiredMajor, progress, ct);
        }

        public async Task<string> DownloadAndInstallJavaAsync(int majorVersion, IProgress<(double percent, string status)>? progress = null, CancellationToken ct = default)
        {
            var runtimesBaseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ServantLauncher", "runtimes");
            var targetDir = Path.Combine(runtimesBaseDir, $"java-{majorVersion}");
            var targetJavaExe = Path.Combine(targetDir, "bin", "javaw.exe");

            if (File.Exists(targetJavaExe) && ValidateJavaPath(targetJavaExe, out int existingVer) && existingVer >= majorVersion)
            {
                return targetJavaExe;
            }

            Directory.CreateDirectory(runtimesBaseDir);
            if (Directory.Exists(targetDir))
            {
                try { Directory.Delete(targetDir, true); } catch { }
            }
            Directory.CreateDirectory(targetDir);

            var urls = GetDownloadUrls(majorVersion);
            var tempZip = Path.Combine(runtimesBaseDir, $"java_{majorVersion}_download.zip");

            bool downloaded = false;
            foreach (var url in urls)
            {
                try
                {
                    progress?.Report((10, $"Подключение к серверу загрузки Java {majorVersion}..."));
                    using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!response.IsSuccessStatusCode) continue;

                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                    await using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        totalRead += bytesRead;
                        if (totalBytes > 0)
                        {
                            double pct = 10.0 + ((double)totalRead / totalBytes * 75.0);
                            double mbRead = totalRead / (1024.0 * 1024.0);
                            double mbTotal = totalBytes / (1024.0 * 1024.0);
                            progress?.Report((pct, $"Загрузка Java {majorVersion} OpenJDK: {mbRead:F1} / {mbTotal:F1} MB"));
                        }
                        else
                        {
                            double mbRead = totalRead / (1024.0 * 1024.0);
                            progress?.Report((50, $"Загрузка Java {majorVersion} OpenJDK: {mbRead:F1} MB"));
                        }
                    }

                    downloaded = true;
                    break;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    Debug.WriteLine($"Failed downloading Java {majorVersion} from {url}: {ex.Message}");
                }
            }

            if (!downloaded || !File.Exists(tempZip))
            {
                throw new InvalidOperationException($"Не удалось загрузить среду выполнения Java {majorVersion}. Проверьте подключение к интернету.");
            }

            progress?.Report((88, $"Распаковка среды выполнения Java {majorVersion}..."));

            var extractTemp = Path.Combine(runtimesBaseDir, $"java_{majorVersion}_extract");
            if (Directory.Exists(extractTemp))
            {
                try { Directory.Delete(extractTemp, true); } catch { }
            }

            try
            {
                ZipFile.ExtractToDirectory(tempZip, extractTemp);
            }
            finally
            {
                try { File.Delete(tempZip); } catch { }
            }

            var foundExe = Directory.GetFiles(extractTemp, "javaw.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (foundExe == null)
            {
                throw new InvalidOperationException($"В загруженном архиве Java {majorVersion} не найден javaw.exe.");
            }

            var binDir = Path.GetDirectoryName(foundExe)!;
            var jdkRootDir = Directory.GetParent(binDir)!.FullName;

            CopyDirectory(jdkRootDir, targetDir);
            try { Directory.Delete(extractTemp, true); } catch { }

            _cachedInstallations = null; // Invalidate cache
            progress?.Report((100, $"Java {majorVersion} успешно установлена!"));
            return targetJavaExe;
        }

        private static List<string> GetDownloadUrls(int majorVersion)
        {
            return majorVersion switch
            {
                25 => new List<string>
                {
                    "https://api.adoptium.net/v3/binary/latest/25/ea/windows/x64/jdk/hotspot/normal/eclipse",
                    "https://download.java.net/java/early_access/jdk25/24/GPL/openjdk-25-ea+24_windows-x64_bin.zip",
                    "https://cdn.azul.com/zulu/bin/zulu25.0.65-ea-jdk25.0.0-ea.24-win_x64.zip"
                },
                21 => new List<string>
                {
                    "https://api.adoptium.net/v3/binary/latest/21/ga/windows/x64/jre/hotspot/normal/eclipse",
                    "https://aka.ms/download-jdk/microsoft-jdk-21.0.6-windows-x64.zip",
                    "https://api.adoptium.net/v3/binary/latest/21/ga/windows/x64/jdk/hotspot/normal/eclipse"
                },
                17 => new List<string>
                {
                    "https://api.adoptium.net/v3/binary/latest/17/ga/windows/x64/jre/hotspot/normal/eclipse",
                    "https://aka.ms/download-jdk/microsoft-jdk-17.0.14-windows-x64.zip"
                },
                8 => new List<string>
                {
                    "https://api.adoptium.net/v3/binary/latest/8/ga/windows/x64/jre/hotspot/normal/eclipse",
                    "https://api.adoptium.net/v3/binary/latest/8/ga/windows/x64/jdk/hotspot/normal/eclipse"
                },
                _ => new List<string>
                {
                    $"https://api.adoptium.net/v3/binary/latest/{majorVersion}/ga/windows/x64/jre/hotspot/normal/eclipse"
                }
            };
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var targetFilePath = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, targetFilePath, true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var targetSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, targetSubDir);
            }
        }
    }
}
