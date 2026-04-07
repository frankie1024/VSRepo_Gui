using System.Text.Json.Serialization;

namespace vsrepo_Gui.Models;

public sealed class VsPackageRoot
{
    [JsonPropertyName("file-format")]
    public int FileFormat { get; set; }

    [JsonPropertyName("packages")]
    public List<VsPackageDefinition> Packages { get; set; } = [];
}

public sealed class VsPackageDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("website")]
    public string? Website { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = string.Empty;

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("modulename")]
    public string? ModuleName { get; set; }

    [JsonPropertyName("wheelname")]
    public string? WheelName { get; set; }

    [JsonPropertyName("github")]
    public string? Github { get; set; }

    [JsonPropertyName("doom9")]
    public string? Doom9 { get; set; }

    [JsonPropertyName("dependencies")]
    public List<string>? Dependencies { get; set; }

    [JsonPropertyName("releases")]
    public List<VsRelease> Releases { get; set; } = [];
}

public sealed class VsRelease
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("published")]
    public string? Published { get; set; }

    [JsonPropertyName("win32")]
    public VsPayload? Win32 { get; set; }

    [JsonPropertyName("win64")]
    public VsPayload? Win64 { get; set; }

    [JsonPropertyName("script")]
    public VsPayload? Script { get; set; }

    [JsonPropertyName("wheel")]
    public VsPayload? Wheel { get; set; }
}

public sealed class VsPayload
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    [JsonPropertyName("api")]
    public int? Api { get; set; }

    [JsonPropertyName("files")]
    public Dictionary<string, string[]>? Files { get; set; }
}

public enum PackageInstallState
{
    NotInstalled,
    Installed,
    InstalledUnknown,
    UpdateAvailable,
}

public sealed class PackageItem
{
    public string Name { get; init; } = string.Empty;
    public string NamespaceOrModule { get; init; } = string.Empty;
    public string Identifier { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? Website { get; init; }
    public string? Github { get; init; }
    public string? Doom9 { get; init; }
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    public string InstalledVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string LatestPublishedText { get; init; } = "Unknown";
    public PackageInstallState State { get; init; }

    public string ActionText => State switch
    {
        PackageInstallState.NotInstalled => "Install",
        PackageInstallState.UpdateAvailable => "Upgrade",
        PackageInstallState.InstalledUnknown => "Force Upgrade",
        _ => "Uninstall",
    };

    public string StateText => State switch
    {
        PackageInstallState.NotInstalled => "Not Installed",
        PackageInstallState.UpdateAvailable => "Update Available",
        PackageInstallState.InstalledUnknown => "Unknown Version",
        _ => "Installed",
    };

    public bool HasWebsite => !string.IsNullOrWhiteSpace(Website);
    public bool HasGithub => !string.IsNullOrWhiteSpace(Github);
    public bool HasDoom9 => !string.IsNullOrWhiteSpace(Doom9);
}
