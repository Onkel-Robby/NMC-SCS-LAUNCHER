using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

/// <summary>
/// Only looks at known SCS game-home folders; never searches arbitrary files
/// or changes Steam/OneDrive profile contents.
/// </summary>
public static class ScsProfileHomeLocator
{
    public static IReadOnlyList<string> GetCandidates(
        GameType gameType,
        string? preferredHome,
        IEnumerable<string> documentRoots)
    {
        ArgumentNullException.ThrowIfNull(documentRoots);
        var candidates = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                var normalized = Path.GetFullPath(path);
                if (visited.Add(normalized))
                    candidates.Add(normalized);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // A stale redirected folder must not interrupt discovery.
            }
        }

        Add(preferredHome);
        foreach (var documentsRoot in documentRoots)
        {
            if (!string.IsNullOrWhiteSpace(documentsRoot))
                Add(Path.Combine(documentsRoot, GameDefinition.For(gameType).HomeDirectoryName));
        }

        return candidates;
    }

    public static IEnumerable<string> GetDocumentRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            yield return Path.Combine(userProfile, "Documents");
            yield return Path.Combine(userProfile, "Dokumente");
        }

        // OneDrive folder redirection may differ from the Windows Documents
        // known-folder value, particularly when a backup was migrated.
        foreach (var env in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var oneDrive = Environment.GetEnvironmentVariable(env);
            if (string.IsNullOrWhiteSpace(oneDrive))
                continue;

            yield return Path.Combine(oneDrive, "Documents");
            yield return Path.Combine(oneDrive, "Dokumente");
        }
    }

    public static string NormalizeSelectedHome(string selected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selected);
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selected));
        var leaf = Path.GetFileName(folder);
        if (leaf.Equals("profiles", StringComparison.OrdinalIgnoreCase)
            || leaf.Equals("steam_profiles", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(folder) ?? folder;
        }

        return folder;
    }
}
