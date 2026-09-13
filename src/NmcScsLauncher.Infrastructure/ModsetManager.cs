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
            return modsets
                .OrderBy(static item => item.Game)
                .ThenBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
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
        if (id == Guid.Empty)
        {
            throw new ModsetValidationException("Die Modset-ID ist ungültig.");
        }

        var normalizedDraft = NormalizeAndValidate(draft);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var modsets = (await _store.LoadAsync(cancellationToken)).ToList();
            var index = modsets.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Modset {id} wurde nicht gefunden.");
            }

            EnsureUniqueName(modsets, normalizedDraft.Game, normalizedDraft.Name, id);

            var existing = modsets[index];
            if (!Directory.Exists(normalizedDraft.HomeBasePath))
            {
                throw new ModsetValidationException("Der neue Home-Pfad muss beim Bearbeiten bereits existieren.");
            }

            var updated = existing with
            {
                Game = normalizedDraft.Game,
                Name = normalizedDraft.Name,
                Description = normalizedDraft.Description,
                HomeBasePath = normalizedDraft.HomeBasePath,
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

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var modsets = (await _store.LoadAsync(cancellationToken)).ToList();
            var removed = modsets.RemoveAll(item => item.Id == id);
            if (removed > 0)
            {
                await _store.SaveAsync(modsets, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }

    private async Task<Modset> AddAsync(ModsetDraft draft, bool isImport, CancellationToken cancellationToken)
    {
        var normalizedDraft = NormalizeAndValidate(draft);

        if (isImport)
        {
            if (!Directory.Exists(normalizedDraft.HomeBasePath))
            {
                throw new ModsetValidationException("Das zu importierende Home-Verzeichnis existiert nicht.");
            }
        }
        else
        {
            Directory.CreateDirectory(normalizedDraft.HomeBasePath);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var modsets = (await _store.LoadAsync(cancellationToken)).ToList();
            EnsureUniqueName(modsets, normalizedDraft.Game, normalizedDraft.Name, exceptId: null);

            var now = DateTimeOffset.UtcNow;
            var modset = new Modset
            {
                Id = Guid.NewGuid(),
                Game = normalizedDraft.Game,
                Name = normalizedDraft.Name,
                Description = normalizedDraft.Description,
                HomeBasePath = normalizedDraft.HomeBasePath,
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
        if (name.Length == 0)
        {
            throw new ModsetValidationException("Der Modset-Name darf nicht leer sein.");
        }

        if (string.IsNullOrWhiteSpace(draft.HomeBasePath))
        {
            throw new ModsetValidationException("Ein Home-Verzeichnis ist erforderlich.");
        }

        string path;
        try
        {
            path = Path.GetFullPath(draft.HomeBasePath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ModsetValidationException("Der Home-Pfad ist ungültig.");
        }

        if (!Path.IsPathFullyQualified(path))
        {
            throw new ModsetValidationException("Der Home-Pfad muss absolut sein.");
        }

        return draft with
        {
            Name = name,
            Description = NormalizeOptional(draft.Description),
            HomeBasePath = path,
            PreferredProfile = NormalizeOptional(draft.PreferredProfile),
            AdditionalLaunchArguments = NormalizeOptional(draft.AdditionalLaunchArguments)
        };
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureUniqueName(IEnumerable<Modset> modsets, GameType game, string name, Guid? exceptId)
    {
        if (modsets.Any(item =>
                item.Game == game &&
                item.Id != exceptId &&
                string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new ModsetValidationException("Für dieses Spiel existiert bereits ein Modset mit diesem Namen.");
        }
    }
}
