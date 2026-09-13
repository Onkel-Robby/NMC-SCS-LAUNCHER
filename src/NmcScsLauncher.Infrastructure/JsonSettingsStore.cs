using System.Text.Json;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsFile;

    public JsonSettingsStore()
        : this(AppPaths.SettingsFile)
    {
    }

    public JsonSettingsStore(string settingsFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFile);
        _settingsFile = settingsFile;
    }

    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFile))
        {
            return new LauncherSettings();
        }

        await using var stream = File.OpenRead(_settingsFile);
        return await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, SerializerOptions, cancellationToken)
            ?? new LauncherSettings();
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_settingsFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryFile = _settingsFile + ".tmp";
        await using (var stream = File.Create(temporaryFile))
        {
            await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
        }

        File.Move(temporaryFile, _settingsFile, overwrite: true);
    }
}
