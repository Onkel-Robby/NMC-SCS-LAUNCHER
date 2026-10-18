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
    private readonly SemaphoreSlim _writeGate = new(1, 1);

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
            return new LauncherSettings();

        await using var stream = File.OpenRead(_settingsFile);
        return await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, SerializerOptions, cancellationToken)
            ?? new LauncherSettings();
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_settingsFile);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            // A fixed settings.json.tmp collides when the UI persists several
            // selections at once. Unique files also avoid collisions if two
            // launcher processes write the same settings file.
            var temporaryFile = _settingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = File.Create(temporaryFile))
                {
                    await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryFile, _settingsFile, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryFile))
                    File.Delete(temporaryFile);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
