using System.Collections.Concurrent;
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

    // Each settings path needs one in-process write gate shared by all store instances.
    // Per-instance locking still permits concurrent File.Move(overwrite: true) races
    // when multiple windows/services save the same settings file.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteGates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly string _settingsFile;
    private readonly SemaphoreSlim _writeGate;

    public JsonSettingsStore()
        : this(AppPaths.SettingsFile)
    {
    }

    public JsonSettingsStore(string settingsFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFile);
        _settingsFile = Path.GetFullPath(settingsFile);
        _writeGate = WriteGates.GetOrAdd(_settingsFile, static _ => new SemaphoreSlim(1, 1));
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

            // Unique temp names prevent collisions between processes; the shared
            // per-path gate additionally serializes the final replacement in-process.
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
