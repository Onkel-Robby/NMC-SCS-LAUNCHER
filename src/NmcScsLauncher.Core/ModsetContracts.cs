namespace NmcScsLauncher.Core;

public interface IModsetStore
{
    Task<IReadOnlyList<Modset>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyCollection<Modset> modsets, CancellationToken cancellationToken = default);
}

public interface IModsetManager
{
    Task<IReadOnlyList<Modset>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Modset> CreateAsync(ModsetDraft draft, CancellationToken cancellationToken = default);

    Task<Modset> ImportAsync(ModsetDraft draft, CancellationToken cancellationToken = default);

    Task<Modset> UpdateAsync(Guid id, ModsetDraft draft, CancellationToken cancellationToken = default);

    Task<Modset> MarkStartedAsync(Guid id, DateTimeOffset startedAt, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
