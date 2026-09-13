using System.Text.Json;
using System.Text.Json.Serialization;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class JsonModsetStore : IModsetStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;

    public JsonModsetStore()
        : this(AppPaths.ModsetsFile)
    {
    }

    public JsonModsetStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public async Task<IReadOnlyList<Modset>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<Modset>();
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<Modset>>(stream, SerializerOptions, cancellationToken)
            ?? new List<Modset>();
    }

    public async Task SaveAsync(IReadOnlyCollection<Modset> modsets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modsets);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryFile = _filePath + ".tmp";
        await using (var stream = File.Create(temporaryFile))
        {
            await JsonSerializer.SerializeAsync(stream, modsets, SerializerOptions, cancellationToken);
        }

        File.Move(temporaryFile, _filePath, overwrite: true);
    }
}
