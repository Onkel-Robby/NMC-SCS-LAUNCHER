using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ModsetManager : IModsetManager, IDisposable
{
    private readonly IModsetStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ModsetManager(IModsetStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<IReadOnlyList<Modset>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var modsets = await _store.LoadAsync(cancellationToken);
            return modsets.OrderBy(static item => item.Game).ThenBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<Modset> CreateAsync(ModsetDraft draft, CancellationToken cancellationToken = default) =>
        AddAsync(draft, isImport: false, cancellationToken);

    public Task<Modset> ImportAsync(ModsetDraft draft, CancellationToken cancellationToken = default) =>
        AddAsync(draft, isImport: true, cancellationToken);

    public async Task<Modset> UpdateAsync(Guid id, ModsetDraft draft, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) throw new ModsetValidationException("Die Modset-ID ist ungültig.");
        var normalizedDraft = NormalizeAndValidate(draft);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var modsets = (await _store.LoadAsync(cancellationToken)).ToList();
            var index = modsets.FindIndex(item => item.Id == id);
            if (index < 0) throw new KeyNotFoundException($"Modset {id} wurde nicht gefunden.");

            EnsureUniqueName(modsets, normalizedDraft.Game, normalizedDraft.Name, id);
            var existing = modsets[index];

            string homeBasePath;
            string? modDirectoryPath;
            if (!string.IsNullOrWhiteSpace(normalizedDraft.ModDirectoryPath))
            {
                modDirectoryPath = normalizedDraft.ModDirectoryPath;
                if (!Directory.Exists(modDirectoryPath))
                    throw new ModsetValidationException("Der ausgewählte Mod-Ordner muss beim Bearbeiten bereits existieren.");

                homeBasePath = !string.IsNullOrWhiteSpace(existing.ModDirectoryPath)
                    ? existing.HomeBasePath
                    : BuildRuntimeHomePath(id);
                Directory.CreateDirectory(homeBasePath);
            }
            else
            {
                homeBasePath = normalizedDraft.HomeBasePath;
                modDirectoryPath = null;
                if (!Directory.Exists(homeBasePath))
                    throw new ModsetValidationException("Der neue Home-Pfad muss beim Bearbeiten bereits existieren.");
            }

            var updated = existing with
            {
                Game = normalizedDraft.Game,
                Name = normalizedDraft.Name,
                Description = normalizedDraft.Description,
                HomeBasePath = homeBasePath,
                ModDirectoryPath = modDirectoryPath,
                PreferredProfile = normalizedDraft.PreferredProfile,
                AdditionalLaunchArguments = normalizedDraft.AdditionalLaunchArguments,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            modsets[index] = updated;
            await _store.SaveAsync(modsets, cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Modset> MarkStartedAsync(Guid id, DateTimeOffset startedAt, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) throw new ModsetValidationException("Die Modset-ID ist ungültig.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var modsets = (await _store.LoadAsync(cancellationToken)).ToList();
            var index = modsets.FindIndex(item => item.Id == id);
            if (index < 0) throw new KeyNotFoundException($"Modset {id} wurde nicht gefunden.");

            var updated = modsets[index] with
            {
                LastStartedAt = startedAt,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            modsets[index] = updated;
            await _store.SaveAsync(modsets, cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var modsets = (await _store.LoadAsync(cancellationToken)).ToList();
            if (modsets.RemoveAll(item => item.Id == id) > 0) await _store.SaveAsync(modsets, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<Modset> AddAsync(ModsetDraft draft, bool isImport, CancellationToken cancellationToken)
    {
        var normalizedDraft = NormalizeAndValidate(draft);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var modsets = (await _store.LoadAsync(cancellationToken)).ToList();
            EnsureUniqueName(modsets, normalizedDraft.Game, normalizedDraft.Name, exceptId: null);

            var id = Guid.NewGuid();
            string homeBasePath;
            string? modDirectoryPath;

            if (!string.IsNullOrWhiteSpace(normalizedDraft.ModDirectoryPath))
            {
                modDirectoryPath = normalizedDraft.ModDirectoryPath;
                if (isImport)
                {
                    if (!Directory.Exists(modDirectoryPath))
                        throw new ModsetValidationException("Der zu importierende Mod-Ordner existiert nicht.");
                }
                else
                {
                    Directory.CreateDirectory(modDirectoryPath);
                }

                homeBasePath = BuildRuntimeHomePath(id);
                Directory.CreateDirectory(homeBasePath);
            }
            else
            {
                homeBasePath = normalizedDraft.HomeBasePath;
                modDirectoryPath = null;
                if (isImport)
                {
                    if (!Directory.Exists(homeBasePath))
                        throw new ModsetValidationException("Das zu importierende Home-Verzeichnis existiert nicht.");
                }
                else
                {
                    Directory.CreateDirectory(homeBasePath);
                }
            }

            var now = DateTimeOffset.UtcNow;
            var modset = new Modset
            {
                Id = id,
                Game = normalizedDraft.Game,
                Name = normalizedDraft.Name,
                Description = normalizedDraft.Description,
                HomeBasePath = homeBasePath,
                ModDirectoryPath = modDirectoryPath,
                CreatedAt = now,
                UpdatedAt = now,
                PreferredProfile = normalizedDraft.PreferredProfile,
                AdditionalLaunchArguments = normalizedDraft.AdditionalLaunchArguments,
                IsManagedDirectory = !isImport
            };
            modsets.Add(modset);
            await _store.SaveAsync(modsets, cancellationToken);
            return modset;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ModsetDraft NormalizeAndValidate(ModsetDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var name = draft.Name.Trim();
        if (name.Length == 0) throw new ModsetValidationException("Der Modset-Name darf nicht leer sein.");

        string homePath = string.Empty;
        string? modDirectoryPath = null;

        if (!string.IsNullOrWhiteSpace(draft.ModDirectoryPath))
        {
            modDirectoryPath = NormalizePath(draft.ModDirectoryPath, "Der Mod-Ordner-Pfad ist ungültig.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(draft.HomeBasePath))
                throw new ModsetValidationException("Ein Home-Verzeichnis ist erforderlich.");

            homePath = NormalizePath(draft.HomeBasePath, "Der Home-Pfad ist ungültig.");
        }

        return draft with
        {
            Name = name,
            Description = NormalizeOptional(draft.Description),
            HomeBasePath = homePath,
            ModDirectoryPath = modDirectoryPath,
            PreferredProfile = NormalizeOptional(draft.PreferredProfile),
            AdditionalLaunchArguments = NormalizeOptional(draft.AdditionalLaunchArguments)
        };
    }

    private static string NormalizePath(string value, string errorMessage)
    {
        string path;
        try { path = Path.GetFullPath(value.Trim()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ModsetValidationException(errorMessage);
        }

        if (!Path.IsPathFullyQualified(path))
            throw new ModsetValidationException(errorMessage);

        return Path.TrimEndingDirectorySeparator(path);
    }

    private static string BuildRuntimeHomePath(Guid id) =>
        Path.Combine(AppPaths.RuntimeHomesDirectory, id.ToString("N"));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureUniqueName(IEnumerable<Modset> modsets, GameType game, string name, Guid? exceptId)
    {
        if (modsets.Any(item => item.Game == game && item.Id != exceptId && string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            throw new ModsetValidationException("Für dieses Spiel existiert bereits ein Modset mit diesem Namen.");
    }
}
