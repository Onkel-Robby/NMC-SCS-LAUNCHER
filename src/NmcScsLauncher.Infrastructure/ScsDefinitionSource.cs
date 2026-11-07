using System.Text;

namespace NmcScsLauncher.Infrastructure;

/// <summary>A read-only view of the actual virtual files in one SCS content source.</summary>
public abstract class ScsDefinitionSource : IDisposable
{
    internal const int MaximumTextBytes = 4 * 1024 * 1024;
    internal const int MaximumMetadataBytes = 64 * 1024 * 1024;
    internal const int MaximumFiles = 100_000;
    internal const int MaximumDepth = 64;
    private const long MaximumReadBytes = 64L * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private long _readBytes;
    protected bool IsDisposed { get; private set; }

    /// <summary>Opens a directory, ordinary ZIP, or supported HashFS archive without writing to it.</summary>
    public static ScsDefinitionSource Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = Path.GetFullPath(path);
        CheckNoReparsePoints(path);
        if (Directory.Exists(path)) return new FolderDefinitionSource(path);

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Span<byte> magic = stackalloc byte[4];
            stream.ReadExactly(magic);
            stream.Position = 0;
            if (magic.SequenceEqual("SCS#"u8)) return new HashFsDefinitionSource(stream);
            if (magic[0] == 'P' && magic[1] == 'K') return new ScsZipDefinitionSource(stream);
            throw new NotSupportedException("Das Archivformat wird nicht unterstützt.");
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public abstract IReadOnlyList<string> GetFiles(string virtualDirectory, CancellationToken cancellationToken = default);
    public abstract string ReadText(string virtualPath, CancellationToken cancellationToken = default);

    public virtual void Dispose() => IsDisposed = true;

    protected void CheckAvailable(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        token.ThrowIfCancellationRequested();
    }

    internal static string ValidatePath(string path, bool directory = false)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/' || path.Length > 2048)
            throw new InvalidDataException("Ungültiger SCS-Definitionspfad.");
        if (directory && path.Length > 1 && path.EndsWith('/')) path = path[..^1];
        if (path == "/" && directory) return path;
        foreach (var segment in path[1..].Split('/'))
        {
            ValidateSegment(segment);
        }
        return path;
    }

    internal static void ValidateSegment(string segment)
    {
        if (segment.Length == 0 || segment.Length > 255 || segment is "." or ".." ||
            segment.EndsWith('.') || segment.EndsWith(' ') ||
            segment.Any(character => char.IsControl(character) || "\\/:*?\"<>|".Contains(character)))
            throw new InvalidDataException("Unsicherer Pfad im Definitionsarchiv.");
        var stem = segment.Split('.', 2)[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
             "123456789¹²³".Contains(stem[3])))
            throw new InvalidDataException("Reservierter Windows-Gerätename im Definitionspfad.");
    }

    internal static void CheckNoReparsePoints(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Verknüpfungen und Reparse-Points werden nicht als Definitionsquelle gelesen.");
        }
    }

    protected byte[] ReadContent(Stream stream, long expectedLength, CancellationToken token, bool requireEnd = true)
    {
        if (expectedLength < 0 || expectedLength > MaximumTextBytes)
            throw new InvalidDataException("Die Definitionsdatei überschreitet die zulässige Größe.");
        if (expectedLength > MaximumReadBytes - _readBytes)
            throw new InvalidDataException("Das Leselimit der Definitionsquelle wurde erreicht.");
        _readBytes += expectedLength;
        return ReadExactBytes(stream, (int)expectedLength, token, requireEnd);
    }

    internal static byte[] ReadExactBytes(Stream stream, int size, CancellationToken token, bool requireEnd)
    {
        var bytes = new byte[size];
        var position = 0;
        while (position < size)
        {
            token.ThrowIfCancellationRequested();
            var count = stream.Read(bytes, position, Math.Min(32 * 1024, size - position));
            if (count == 0) throw new InvalidDataException("Die Definitionsquelle ist abgeschnitten.");
            position += count;
        }
        token.ThrowIfCancellationRequested();
        if (requireEnd && stream.ReadByte() != -1)
            throw new InvalidDataException("Die entpackte Länge stimmt nicht mit dem Archivindex überein.");
        return bytes;
    }

    internal static string DecodeText(byte[] bytes)
    {
        try
        {
            var text = StrictUtf8.GetString(bytes);
            return text.StartsWith('\ufeff') ? text[1..] : text;
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Die Definitionsdatei enthält ungültiges UTF-8.", exception);
        }
    }

    private sealed class FolderDefinitionSource(string root) : ScsDefinitionSource
    {
        public override IReadOnlyList<string> GetFiles(string virtualDirectory, CancellationToken cancellationToken = default)
        {
            CheckAvailable(cancellationToken);
            virtualDirectory = ValidatePath(virtualDirectory, directory: true);
            var physical = Resolve(virtualDirectory);
            if (!Directory.Exists(physical)) return [];
            var files = new List<string>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<(string Physical, string Virtual, int Depth)>();
            pending.Push((physical, virtualDirectory.TrimEnd('/'), 0));
            var visited = 0;
            while (pending.TryPop(out var current))
            {
                CheckAvailable(cancellationToken);
                CheckNoReparsePoints(current.Physical);
                if (current.Depth > MaximumDepth) throw new InvalidDataException("Der Definitionsordner ist zu tief verschachtelt.");
                foreach (var child in Directory.EnumerateFileSystemEntries(current.Physical))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++visited > MaximumFiles) throw new InvalidDataException("Zu viele Einträge im Definitionsordner.");
                    CheckNoReparsePoints(child);
                    var name = Path.GetFileName(child);
                    ValidateSegment(name);
                    var virtualPath = current.Virtual + "/" + name;
                    ValidatePath(virtualPath);
                    if (!paths.Add(virtualPath)) throw new InvalidDataException("Mehrdeutiger Definitionspfad.");
                    if ((File.GetAttributes(child) & FileAttributes.Directory) != 0)
                        pending.Push((child, virtualPath, current.Depth + 1));
                    else
                        files.Add(virtualPath);
                }
            }
            files.Sort(StringComparer.Ordinal);
            return files;
        }

        public override string ReadText(string virtualPath, CancellationToken cancellationToken = default)
        {
            CheckAvailable(cancellationToken);
            virtualPath = ValidatePath(virtualPath);
            var physical = Resolve(virtualPath);
            using var file = new FileStream(physical, FileMode.Open, FileAccess.Read, FileShare.Read);
            CheckNoReparsePoints(physical);
            var content = ReadContent(file, file.Length, cancellationToken);
            CheckNoReparsePoints(physical);
            return DecodeText(content);
        }

        private string Resolve(string virtualPath)
        {
            CheckNoReparsePoints(root);
            var current = root;
            foreach (var segment in virtualPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (File.Exists(current) || Directory.Exists(current)) CheckNoReparsePoints(current);
            }
            return current;
        }
    }

}
