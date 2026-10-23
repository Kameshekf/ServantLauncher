using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Threading;
using ServantLauncher.Models;

namespace ServantLauncher.Services
{
    public interface IProfileManager
    {
        List<GameProfile> Profiles { get; }
        GameProfile? ActiveProfile { get; }
        LauncherConfig Config { get; }
        string BaseLauncherDir { get; }
        string InstancesDir { get; }
        string CommonGameDir { get; }
        event Action? ProfilesChanged;
        event Action? ActiveProfileChanged;

        void LoadProfiles();
        void SaveProfiles();
        void SetActiveProfile(GameProfile profile);
        void AddProfile(GameProfile profile);
        void UpdateProfile(GameProfile profile);
        void DeleteProfile(GameProfile profile);
        GameProfile DuplicateProfile(GameProfile profile);
        GameProfile DuplicateProfileWithOptions(GameProfile profile, string newName, bool copyMods, bool copyConfigs, bool copySaves, bool copyShaderPacks, bool copyOptions);
        string GetProfileGameDir(GameProfile profile);
        string GetProfileModsDir(GameProfile profile);
        bool IsProfileInstalled(GameProfile? profile);
        void SaveConfig();
        void EnsureServerProfile();
    }

    public class ProfileManager : IProfileManager
    {
        private readonly string _profilesFile;
        private readonly string _configFile;

        public List<GameProfile> Profiles { get; private set; } = new();
        public GameProfile? ActiveProfile { get; private set; }
        public LauncherConfig Config { get; private set; } = new();

        public string BaseLauncherDir { get; }
        public string InstancesDir { get; }
        public string CommonGameDir { get; }

        public event Action? ProfilesChanged;
        public event Action? ActiveProfileChanged;

        public ProfileManager()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            BaseLauncherDir = Path.Combine(appData, ".servantlauncher");
            InstancesDir = Path.Combine(BaseLauncherDir, "instances");
            CommonGameDir = Path.Combine(BaseLauncherDir, "common");

            Directory.CreateDirectory(BaseLauncherDir);
            Directory.CreateDirectory(InstancesDir);
            Directory.CreateDirectory(CommonGameDir);

            _profilesFile = Path.Combine(BaseLauncherDir, "profiles.json");
            _configFile = Path.Combine(BaseLauncherDir, "config.json");

