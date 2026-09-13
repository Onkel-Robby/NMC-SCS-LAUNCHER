using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ModsetDuplicationService : IModsetDuplicationService
{
    private const int CopyBufferSize = 128 * 1024;
    private readonly IModsetManager _modsetManager;

    public ModsetDuplicationService(IModsetManager modsetManager)
    {
        _modsetManager = modsetManager ?? throw new ArgumentNullException(nameof(modsetManager));
    }

    public async Task<ModsetDuplicationResult> DuplicateAsync(
        ModsetDuplicationRequest request,
        IProgress<ModsetDuplicationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.SourceModsetId == Guid.Empty)
            throw new ModsetValidationException("Das Quell-Modset ist ungültig.");

        var targetName = request.Name?.Trim() ?? string.Empty;
        if (targetName.Length == 0)
            throw new ModsetValidationException("Der Name des duplizierten Modsets darf nicht leer sein.");
        if (string.IsNullOrWhiteSpace(request.TargetHomeBasePath))
            throw new ModsetValidationException("Für das duplizierte Modset ist ein Zielordner erforderlich.");

        var modsets = await _modsetManager.GetAllAsync(cancellationToken);
        var source = modsets.FirstOrDefault(item => item.Id == request.SourceModsetId)
            ?? throw new KeyNotFoundException($"Modset {request.SourceModsetId} wurde nicht gefunden.");

        if (modsets.Any(item => item.Game == source.Game && string.Equals(item.Name, targetName, StringComparison.CurrentCultureIgnoreCase)))
            throw new ModsetValidationException("Für dieses Spiel existiert bereits ein Modset mit diesem Namen.");

        var sourceHome = NormalizePath(source.HomeBasePath, "Der Quellpfad ist ungültig.");
        var targetHome = NormalizePath(request.TargetHomeBasePath, "Der Zielpfad ist ungültig.");
        EnsureSeparatedTrees(sourceHome, targetHome);
        if (Directory.Exists(targetHome) || File.Exists(targetHome))
            throw new ModsetValidationException("Der Zielordner muss neu sein und darf noch nicht existieren.");

        var targetParent = Path.GetDirectoryName(targetHome);
        if (string.IsNullOrWhiteSpace(targetParent))
            throw new ModsetValidationException("Der Zielordner kann an dieser Stelle nicht angelegt werden.");

        Directory.CreateDirectory(targetParent);
        var stagingHome = targetHome + ".nmc-dup-" + Guid.NewGuid().ToString("N");
        var warnings = new List<string>();
        var plan = BuildCopyPlan(source, sourceHome, request.Content, warnings, cancellationToken);
        var totalBytes = plan.Files.Sum(static item => item.Length);
        progress?.Report(new ModsetDuplicationProgress("Scanning", 0, totalBytes, 0, plan.Files.Count));

        Directory.CreateDirectory(stagingHome);
        foreach (var relativeDirectory in plan.Directories.OrderBy(static path => path.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(stagingHome, relativeDirectory));
        }

        long bytesCopied = 0;
        var filesCopied = 0;
        foreach (var item in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(stagingHome, item.RelativePath);
            var destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            await CopyFileAsync(item.SourcePath, destination, copied =>
            {
                bytesCopied += copied;
                progress?.Report(new ModsetDuplicationProgress(
                    "Copying", bytesCopied, totalBytes, filesCopied, plan.Files.Count, item.RelativePath));
            }, cancellationToken);

            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(item.SourcePath));
            filesCopied++;
            progress?.Report(new ModsetDuplicationProgress(
                "Copying", bytesCopied, totalBytes, filesCopied, plan.Files.Count, item.RelativePath));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.Move(stagingHome, targetHome);
        progress?.Report(new ModsetDuplicationProgress("Registering", bytesCopied, totalBytes, filesCopied, plan.Files.Count));

        var created = await _modsetManager.CreateAsync(new ModsetDraft(
            source.Game,
            targetName,
            source.Description,
            targetHome,
            source.PreferredProfile,
            source.AdditionalLaunchArguments), cancellationToken);

        progress?.Report(new ModsetDuplicationProgress("Completed", bytesCopied, totalBytes, filesCopied, plan.Files.Count));
        return new ModsetDuplicationResult(created, filesCopied, bytesCopied, warnings);
    }

    private static CopyPlan BuildCopyPlan(
        Modset source,
        string sourceHome,
        ModsetCopyContent content,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<CopyFile>();
        var gameDataDirectory = Path.Combine(sourceHome, GameDefinition.For(source.Game).HomeDirectoryName);

        if (content == ModsetCopyContent.None)
            return new CopyPlan(directories, files);
        if (!Directory.Exists(gameDataDirectory))
        {
            warnings.Add("Der SCS-Datenordner existiert noch nicht; es wurden nur die Launcher-Einstellungen dupliziert.");
            return new CopyPlan(directories, files);
        }

        directories.Add(Path.GetRelativePath(sourceHome, gameDataDirectory));
        if (content.HasFlag(ModsetCopyContent.Configuration))
            AddRootFiles(sourceHome, gameDataDirectory, IsConfigurationFile, files, warnings, cancellationToken);
        if (content.HasFlag(ModsetCopyContent.Mods))
            AddDirectoryTree(sourceHome, Path.Combine(gameDataDirectory, "mod"), directories, files, warnings, cancellationToken);
        if (content.HasFlag(ModsetCopyContent.Profiles))
        {
            AddDirectoryTree(sourceHome, Path.Combine(gameDataDirectory, "profiles"), directories, files, warnings, cancellationToken);
            AddDirectoryTree(sourceHome, Path.Combine(gameDataDirectory, "steam_profiles"), directories, files, warnings, cancellationToken);
        }
        if (content.HasFlag(ModsetCopyContent.Screenshots))
        {
            AddDirectoryTree(sourceHome, Path.Combine(gameDataDirectory, "screenshot"), directories, files, warnings, cancellationToken);
            AddDirectoryTree(sourceHome, Path.Combine(gameDataDirectory, "screenshots"), directories, files, warnings, cancellationToken);
        }
        if (content.HasFlag(ModsetCopyContent.Logs))
            AddRootFiles(sourceHome, gameDataDirectory, IsLogFile, files, warnings, cancellationToken);

        return new CopyPlan(directories, files.OrderBy(static item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static void AddRootFiles(
        string sourceHome,
        string directory,
        Func<string, bool> predicate,
        ICollection<CopyFile> files,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        string[] entries;
        try { entries = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new IOException($"'{directory}' konnte für die Duplizierung nicht gelesen werden.", ex); }

        foreach (var file in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate(file)) TryAddFile(sourceHome, file, files, warnings);
        }
    }

    private static void AddDirectoryTree(
        string sourceHome,
        string root,
        ISet<string> directories,
        ICollection<CopyFile> files,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root)) return;
        if (IsReparsePoint(root))
        {
            warnings.Add($"'{Path.GetFileName(root)}' wurde übersprungen, weil es ein Link/Junction ist.");
            return;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            directories.Add(Path.GetRelativePath(sourceHome, current));

            string[] childDirectories;
            string[] childFiles;
            try
            {
                childDirectories = Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly);
                childFiles = Directory.GetFiles(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { throw new IOException($"'{current}' konnte für die Duplizierung nicht vollständig gelesen werden.", ex); }

            foreach (var childDirectory in childDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsReparsePoint(childDirectory))
                    warnings.Add($"'{Path.GetRelativePath(sourceHome, childDirectory)}' wurde übersprungen, weil es ein Link/Junction ist.");
                else
                    pending.Push(childDirectory);
            }

            foreach (var file in childFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryAddFile(sourceHome, file, files, warnings);
            }
        }
    }

    private static void TryAddFile(string sourceHome, string file, ICollection<CopyFile> files, ICollection<string> warnings)
    {
        if (IsReparsePoint(file))
        {
            warnings.Add($"'{Path.GetRelativePath(sourceHome, file)}' wurde übersprungen, weil es ein Link ist.");
            return;
        }
        var info = new FileInfo(file);
        files.Add(new CopyFile(file, Path.GetRelativePath(sourceHome, file), info.Length));
    }

    private static bool IsConfigurationFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".cfg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".sii", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ini", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLogFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".log.txt", StringComparison.OrdinalIgnoreCase)
            || name.Equals("game.crash.txt", StringComparison.OrdinalIgnoreCase)
            || name.Equals("game.log.txt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparsePoint(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new IOException($"Die Dateiattribute von '{path}' konnten nicht gelesen werden.", ex); }
    }

    private static async Task CopyFileAsync(string source, string destination, Action<int> reportBytes, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[CopyBufferSize];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            reportBytes(read);
        }
        await output.FlushAsync(cancellationToken);
    }

    private static string NormalizePath(string path, string errorMessage)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim())); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new ModsetValidationException(errorMessage); }
    }

    private static void EnsureSeparatedTrees(string sourceHome, string targetHome)
    {
        if (IsSameOrDescendant(sourceHome, targetHome) || IsSameOrDescendant(targetHome, sourceHome))
            throw new ModsetValidationException("Quell- und Zielordner dürfen nicht identisch oder ineinander verschachtelt sein.");
    }

    private static bool IsSameOrDescendant(string parent, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(parent, candidate, comparison)) return true;
        return candidate.StartsWith(parent + Path.DirectorySeparatorChar, comparison);
    }

    private sealed record CopyFile(string SourcePath, string RelativePath, long Length);
    private sealed record CopyPlan(ISet<string> Directories, List<CopyFile> Files);
}
