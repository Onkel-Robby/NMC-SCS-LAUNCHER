using System.Text.Json;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ScsSaveBackupService : IScsSaveBackupService
{
    private readonly string _backupRoot;

    public ScsSaveBackupService()
        : this(AppPaths.SaveEditorBackupsDirectory)
    {
    }

    public ScsSaveBackupService(string backupRoot)
    {
        if (string.IsNullOrWhiteSpace(backupRoot))
            throw new ArgumentException("Backup root is required.", nameof(backupRoot));

        _backupRoot = Path.GetFullPath(backupRoot);
    }

    public async Task<ScsSaveBackupResult> CreateAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);
        cancellationToken.ThrowIfCancellationRequested();

        var source = Path.GetFullPath(save.SaveDirectory);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Save directory does not exist: {source}");

        var createdAt = DateTimeOffset.UtcNow;
        var profileName = SanitizeSegment(Path.GetFileName(save.ProfileDirectory));
        var saveName = SanitizeSegment(save.SaveDirectoryName);
        var destination = Path.Combine(
            _backupRoot,
            save.GameType.ToString(),
            profileName,
            saveName,
            $"{createdAt:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");

        try
        {
            await CopyDirectoryAsync(source, destination, cancellationToken);

            var manifest = new BackupManifest(
                "NMC SCS LAUNCHER",
                save.GameType.ToString(),
                source,
                save.ProfileDirectory,
                save.SaveDirectoryName,
                createdAt);

            var manifestPath = Path.Combine(destination, "nmc-save-backup.json");
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);

            return new ScsSaveBackupResult(source, destination, createdAt);
        }
        catch
        {
            TryDeleteDirectory(destination);
            throw;
        }
    }

    private static async Task CopyDirectoryAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var sourceInfo = new DirectoryInfo(source);
        if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Save backups do not follow reparse points.");

        Directory.CreateDirectory(destination);

        foreach (var file in sourceInfo.EnumerateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Save backup refused reparse-point file: {file.FullName}");

            var target = Path.Combine(destination, file.Name);
            await using var input = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(
                target,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await input.CopyToAsync(output, cancellationToken);
        }

        foreach (var directory in sourceInfo.EnumerateDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Save backup refused reparse-point directory: {directory.FullName}");

            await CopyDirectoryAsync(
                directory.FullName,
                Path.Combine(destination, directory.Name),
                cancellationToken);
        }
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // A failed backup must not hide the original exception.
        }
    }

    private sealed record BackupManifest(
        string Product,
        string Game,
        string SourceDirectory,
        string ProfileDirectory,
        string SaveDirectoryName,
        DateTimeOffset CreatedAtUtc);
}