            LoadConfig();
            LoadProfiles();
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public bool IsProfileInstalled(GameProfile? profile)
        {
            if (profile == null) return false;
            var commonDir = CommonGameDir;
            var versionsDir = Path.Combine(commonDir, "versions");
            var clientJar = Path.Combine(versionsDir, profile.GameVersion, $"{profile.GameVersion}.jar");
            var clientJson = Path.Combine(versionsDir, profile.GameVersion, $"{profile.GameVersion}.json");

            if (!File.Exists(clientJar) || !File.Exists(clientJson))
                return false;

            if (profile.LoaderType == LoaderType.Fabric)
            {
                var loaderVer = string.IsNullOrEmpty(profile.LoaderVersion) ? "0.16.10" : profile.LoaderVersion;
                var fabricId = $"fabric-loader-{loaderVer}-{profile.GameVersion}";
                var fabricJson = Path.Combine(versionsDir, fabricId, $"{fabricId}.json");
                if (!File.Exists(fabricJson))
                    return false;
            }

            return true;
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configFile))
                {
                    var json = File.ReadAllText(_configFile);
                    var conf = JsonSerializer.Deserialize<LauncherConfig>(json, JsonOptions);
                    if (conf != null) Config = conf;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
            }
        }

        public void SaveConfig()
        {
            try
            {
                var json = JsonSerializer.Serialize(Config, JsonOptions);
                File.WriteAllText(_configFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving config: {ex.Message}");
            }
        }

        public void LoadProfiles()
        {
            try
            {
                if (File.Exists(_profilesFile))
                {
                    var json = File.ReadAllText(_profilesFile);
                    var list = JsonSerializer.Deserialize<List<GameProfile>>(json, JsonOptions);
                    if (list != null && list.Count > 0)
                    {
                        Profiles = list;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading profiles: {ex.Message}");
            }

            if (Profiles.Count == 0)
            {
                // Create default Servant Fabric and Vanilla profiles
                var servantFabric = new GameProfile
                {
                    Name = "Servant Server (1.21.11 Fabric)",
                    GameVersion = "1.21.11",
                    LoaderType = LoaderType.Fabric,
                    LoaderVersion = "0.19.5",
                    AllocatedRamMb = 4096,
                    IsServerProfile = true,
                    ServerPackVersion = "1.0.0"
                };

                var servantVanilla = new GameProfile
                {
                    Name = "Vanilla 1.21.11",
                    GameVersion = "1.21.11",
                    LoaderType = LoaderType.Vanilla,
                    AllocatedRamMb = 4096
                };

                Profiles.Add(servantFabric);
                Profiles.Add(servantVanilla);
                SaveProfiles();
            }

            EnsureServerProfile();

            if (!string.IsNullOrEmpty(Config.ActiveProfileId))
            {
                ActiveProfile = Profiles.FirstOrDefault(p => p.Id == Config.ActiveProfileId) ?? Profiles.First();
            }
            else
            {
                ActiveProfile = Profiles.First();
            }

            ProfilesChanged?.Invoke();
        }

        public void EnsureServerProfile()
        {
            var serverProfile = Profiles.FirstOrDefault(p => p.IsServerProfile);
            if (serverProfile == null)
            {
                // If a profile named Servant Server exists, promote it to server profile
                serverProfile = Profiles.FirstOrDefault(p => p.Name.StartsWith("Servant Server", StringComparison.OrdinalIgnoreCase));
                if (serverProfile != null)
                {
                    serverProfile.IsServerProfile = true;
                    serverProfile.Name = "Servant Server (1.21.11 Fabric)";
                    serverProfile.GameVersion = "1.21.11";
                    serverProfile.LoaderType = LoaderType.Fabric;
                    if (string.IsNullOrEmpty(serverProfile.LoaderVersion) || serverProfile.LoaderVersion == "0.16.10") serverProfile.LoaderVersion = "0.19.5";
                    if (string.IsNullOrEmpty(serverProfile.ServerPackVersion))
                    {
                        serverProfile.ServerPackVersion = "1.0.0";
                    }
                }
                else
                {
                    serverProfile = new GameProfile
                    {
                        Name = "Servant Server (1.21.11 Fabric)",
                        GameVersion = "1.21.11",
                        LoaderType = LoaderType.Fabric,
                        LoaderVersion = "0.19.5",
                        AllocatedRamMb = 4096,
                        IsServerProfile = true,
                        ServerPackVersion = "1.0.0"
                    };
                    Profiles.Insert(0, serverProfile);
                }
                SaveProfiles();
            }
            else
            {
                // Ensure name and version are 1.21.11 Fabric
                if (serverProfile.GameVersion == "1.21.1" || serverProfile.Name.Contains("1.21.1 Fabric") || serverProfile.LoaderVersion == "0.16.10")
                {
                    serverProfile.Name = "Servant Server (1.21.11 Fabric)";
                    serverProfile.GameVersion = "1.21.11";
                    serverProfile.LoaderType = LoaderType.Fabric;
                    serverProfile.LoaderVersion = "0.19.5";
                    SaveProfiles();
                }
            }
        }

        public void SaveProfiles()
        {
            try
            {
                var json = JsonSerializer.Serialize(Profiles, JsonOptions);
                File.WriteAllText(_profilesFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving profiles: {ex.Message}");
            }

            RaiseProfilesChanged();
        }

        private void RaiseActiveProfileChanged()
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                ActiveProfileChanged?.Invoke();
            }
            else
            {
                Dispatcher.UIThread.Post(() => ActiveProfileChanged?.Invoke());
            }
        }

        private void RaiseProfilesChanged()
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                ProfilesChanged?.Invoke();
            }
            else
            {
                Dispatcher.UIThread.Post(() => ProfilesChanged?.Invoke());
            }
        }

        public void SetActiveProfile(GameProfile profile)
        {
            if (Profiles.Contains(profile))
            {
                ActiveProfile = profile;
                Config.ActiveProfileId = profile.Id;
                SaveConfig();
                RaiseActiveProfileChanged();
            }
        }

        public void AddProfile(GameProfile profile)
        {
            Profiles.Add(profile);
            SaveProfiles();
            SetActiveProfile(profile);
        }

        public void UpdateProfile(GameProfile profile)
        {
            var idx = Profiles.FindIndex(p => p.Id == profile.Id);
            if (idx >= 0)
            {
                Profiles[idx] = profile;
                SaveProfiles();
                if (ActiveProfile?.Id == profile.Id)
                {
                    ActiveProfile = profile;
                    RaiseActiveProfileChanged();
                }
            }
        }

        public void DeleteProfile(GameProfile profile)
        {
            if (profile.IsServerProfile)
            {
                // Official server profile cannot be deleted
                return;
            }

            Profiles.Remove(profile);
            if (ActiveProfile?.Id == profile.Id)
            {
                ActiveProfile = Profiles.FirstOrDefault();
                Config.ActiveProfileId = ActiveProfile?.Id;
                SaveConfig();
                RaiseActiveProfileChanged();
            }
            SaveProfiles();

            // Clean instance folder if desired
            try
            {
                var dir = GetProfileGameDir(profile);
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch { }
        }

        public GameProfile DuplicateProfile(GameProfile original)
        {
            return DuplicateProfileWithOptions(original, $"{original.Name} (Копия)", true, true, false, true, true);
        }

        public GameProfile DuplicateProfileWithOptions(GameProfile original, string newName, bool copyMods, bool copyConfigs, bool copySaves, bool copyShaderPacks, bool copyOptions)
        {
            var clone = new GameProfile
            {
                Name = string.IsNullOrWhiteSpace(newName) ? $"{original.Name} (Копия)" : newName.Trim(),
                GameVersion = original.GameVersion,
                IsSnapshot = original.IsSnapshot,
                LoaderType = original.LoaderType,
                LoaderVersion = original.LoaderVersion,
                AllocatedRamMb = original.AllocatedRamMb,
                CustomJavaPath = original.CustomJavaPath,
                JvmArgs = original.JvmArgs,
                Icon = original.Icon
            };

            var originalDir = GetProfileGameDir(original);
            var cloneDir = GetProfileGameDir(clone);

            try
            {
                if (Directory.Exists(originalDir))
                {
                    Directory.CreateDirectory(cloneDir);
                    
                    var dirsToCopy = new List<string>();
                    if (copyMods) dirsToCopy.Add("mods");
                    if (copyConfigs) dirsToCopy.Add("config");
                    if (copySaves) dirsToCopy.Add("saves");
                    if (copyShaderPacks)
                    {
                        dirsToCopy.Add("shaderpacks");
                        dirsToCopy.Add("resourcepacks");
                    }

                    foreach (var d in dirsToCopy)
                    {
                        var srcSub = Path.Combine(originalDir, d);
                        var dstSub = Path.Combine(cloneDir, d);
                        if (Directory.Exists(srcSub))
                        {
                            CopyDirectory(srcSub, dstSub);
                        }
                    }

                    var filesToCopy = new List<string> { "servers.dat" };
                    if (copyOptions)
                    {
                        filesToCopy.Add("options.txt");
                        filesToCopy.Add("optionsof.txt");
                        filesToCopy.Add("optionsshaders.txt");
                    }

                    foreach (var f in filesToCopy)
                    {
                        var srcF = Path.Combine(originalDir, f);
                        var dstF = Path.Combine(cloneDir, f);
                        if (File.Exists(srcF))
                        {
                            File.Copy(srcF, dstF, true);
                        }
                    }
                }
            }
            catch { }

            AddProfile(clone);
            return clone;
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), true);
            }
            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
            }
        }

        public string GetProfileGameDir(GameProfile profile)
        {
            var dir = Path.Combine(InstancesDir, profile.Id);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public string GetProfileModsDir(GameProfile profile)
        {
            var dir = Path.Combine(GetProfileGameDir(profile), "mods");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
