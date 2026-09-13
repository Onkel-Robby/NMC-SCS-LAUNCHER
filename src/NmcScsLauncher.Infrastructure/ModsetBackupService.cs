using System.IO.Compression;
using System.Text.Json;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ModsetBackupService : IModsetBackupService
{
    private const int BufferSize = 128 * 1024;
    private const int ManifestSchemaVersion = 1;
    private const string ManifestEntryName = "nmc-backup.json";
    private readonly IModsetManager _modsetManager;

    public ModsetBackupService(IModsetManager modsetManager)
    {
        _modsetManager = modsetManager ?? throw new ArgumentNullException(nameof(modsetManager));
    }

    public async Task<ModsetBackupResult> CreateAsync(
        ModsetBackupRequest request,
        IProgress<ModsetBackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.ModsetId == Guid.Empty)
            throw new ModsetValidationException("Das Modset für das Backup ist ungültig.");
        if (request.Content == ModsetBackupContent.None)
            throw new ModsetValidationException("Für das Backup muss mindestens ein Inhaltsbereich ausgewählt werden.");

        var archivePath = NormalizeArchivePath(request.ArchivePath);
        if (File.Exists(archivePath) || Directory.Exists(archivePath))
            throw new ModsetValidationException("Die Backup-Datei existiert bereits. Bitte einen neuen Dateinamen wählen.");

        var modsets = await _modsetManager.GetAllAsync(cancellationToken);
        var modset = modsets.FirstOrDefault(item => item.Id == request.ModsetId)
            ?? throw new KeyNotFoundException($"Modset {request.ModsetId} wurde nicht gefunden.");

        var gameDataDirectory = Path.Combine(Path.GetFullPath(modset.HomeBasePath), GameDefinition.For(modset.Game).HomeDirectoryName);
        if (!Directory.Exists(gameDataDirectory))
            throw new DirectoryNotFoundException("Der SCS-Datenordner des Modsets existiert noch nicht.");

        var warnings = new List<string>();
        var files = BuildFilePlan(gameDataDirectory, request.Content, warnings, cancellationToken);
        if (files.Count == 0)
            warnings.Add("Für die ausgewählten Backup-Bereiche wurden keine Dateien gefunden.");

        var totalBytes = files.Sum(static file => file.Length);
        progress?.Report(new ModsetBackupProgress("Scanning", 0, totalBytes, 0, files.Count));

        var manifest = new ModsetBackupManifest(
            ManifestSchemaVersion,
            "NMC SCS LAUNCHER",
            GetLauncherVersion(),
            DateTimeOffset.UtcNow,
            modset.Id,
            modset.Name,
            modset.Game,
            request.Content,
            files.Select(static file => new ModsetBackupManifestFile(file.RelativePath, file.Length, file.LastWriteTimeUtc)).ToArray());

        var parentDirectory = Path.GetDirectoryName(archivePath);
        if (string.IsNullOrWhiteSpace(parentDirectory))
            throw new ModsetValidationException("Der Backup-Zielordner ist ungültig.");
        Directory.CreateDirectory(parentDirectory);

        var stagingPath = archivePath + ".partial-" + Guid.NewGuid().ToString("N");
        long bytesArchived = 0;
        var filesArchived = 0;

        await using (var archiveStream = new FileStream(
                         stagingPath,
                         FileMode.CreateNew,
                         FileAccess.ReadWrite,
                         FileShare.None,
                         BufferSize,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryName = "data/" + file.RelativePath.Replace(Path.DirectorySeparatorChar, '/');
                var compression = string.Equals(Path.GetExtension(file.SourcePath), ".scs", StringComparison.OrdinalIgnoreCase)
                    ? CompressionLevel.NoCompression
                    : CompressionLevel.Fastest;
                var entry = archive.CreateEntry(entryName, compression);
                entry.LastWriteTime = file.LastWriteTimeUtc;

                await using var source = new FileStream(
                    file.SourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var destination = entry.Open();
                var buffer = new byte[BufferSize];
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (read == 0) break;
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    bytesArchived += read;
                    progress?.Report(new ModsetBackupProgress(
                        "Archiving",
                        bytesArchived,
                        totalBytes,
                        filesArchived,
                        files.Count,
                        file.RelativePath));
                }

                filesArchived++;
                progress?.Report(new ModsetBackupProgress(
                    "Archiving",
                    bytesArchived,
                    totalBytes,
                    filesArchived,
                    files.Count,
                    file.RelativePath));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Fastest);
            await using var manifestStream = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(manifestStream, manifest, cancellationToken: cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        File.Move(stagingPath, archivePath);
        progress?.Report(new ModsetBackupProgress("Completed", bytesArchived, totalBytes, filesArchived, files.Count));
        return new ModsetBackupResult(archivePath, filesArchived, bytesArchived, manifest, warnings);
    }

    private static List<BackupFile> BuildFilePlan(
        string gameDataDirectory,
        ModsetBackupContent content,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var files = new List<BackupFile>();
        if (content.HasFlag(ModsetBackupContent.Configuration))
            AddRootConfigurationFiles(gameDataDirectory, files, warnings, cancellationToken);
        if (content.HasFlag(ModsetBackupContent.Profiles))
        {
            AddDirectoryTree(gameDataDirectory, Path.Combine(gameDataDirectory, "profiles"), files, warnings, cancellationToken);
            AddDirectoryTree(gameDataDirectory, Path.Combine(gameDataDirectory, "steam_profiles"), files, warnings, cancellationToken);
        }
        if (content.HasFlag(ModsetBackupContent.Mods))
            AddDirectoryTree(gameDataDirectory, Path.Combine(gameDataDirectory, "mod"), files, warnings, cancellationToken);

        return files
            .GroupBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddRootConfigurationFiles(
        string gameDataDirectory,
        ICollection<BackupFile> files,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(gameDataDirectory, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Der SCS-Datenordner konnte für das Backup nicht gelesen werden.", ex);
        }

        foreach (var file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(file);
            if (!extension.Equals(".cfg", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".sii", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".ini", StringComparison.OrdinalIgnoreCase))
                continue;
            TryAddFile(gameDataDirectory, file, files, warnings);
        }
    }

    private static void AddDirectoryTree(
        string gameDataDirectory,
        string root,
        ICollection<BackupFile> files,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root)) return;
        if (IsReparsePoint(root))
        {
            warnings.Add($"'{Path.GetRelativePath(gameDataDirectory, root)}' wurde nicht gesichert, weil es ein Link/Junction ist.");
            return;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            string[] directories;
            string[] currentFiles;
            try
            {
                directories = Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly);
                currentFiles = Directory.GetFiles(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"'{current}' konnte für das Backup nicht vollständig gelesen werden.", ex);
            }

            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsReparsePoint(directory))
                    warnings.Add($"'{Path.GetRelativePath(gameDataDirectory, directory)}' wurde nicht gesichert, weil es ein Link/Junction ist.");
                else
                    pending.Push(directory);
            }

            foreach (var file in currentFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryAddFile(gameDataDirectory, file, files, warnings);
            }
        }
    }

    private static void TryAddFile(
        string gameDataDirectory,
        string file,
        ICollection<BackupFile> files,
        ICollection<string> warnings)
    {
        if (IsReparsePoint(file))
        {
            warnings.Add($"'{Path.GetRelativePath(gameDataDirectory, file)}' wurde nicht gesichert, weil es ein Link ist.");
            return;
        }

        var info = new FileInfo(file);
        files.Add(new BackupFile(
            file,
            Path.GetRelativePath(gameDataDirectory, file),
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Die Dateiattribute von '{path}' konnten nicht gelesen werden.", ex);
        }
    }

    private static string NormalizeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ModsetValidationException("Bitte einen Zielpfad für die Backup-Datei angeben.");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ModsetValidationException("Der Backup-Zielpfad ist ungültig.");
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new ModsetValidationException("Backup-Dateien müssen die Endung .zip verwenden.");
        return fullPath;
    }

    private static string GetLauncherVersion()
    {
        var version = typeof(ModsetBackupService).Assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private sealed record BackupFile(
        string SourcePath,
        string RelativePath,
        long Length,
        DateTimeOffset LastWriteTimeUtc);
}
