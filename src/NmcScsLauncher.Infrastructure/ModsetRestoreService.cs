using System.IO.Compression;
using System.Text.Json;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ModsetRestoreService : IModsetRestoreService
{
    private const int BufferSize = 128 * 1024;
    private const int ManifestSchemaVersion = 1;
    private const string ProductName = "NMC SCS LAUNCHER";
    private const string ManifestEntryName = "nmc-backup.json";
    private readonly IModsetManager _modsetManager;

    public ModsetRestoreService(IModsetManager modsetManager)
    {
        _modsetManager = modsetManager ?? throw new ArgumentNullException(nameof(modsetManager));
    }

    public async Task<ModsetRestorePreview> InspectAsync(
        Guid targetModsetId,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = await GetTargetModsetAsync(targetModsetId, cancellationToken);
        var normalizedArchivePath = NormalizeExistingArchivePath(archivePath);

        try
        {
            using var archive = ZipFile.OpenRead(normalizedArchivePath);
            var manifest = await ReadManifestAsync(archive, cancellationToken);
            var gameRoot = GetGameDataDirectory(target);
            var entries = ValidateArchive(archive, manifest, target, gameRoot);
            var existing = entries.Count(item => File.Exists(item.DestinationPath));
            return new ModsetRestorePreview(
                normalizedArchivePath,
                manifest,
                entries.Count,
                entries.Sum(static item => item.ManifestFile.Length),
                existing,
                entries.Count - existing);
        }
        catch (ModsetBackupValidationException)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            throw new ModsetBackupValidationException("Die ZIP-Datei ist kein gültiges NMC-SCS-Launcher-Backup.", ex);
        }
    }

    public async Task<ModsetRestoreResult> RestoreAsync(
        ModsetRestoreRequest request,
        IProgress<ModsetBackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var preview = await InspectAsync(request.TargetModsetId, request.ArchivePath, cancellationToken);
        if (preview.ExistingTargetFiles > 0 && !request.OverwriteExisting)
        {
            throw new ModsetBackupValidationException(
                $"Das Backup würde {preview.ExistingTargetFiles} vorhandene Datei(en) ersetzen. Eine ausdrückliche Überschreib-Freigabe ist erforderlich.");
        }

        var target = await GetTargetModsetAsync(request.TargetModsetId, cancellationToken);
        var gameRoot = GetGameDataDirectory(target);
        Directory.CreateDirectory(gameRoot);

        try
        {
            using var archive = ZipFile.OpenRead(preview.ArchivePath);
            var manifest = await ReadManifestAsync(archive, cancellationToken);
            var entries = ValidateArchive(archive, manifest, target, gameRoot);
            var totalBytes = entries.Sum(static item => item.ManifestFile.Length);
            progress?.Report(new ModsetBackupProgress("Restoring", 0, totalBytes, 0, entries.Count));

            long bytesRestored = 0;
            var filesRestored = 0;
            var filesOverwritten = 0;

            foreach (var item in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureSafeTargetChain(gameRoot, item.DestinationPath);
                var destinationDirectory = Path.GetDirectoryName(item.DestinationPath)
                    ?? throw new ModsetBackupValidationException("Ein Zielpfad im Backup ist ungültig.");
                Directory.CreateDirectory(destinationDirectory);

                var existed = File.Exists(item.DestinationPath);
                if (existed && !request.OverwriteExisting)
                    throw new ModsetBackupValidationException("Eine vorhandene Datei darf ohne Bestätigung nicht überschrieben werden.");

                var stagingPath = item.DestinationPath + ".nmc-restore-" + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var source = item.Entry.Open())
                await using (var destination = new FileStream(
                                 stagingPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 BufferSize,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[BufferSize];
                    while (true)
                    {
                        var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                        if (read == 0) break;
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        bytesRestored += read;
                        progress?.Report(new ModsetBackupProgress(
                            "Restoring",
                            bytesRestored,
                            totalBytes,
                            filesRestored,
                            entries.Count,
                            item.ManifestFile.RelativePath));
                    }
                    await destination.FlushAsync(cancellationToken);
                }

                var stagedLength = new FileInfo(stagingPath).Length;
                if (stagedLength != item.ManifestFile.Length)
                {
                    throw new ModsetBackupValidationException(
                        $"Die wiederhergestellte Datei '{item.ManifestFile.RelativePath}' hat eine unerwartete Größe.");
                }

                File.Move(stagingPath, item.DestinationPath, overwrite: request.OverwriteExisting);
                File.SetLastWriteTimeUtc(item.DestinationPath, item.ManifestFile.LastWriteTimeUtc.UtcDateTime);
                filesRestored++;
                if (existed) filesOverwritten++;
                progress?.Report(new ModsetBackupProgress(
                    "Restoring",
                    bytesRestored,
                    totalBytes,
                    filesRestored,
                    entries.Count,
                    item.ManifestFile.RelativePath));
            }

            progress?.Report(new ModsetBackupProgress("Completed", bytesRestored, totalBytes, filesRestored, entries.Count));
            return new ModsetRestoreResult(filesRestored, bytesRestored, filesOverwritten, manifest);
        }
        catch (ModsetBackupValidationException)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            throw new ModsetBackupValidationException("Die ZIP-Datei ist kein gültiges NMC-SCS-Launcher-Backup.", ex);
        }
    }

    private async Task<Modset> GetTargetModsetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
            throw new ModsetBackupValidationException("Das Ziel-Modset ist ungültig.");

        var modsets = await _modsetManager.GetAllAsync(cancellationToken);
        return modsets.FirstOrDefault(item => item.Id == id)
            ?? throw new KeyNotFoundException($"Modset {id} wurde nicht gefunden.");
    }

    private static async Task<ModsetBackupManifest> ReadManifestAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var manifestEntry = archive.GetEntry(ManifestEntryName)
            ?? throw new ModsetBackupValidationException("Das Backup enthält kein NMC-Manifest.");
        if (manifestEntry.Length <= 0 || manifestEntry.Length > 2 * 1024 * 1024)
            throw new ModsetBackupValidationException("Das Backup-Manifest hat eine ungültige Größe.");

        await using var stream = manifestEntry.Open();
        ModsetBackupManifest? manifest;
        try
        {
            manifest = await JsonSerializer.DeserializeAsync<ModsetBackupManifest>(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new ModsetBackupValidationException("Das Backup-Manifest ist beschädigt.", ex);
        }

        return manifest ?? throw new ModsetBackupValidationException("Das Backup-Manifest ist leer.");
    }

    private static List<ValidatedEntry> ValidateArchive(
        ZipArchive archive,
        ModsetBackupManifest manifest,
        Modset target,
        string gameRoot)
    {
        if (manifest.SchemaVersion != ManifestSchemaVersion)
            throw new ModsetBackupValidationException($"Backup-Schema {manifest.SchemaVersion} wird nicht unterstützt.");
        if (!string.Equals(manifest.Product, ProductName, StringComparison.Ordinal))
            throw new ModsetBackupValidationException("Das Archiv stammt nicht vom NMC SCS LAUNCHER.");
        if (manifest.Game != target.Game)
            throw new ModsetBackupValidationException("Das Backup gehört zu einem anderen Spiel als das ausgewählte Ziel-Modset.");
        if (manifest.Files is null)
            throw new ModsetBackupValidationException("Das Backup-Manifest enthält keine Dateiliste.");

        var manifestFiles = new Dictionary<string, ModsetBackupManifestFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifestFile in manifest.Files)
        {
            var portablePath = ValidatePortableRelativePath(manifestFile.RelativePath);
            if (manifestFile.Length < 0)
                throw new ModsetBackupValidationException($"Ungültige Dateigröße im Manifest: {portablePath}");
            if (!manifestFiles.TryAdd(portablePath, manifestFile with { RelativePath = portablePath }))
                throw new ModsetBackupValidationException($"Doppelter Dateipfad im Manifest: {portablePath}");
        }

        var validated = new List<ValidatedEntry>(manifestFiles.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (string.Equals(entry.FullName, ManifestEntryName, StringComparison.Ordinal))
                continue;
            if (!entry.FullName.StartsWith("data/", StringComparison.Ordinal) || entry.FullName.EndsWith('/', StringComparison.Ordinal))
                throw new ModsetBackupValidationException($"Unerwarteter Eintrag im Backup: {entry.FullName}");

            var portablePath = ValidatePortableRelativePath(entry.FullName[5..]);
            if (!seen.Add(portablePath))
                throw new ModsetBackupValidationException($"Doppelter ZIP-Eintrag: {portablePath}");
            if (!manifestFiles.TryGetValue(portablePath, out var manifestFile))
                throw new ModsetBackupValidationException($"ZIP-Eintrag fehlt im Manifest: {portablePath}");
            if (entry.Length != manifestFile.Length)
                throw new ModsetBackupValidationException($"Dateigröße stimmt nicht mit dem Manifest überein: {portablePath}");

            var destination = BuildSafeDestinationPath(gameRoot, portablePath);
            validated.Add(new ValidatedEntry(entry, manifestFile, destination));
        }

        if (validated.Count != manifestFiles.Count)
            throw new ModsetBackupValidationException("Mindestens eine im Manifest aufgeführte Datei fehlt im ZIP-Archiv.");

        return validated.OrderBy(static item => item.ManifestFile.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ValidatePortableRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ModsetBackupValidationException("Das Backup enthält einen leeren Dateipfad.");

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/', StringComparison.Ordinal) || normalized.Contains(':', StringComparison.Ordinal))
            throw new ModsetBackupValidationException($"Absoluter oder ungültiger Dateipfad im Backup: {path}");

        var segments = normalized.Split('/');
        if (segments.Any(static segment => segment.Length == 0 || segment == "." || segment == ".."))
            throw new ModsetBackupValidationException($"Unsicherer Dateipfad im Backup: {path}");
        return string.Join('/', segments);
    }

    private static string BuildSafeDestinationPath(string gameRoot, string portableRelativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        var relative = portableRelativePath.Replace('/', Path.DirectorySeparatorChar);
        var destination = Path.GetFullPath(Path.Combine(root, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!destination.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new ModsetBackupValidationException($"Dateipfad verlässt den SCS-Datenordner: {portableRelativePath}");
        return destination;
    }

    private static void EnsureSafeTargetChain(string gameRoot, string destinationPath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        if (Directory.Exists(root) && File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            throw new ModsetBackupValidationException("Der SCS-Zielordner ist ein Link/Junction und kann nicht sicher wiederhergestellt werden.");

        var parent = Path.GetDirectoryName(destinationPath)
            ?? throw new ModsetBackupValidationException("Ein Zielpfad im Backup ist ungültig.");
        var relativeParent = Path.GetRelativePath(root, parent);
        var current = root;
        if (relativeParent != ".")
        {
            foreach (var segment in relativeParent.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (File.Exists(current))
                    throw new ModsetBackupValidationException($"Ein Zielverzeichnis kollidiert mit einer Datei: {current}");
                if (Directory.Exists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    throw new ModsetBackupValidationException($"Ein Zielverzeichnis ist ein Link/Junction: {current}");
            }
        }

        if (File.Exists(destinationPath) && File.GetAttributes(destinationPath).HasFlag(FileAttributes.ReparsePoint))
            throw new ModsetBackupValidationException($"Eine Zieldatei ist ein Link: {destinationPath}");
    }

    private static string NormalizeExistingArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ModsetBackupValidationException("Bitte eine Backup-Datei auswählen.");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ModsetBackupValidationException("Der Backup-Pfad ist ungültig.", ex);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new ModsetBackupValidationException("Backup-Dateien müssen die Endung .zip verwenden.");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Die ausgewählte Backup-Datei wurde nicht gefunden.", fullPath);
        return fullPath;
    }

    private static string GetGameDataDirectory(Modset modset)
    {
        return Path.Combine(Path.GetFullPath(modset.HomeBasePath), GameDefinition.For(modset.Game).HomeDirectoryName);
    }

    private sealed record ValidatedEntry(
        ZipArchiveEntry Entry,
        ModsetBackupManifestFile ManifestFile,
        string DestinationPath);
}
