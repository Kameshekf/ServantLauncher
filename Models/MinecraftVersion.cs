using System;
using System.Text.Json.Serialization;

namespace ServantLauncher.Models
{
    public class MinecraftVersion
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = "release"; // "release" or "snapshot"

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("time")]
        public DateTimeOffset? Time { get; set; }

        [JsonPropertyName("releaseTime")]
        public DateTimeOffset? ReleaseTime { get; set; }

        public bool IsRelease => string.Equals(Type, "release", StringComparison.OrdinalIgnoreCase);
        public bool IsSnapshot => !IsRelease;

        public override string ToString() => Id;
    }

    public class MojangVersionManifest
    {
        [JsonPropertyName("latest")]
        public LatestVersions? Latest { get; set; }

        [JsonPropertyName("versions")]
        public MinecraftVersion[] Versions { get; set; } = Array.Empty<MinecraftVersion>();
    }

    public class LatestVersions
    {
        [JsonPropertyName("release")]
        public string Release { get; set; } = string.Empty;

        [JsonPropertyName("snapshot")]
        public string Snapshot { get; set; } = string.Empty;
    }

    public class FabricLoaderInfo
    {
        [JsonPropertyName("loader")]
        public FabricLoaderDetail? Loader { get; set; }

        public string Version => Loader?.Version ?? "Unknown";
        public bool Stable => Loader?.Stable ?? false;

        public override string ToString() => $"Fabric {Version}" + (Stable ? " (Stable)" : "");
    }

    public class FabricLoaderDetail
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("stable")]
        public bool Stable { get; set; }
    }
}
