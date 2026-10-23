using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;

namespace ServantLauncher.Models
{
    public class ModrinthSearchResult
    {
        [JsonPropertyName("hits")]
        public List<ModrinthProjectHit> Hits { get; set; } = new();

        [JsonPropertyName("total_hits")]
        public int TotalHits { get; set; }
    }

    public class ModrinthProjectHit : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = string.Empty;

        [JsonPropertyName("project_type")]
        public string ProjectType { get; set; } = "mod";

        public bool IsModpack => string.Equals(ProjectType, "modpack", StringComparison.OrdinalIgnoreCase);

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = new();

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("downloads")]
        public long Downloads { get; set; }

        [JsonPropertyName("follows")]
        public long Follows { get; set; }

        [JsonPropertyName("versions")]
        public List<string> Versions { get; set; } = new();

        [JsonPropertyName("gallery")]
        public List<string> Gallery { get; set; } = new();

        [JsonPropertyName("featured_gallery")]
        public string? FeaturedGallery { get; set; }

        private Bitmap? _iconBitmap;
        [JsonIgnore]
        public Bitmap? IconBitmap
        {
            get => _iconBitmap;
            set
            {
                if (SetProperty(ref _iconBitmap, value))
                {
                    OnPropertyChanged(nameof(HasIconBitmap));
                }
            }
        }

        [JsonIgnore]
        public bool HasIconBitmap => _iconBitmap != null;

        private bool _isInstalled;
        [JsonIgnore]
        public bool IsInstalled
        {
            get => _isInstalled;
            set => SetProperty(ref _isInstalled, value);
        }

        public string FormattedDownloads => Downloads >= 1_000_000 
            ? $"{Downloads / 1_000_000.0:F1}M" 
            : Downloads >= 1_000 
                ? $"{Downloads / 1000.0:F1}K" 
                : Downloads.ToString();

        public string DisplayCategories => string.Join(" • ", Categories);
    }

    public class ModrinthProjectDetails
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = new();

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("downloads")]
        public long Downloads { get; set; }

        [JsonPropertyName("followers")]
        public long Followers { get; set; }

        [JsonPropertyName("gallery")]
        public List<ModrinthGalleryItem> Gallery { get; set; } = new();
    }

    public class ModrinthGalleryItem
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("featured")]
        public bool Featured { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
    }

    public class ModrinthVersion
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version_number")]
        public string VersionNumber { get; set; } = string.Empty;

        [JsonPropertyName("game_versions")]
        public List<string> GameVersions { get; set; } = new();

        [JsonPropertyName("loaders")]
        public List<string> Loaders { get; set; } = new();

        [JsonPropertyName("files")]
        public List<ModrinthFile> Files { get; set; } = new();

        [JsonPropertyName("dependencies")]
        public List<ModrinthDependency> Dependencies { get; set; } = new();

        [JsonPropertyName("version_type")]
        public string VersionType { get; set; } = "release";

        [JsonPropertyName("date_published")]
        public DateTime DatePublished { get; set; }

        public bool IsRelease => string.Equals(VersionType, "release", StringComparison.OrdinalIgnoreCase);
        public bool IsBeta => string.Equals(VersionType, "beta", StringComparison.OrdinalIgnoreCase);
        public bool IsAlpha => string.Equals(VersionType, "alpha", StringComparison.OrdinalIgnoreCase);

        public string FormattedDate => DatePublished > DateTime.MinValue ? DatePublished.ToLocalTime().ToString("dd.MM.yyyy") : "";
        public string DisplayGameVersions => string.Join(", ", GameVersions);
        public string DisplayLoaders => string.Join(", ", Loaders);
    }

    public class ModrinthDependency
    {
        [JsonPropertyName("version_id")]
        public string? VersionId { get; set; }

        [JsonPropertyName("project_id")]
        public string? ProjectId { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }

        [JsonPropertyName("dependency_type")]
        public string DependencyType { get; set; } = "required"; // "required", "optional", "incompatible", "embedded"
    }

    public class ModrinthFile
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("filename")]
        public string FileName { get; set; } = string.Empty;

        [JsonIgnore]
        public string Filename => FileName;

        [JsonPropertyName("primary")]
        public bool Primary { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("hashes")]
        public ModrinthHashes Hashes { get; set; } = new();

        public string DisplaySize => $"{Size / 1024.0 / 1024.0:F2} MB";
    }

    public class ModrinthHashes
    {
        [JsonPropertyName("sha1")]
        public string Sha1 { get; set; } = string.Empty;

        [JsonPropertyName("sha512")]
        public string Sha512 { get; set; } = string.Empty;
    }

    public enum MarkdownBlockType
    {
        Heading1,
        Heading2,
        Heading3,
        Paragraph,
        Bullet,
        Code,
        Image,
        Table
    }

    public class MarkdownBlock : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public MarkdownBlockType Type { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public bool IsBadge { get; set; }

        private Bitmap? _imageBitmap;
        public Bitmap? ImageBitmap
        {
            get => _imageBitmap;
            set
            {
                if (SetProperty(ref _imageBitmap, value))
                {
                    OnPropertyChanged(nameof(HasImageBitmap));
                }
            }
        }

        public bool HasImageBitmap => _imageBitmap != null;

        public bool IsHeading1 => Type == MarkdownBlockType.Heading1;
        public bool IsHeading2 => Type == MarkdownBlockType.Heading2;
        public bool IsHeading3 => Type == MarkdownBlockType.Heading3;
        public bool IsParagraph => Type == MarkdownBlockType.Paragraph;
        public bool IsBullet => Type == MarkdownBlockType.Bullet;
        public bool IsCode => Type == MarkdownBlockType.Code;
        public bool IsImage => Type == MarkdownBlockType.Image;
        public bool IsTable => Type == MarkdownBlockType.Table;
    }
}
