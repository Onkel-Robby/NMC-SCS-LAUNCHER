using System.Text.Json.Serialization;

namespace NmcScsLauncher.Core;

public sealed record Modset
{
    public required Guid Id { get; init; }

    public required GameType Game { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required string HomeBasePath { get; init; }

    public string? ModDirectoryPath { get; init; }

    [JsonIgnore]
    public string ModFolderDisplayPath => !string.IsNullOrWhiteSpace(ModDirectoryPath)
        ? ModDirectoryPath
        : Path.Combine(HomeBasePath, GameDefinition.For(Game).HomeDirectoryName, "mod");

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? LastStartedAt { get; init; }

    public string? PreferredProfile { get; init; }

    public string? AdditionalLaunchArguments { get; init; }

    public required bool IsManagedDirectory { get; init; }
}

public sealed record ModsetDraft(
    GameType Game,
    string Name,
    string? Description,
    string HomeBasePath,
    string? PreferredProfile = null,
    string? AdditionalLaunchArguments = null,
    string? ModDirectoryPath = null);

public sealed class ModsetValidationException : Exception
{
    public ModsetValidationException(string message)
        : base(message)
    {
    }
}
