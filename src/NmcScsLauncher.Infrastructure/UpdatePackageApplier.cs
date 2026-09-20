using System.IO.Compression;

namespace NmcScsLauncher.Infrastructure;

public sealed record UpdateApplyResult(
    string TargetDirectory,
    string BackupDirectory,
    string RestartExecutablePath);

public sealed class UpdatePackageApplier
{
    public async Task<UpdateApplyResult> ApplyAsync(
        string packagePath,
        string targetDirectory,
        string restartExecutableRelativePath,
        CancellationToken cancellationToken = default)
    {
        var package = ValidatePackagePath(packagePath);
        var target = ValidateTargetDirectory(targetDirectory);
        var restartRelative = ValidateRestartRelativePath(restartExecutableRelativePath);
        var parent = Directory.GetParent(target)?.FullName
            ?? throw new InvalidOperationException("Update target must have a parent directory.");

        var token = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(parent, $".nmc-update-stage-{token}");
        var backup = Path.Combine(parent, $".nmc-update-backup-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{token}");

        Directory.CreateDirectory(staging);
        try
        {
            await ExtractPackageAsync(package, staging, cancellationToken);
            PreserveInstallerUninstallerFiles(target, staging);
            var stagedRestart = Path.GetFullPath(Path.Combine(staging, restartRelative));
            EnsureWithinRoot(staging, stagedRestart);
            if (!File.Exists(stagedRestart))
                throw new InvalidDataException($"Update package does not contain '{restartRelative}'.");

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(target, backup);
            try
            {
                Directory.Move(staging, target);
            }
            catch
            {
                if (!Directory.Exists(target) && Directory.Exists(backup))
                    Directory.Move(backup, target);
                throw;
            }

            var restartPath = Path.GetFullPath(Path.Combine(target, restartRelative));
            EnsureWithinRoot(target, restartPath);
            return new UpdateApplyResult(target, backup, restartPath);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    public Task RollbackAsync(UpdateApplyResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        var target = Path.GetFullPath(result.TargetDirectory);
        var backup = Path.GetFullPath(result.BackupDirectory);
        if (!Directory.Exists(backup))
            throw new DirectoryNotFoundException("Update backup directory does not exist.");

        var failedDirectory = target + ".failed-" + Guid.NewGuid().ToString("N");
        if (Directory.Exists(target))
            Directory.Move(target, failedDirectory);

        try
        {
            Directory.Move(backup, target);
            TryDeleteDirectory(failedDirectory);
        }
        catch
        {
            if (!Directory.Exists(target) && Directory.Exists(failedDirectory))
                Directory.Move(failedDirectory, target);
            throw;
        }

        return Task.CompletedTask;
    }

    private static async Task ExtractPackageAsync(
        string packagePath,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            packagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count == 0)
            throw new InvalidDataException("Update package is empty.");

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.GetFullPath(Path.Combine(stagingDirectory, entry.FullName));
            EnsureWithinRoot(stagingDirectory, destination);

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await using var source = entry.Open();
            await using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
    }

    private static void PreserveInstallerUninstallerFiles(string sourceDirectory, string stagingDirectory)
    {
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "unins*.*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(sourcePath);
            if (!IsInstallerUninstallerFile(fileName)) continue;

            var destinationPath = Path.Combine(stagingDirectory, fileName);
            if (File.Exists(destinationPath))
                throw new InvalidDataException(
                    $"Update package must not replace installer-owned file '{fileName}'.");

            File.Copy(sourcePath, destinationPath, overwrite: false);
        }
    }

    private static bool IsInstallerUninstallerFile(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (extension is not ".exe" and not ".dat" and not ".msg")
            return false;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (!stem.StartsWith("unins", StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = stem[5..];
        return suffix.Length == 3 && suffix.All(char.IsDigit);
    }

    private static string ValidatePackagePath(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Update package path is required.", nameof(packagePath));
        var fullPath = Path.GetFullPath(packagePath.Trim());
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Update package does not exist.", fullPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only ZIP update packages are supported.");
        return fullPath;
    }

    private static string ValidateTargetDirectory(string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
            throw new ArgumentException("Update target directory is required.", nameof(targetDirectory));
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDirectory.Trim()));
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException("Update target directory does not exist.");
        if (Path.GetPathRoot(fullPath)?.Equals(fullPath, StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("A filesystem root cannot be used as update target.");
        return fullPath;
    }

    private static string ValidateRestartRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Restart executable is required.", nameof(value));
        if (Path.IsPathRooted(value))
            throw new ArgumentException("Restart executable must be relative to the application directory.", nameof(value));
        var normalized = value.Replace('/', Path.DirectorySeparatorChar).Trim();
        if (normalized.Split(Path.DirectorySeparatorChar).Any(part => part is ".." or "."))
            throw new ArgumentException("Restart executable path contains traversal segments.", nameof(value));
        return normalized;
    }

    private static void EnsureWithinRoot(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        if (!normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update package contains an entry outside the staging directory.");
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Cleanup failure must not hide the primary update result.
            }
        }
    }
}
