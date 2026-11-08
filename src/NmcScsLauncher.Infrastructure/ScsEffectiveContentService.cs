using System.Security.Cryptography;
using System.Text;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

/// <summary>Resolves actual virtual files against a bounded, read-only content snapshot.</summary>
public sealed class ScsEffectiveContentService(IScsActiveContentResolver resolver) : IScsEffectiveContentService
{
    public async Task<IScsEffectiveContentSession> OpenAsync(ScsEffectiveContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        request = request with { ModDirectories = request.ModDirectories?.ToArray() };
        var evidence = await resolver.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
        var session = new Session(evidence, request, resolver);
        try
        {
            await Task.Run(() => session.Initialize(cancellationToken), cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ScsSaveEditException)
        {
            session.Dispose();
            var issue = exception is ScsPowertrainDiagnosticException contextual ? contextual.Diagnostic :
                new ScsPowertrainDiagnostic("source_snapshot_invalid", ScsPowertrainDiagnosticStage.ActiveConfiguration,
                    ScsPowertrainDiagnosticScope.Global, exception.Message, BlocksConfiguration: true);
            throw new ScsPowertrainDiagnosticException(issue, exception, evidence);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private sealed class Session : IScsEffectiveContentSession
    {
        private const int MaximumLayers = 512;
        private const int MaximumEvidenceFiles = 4096;
        private const int MaximumQueries = 4096;
        private const int MaximumDefinitions = 16_384;
        private const int MaximumListedFiles = 500_000;
        private const long MaximumTextBytes = 64L * 1024 * 1024;
        private const long MaximumEvidenceFileBytes = 256L * 1024 * 1024;
        private const long MaximumEvidenceHashBytes = 512L * 1024 * 1024;
        private readonly object _gate = new();
        private readonly ScsContentLayer[] _layers;
        private readonly ScsEffectiveContentRequest _request;
        private readonly IScsActiveContentResolver _resolver;
        private readonly Dictionary<string, Source> _sources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Listing> _listings = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ScsResolvedDefinition> _definitions = new(StringComparer.Ordinal);
        private long _textBytes;
        private long _listedFiles;
        private bool _disposed;

        public ScsActiveContentEvidence Evidence { get; }

        public Session(ScsActiveContentEvidence evidence, ScsEffectiveContentRequest request, IScsActiveContentResolver resolver)
        {
            if (evidence.Layers.Count > MaximumLayers || evidence.Files.Count > MaximumEvidenceFiles)
                throw new ScsSaveEditException("Zu viele Quellen oder Nachweisdateien für die effektive Dateisicht.");
            // Retain immutable copies: changing a caller-owned list must not change the snapshot.
            Evidence = evidence with
            {
                ActiveMods = Array.AsReadOnly(evidence.ActiveMods.ToArray()),
                Layers = Array.AsReadOnly(evidence.Layers.ToArray()),
                Files = Array.AsReadOnly(evidence.Files.ToArray()),
                Diagnostics = Array.AsReadOnly(evidence.Diagnostics.ToArray()),
                DiagnosticDetails = Array.AsReadOnly((evidence.DiagnosticDetails ?? []).ToArray()),
                SaveDependencies = evidence.SaveDependencies is null ? null : Array.AsReadOnly(evidence.SaveDependencies.ToArray())
            };
            _layers = Evidence.Layers.OrderByDescending(layer => layer.Priority).ToArray();
            _request = request;
            _resolver = resolver;
            foreach (var layer in _layers)
            {
                _ = MountRoot(layer);
                _ = Path.GetFullPath(layer.PhysicalPath);
            }
        }

        public void Initialize(CancellationToken token)
        {
            if (!Evidence.ConfigurationVerified) return;
            GuardSourceFailures(() =>
            {
                VerifyEvidence(token);
                return true;
            });
        }

        public IReadOnlyList<string> GetFiles(string virtualDirectory, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                CheckAvailable(cancellationToken);
                virtualDirectory = ScsDefinitionSource.ValidatePath(virtualDirectory, directory: true);
                if (_listings.TryGetValue(virtualDirectory, out var cached)) return cached.Files;
                if (_listings.Count >= MaximumQueries) throw new ScsSaveEditException("Zu viele Verzeichnisabfragen im Inhaltssnapshot.");
                var listing = GuardSourceFailures(() => ReadListing(virtualDirectory, cancellationToken));
                _listedFiles += listing.PerLayer.Sum(files => (long)files.Length);
                if (_listedFiles > MaximumListedFiles) throw new ScsSaveEditException("Das Dateilimit des Inhaltssnapshots wurde erreicht.");
                _listings.Add(virtualDirectory, listing);
                return listing.Files;
            }
        }

        public ScsResolvedDefinition ReadDefinition(string virtualPath, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                CheckAvailable(cancellationToken);
                virtualPath = ScsDefinitionSource.ValidatePath(virtualPath);
                if (_definitions.TryGetValue(virtualPath, out var cached)) return cached;
                if (_definitions.Count >= MaximumDefinitions) throw new ScsSaveEditException("Zu viele gelesene Definitionen im Inhaltssnapshot.");
                var resolved = GuardSourceFailures(() => Resolve(virtualPath, cancellationToken), allowMissing: true);
                _textBytes += Encoding.UTF8.GetByteCount(resolved.Content);
                if (_textBytes > MaximumTextBytes) throw new ScsSaveEditException("Das Textlimit des Inhaltssnapshots wurde erreicht.");
                _definitions.Add(virtualPath, resolved);
                return resolved;
            }
        }

        public async Task VerifyUnchangedAsync(CancellationToken cancellationToken = default)
        {
            lock (_gate) CheckAvailable(cancellationToken);
            // Inventory the configured roots again. A new ambiguous package can invalidate the
            // selection without modifying any already-open source or its existing metadata.
            var currentEvidence = await _resolver.ResolveAsync(_request, cancellationToken).ConfigureAwait(false);
            await Task.Run(() =>
            {
                lock (_gate)
                {
                    CheckAvailable(cancellationToken);
                    GuardSourceFailures(() =>
                    {
                        if (!currentEvidence.ConfigurationVerified || currentEvidence.Game != Evidence.Game ||
                            currentEvidence.GameVersion != Evidence.GameVersion ||
                            !currentEvidence.ActiveMods.SequenceEqual(Evidence.ActiveMods) ||
                            !currentEvidence.Layers.SequenceEqual(Evidence.Layers) ||
                            !currentEvidence.Files.SequenceEqual(Evidence.Files))
                            throw Changed("Die Auswahl der aktiven Pakete oder ihre Nachweise haben sich geändert.");
                        VerifyEvidence(cancellationToken);
                        foreach (var source in _sources.Values) source.VerifyPhysicalIdentity();
                        foreach (var query in _listings)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var current = ReadListing(query.Key, cancellationToken);
                            for (var index = 0; index < current.PerLayer.Length; index++)
                                if (!current.PerLayer[index].SequenceEqual(query.Value.PerLayer[index], StringComparer.Ordinal))
                                    throw Changed("Die Dateien oder überlagernden Quellen eines abgefragten Verzeichnisses haben sich geändert.");
                        }
                        foreach (var definition in _definitions)
                        {
                            var current = Resolve(definition.Key, cancellationToken);
                            if (current.ContentSha256 != definition.Value.ContentSha256 || current.Source != definition.Value.Source ||
                                current.AvailabilityVerified != definition.Value.AvailabilityVerified)
                                throw Changed("Eine gelesene Definition oder ihre wirksame Quelle hat sich geändert.");
                        }
                        // Recheck after reading so evidence mutation during verification is rejected.
                        VerifyEvidence(cancellationToken);
                        return true;
                    });
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        private Listing ReadListing(string directory, CancellationToken token)
        {
            var union = new SortedSet<string>(StringComparer.Ordinal);
            var perLayer = new string[_layers.Length][];
            long count = 0;
            for (var index = 0; index < _layers.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var layer = _layers[index];
                var source = GetSource(layer.PhysicalPath);
                var mountRoot = MountRoot(layer);
                IReadOnlyList<string> paths;
                try { paths = source.Reader.GetFiles(MountPath(layer, directory), token); }
                catch (Exception exception) when (IsSourceFailure(exception))
                { throw SourceFailure("source_listing_failed", layer, directory, exception); }
                count += paths.Count;
                if (count > MaximumListedFiles) throw new ScsSaveEditException("Zu viele Dateien in der effektiven Dateisicht.");
                var mapped = new string[paths.Count];
                for (var file = 0; file < paths.Count; file++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!paths[file].StartsWith(mountRoot + "/", StringComparison.Ordinal))
                        throw new InvalidDataException("Eine Quelle lieferte einen Pfad außerhalb ihres Mount-Verzeichnisses.");
                    mapped[file] = ScsDefinitionSource.ValidatePath(paths[file][mountRoot.Length..]);
                    union.Add(mapped[file]);
                }
                Array.Sort(mapped, StringComparer.Ordinal);
                perLayer[index] = mapped;
            }
            return new(Array.AsReadOnly(union.ToArray()), perLayer);
        }

        private ScsResolvedDefinition Resolve(string virtualPath, CancellationToken token)
        {
            ScsContentLayer? selectedLayer = null;
            string? selectedContent = null;
            foreach (var layer in _layers)
            {
                token.ThrowIfCancellationRequested();
                if (selectedLayer is not null && layer.Priority < selectedLayer.Priority) break;
                var source = GetSource(layer.PhysicalPath);
                var path = MountPath(layer, virtualPath);
                if (source.IsFolder && source.IsMissingFolderFile(path)) continue;
                string content;
                try { content = source.Reader.ReadText(path, token); }
                catch (FileNotFoundException)
                {
                    // Only a missing virtual file is an absent layer. Missing sources and all
                    // unsupported/corrupt entries remain failures, including unknown DLC layers.
                    source.VerifyPhysicalIdentity();
                    continue;
                }
                catch (Exception exception) when (IsSourceFailure(exception))
                { throw SourceFailure("definition_read_failed", layer, virtualPath, exception); }
                if (selectedLayer is not null)
                {
                    if (!string.Equals(selectedContent, content, StringComparison.Ordinal))
                        throw new ScsPowertrainDiagnosticException(new("definition_priority_ambiguous",
                            ScsPowertrainDiagnosticStage.EffectiveDefinition, ScsPowertrainDiagnosticScope.Component,
                            $"Mehrdeutige Definition '{virtualPath}': unterschiedliche Inhalte bei gleicher Priorität.",
                            layer.PackageIdentity, layer.PackageIdentity, layer.PhysicalPath, layer.PackageVersion, virtualPath,
                            $"Priorität {layer.Priority}: {selectedLayer.PackageIdentity} / {layer.PackageIdentity}"));
                    // A known identical file establishes the same result even when an optional
                    // equal-priority layer is absent. All candidates are rechecked before use.
                    if (!selectedLayer.AvailabilityVerified && layer.AvailabilityVerified) selectedLayer = layer;
                }
                else
                {
                    selectedLayer = layer;
                    selectedContent = content;
                }
            }
            if (selectedLayer is null) throw new FileNotFoundException("Definition in den aktiven Quellen nicht gefunden.", virtualPath);
            return new(virtualPath, selectedContent!, selectedLayer, HashText(selectedContent!),
                selectedLayer.AvailabilityVerified, selectedLayer.AvailabilityReason);
        }

        private void VerifyEvidence(CancellationToken token)
        {
            long hashedBytes = 0;
            foreach (var file in Evidence.Files)
            {
                token.ThrowIfCancellationRequested();
                var path = Path.GetFullPath(file.Path);
                ScsDefinitionSource.CheckNoReparsePoints(path);
                var current = new FileInfo(path);
                if (file.Length < 0 || !current.Exists || current.Length != file.Length || current.LastWriteTimeUtc != file.LastWriteTimeUtc.UtcDateTime)
                    throw Changed("Eine Nachweisdatei hat sich seit der Ermittlung der aktiven Inhalte geändert.");
                if (file.Sha256.Length == 0)
                {
                    if (file.VirtualPath is not null) throw new InvalidDataException("Virtueller Inhaltsnachweis ohne Prüfsumme.");
                    continue;
                }
                if (file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Ungültige SHA-256-Prüfsumme im Inhaltsnachweis.");
                string hash;
                if (file.VirtualPath is not null)
                {
                    var text = GetSource(path).Reader.ReadText(ScsDefinitionSource.ValidatePath(file.VirtualPath), token);
                    hashedBytes += Encoding.UTF8.GetByteCount(text);
                    hash = HashText(text);
                }
                else
                {
                    hashedBytes += current.Length;
                    if (hashedBytes > MaximumEvidenceHashBytes) throw new InvalidDataException("Nachweisdateien überschreiten das Hash-Limit.");
                    // Mutable profile, save-info and manifest files must remain replaceable
                    // while a read-only UI session is open. Reopen their current path each time.
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    hash = HashStream(stream, token);
                }
                if (hashedBytes > MaximumEvidenceHashBytes) throw new InvalidDataException("Nachweisdateien überschreiten das Hash-Limit.");
                if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw Changed("Der Inhalt einer Nachweisdatei hat sich seit der Ermittlung der aktiven Inhalte geändert.");
            }
        }

        private Source GetSource(string path)
        {
            path = Path.GetFullPath(path);
            if (_sources.TryGetValue(path, out var source)) return source;
            if (_sources.Count >= MaximumLayers + MaximumEvidenceFiles)
                throw new InvalidDataException("Zu viele geöffnete Definitionsquellen.");
            try { source = new Source(path); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
            {
                var layer = _layers.FirstOrDefault(candidate => string.Equals(Path.GetFullPath(candidate.PhysicalPath), path, StringComparison.OrdinalIgnoreCase));
                throw new ScsPowertrainDiagnosticException(new("source_unreadable", ScsPowertrainDiagnosticStage.ArchiveDiscovery,
                    ScsPowertrainDiagnosticScope.Source, "Eine potenziell wirksame Inhaltsquelle ist nicht lesbar: " +
                    (layer?.PackageIdentity ?? Path.GetFileName(path)), layer?.PackageIdentity, layer?.PackageIdentity,
                    path, layer?.PackageVersion, TechnicalDetails: exception.Message, BlocksConfiguration: true), exception);
            }
            _sources.Add(path, source);
            return source;
        }

        private static bool IsSourceFailure(Exception exception) =>
            exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException;

        private static ScsPowertrainDiagnosticException SourceFailure(string code, ScsContentLayer layer, string virtualPath, Exception exception) =>
            new(new(code, ScsPowertrainDiagnosticStage.EffectiveDefinition, ScsPowertrainDiagnosticScope.Component,
                $"{layer.PackageIdentity}: {virtualPath} konnte nicht sicher gelesen werden. {exception.Message}",
                layer.PackageIdentity, layer.PackageIdentity, layer.PhysicalPath, layer.PackageVersion, virtualPath), exception);

        private static string MountPath(ScsContentLayer layer, string virtualPath)
        {
            var root = MountRoot(layer);
            return root + virtualPath;
        }

        private static string MountRoot(ScsContentLayer layer) =>
            ScsDefinitionSource.ValidatePath(layer.VirtualRoot.Length == 0 ? "/" : layer.VirtualRoot, directory: true).TrimEnd('/');

        private void CheckAvailable(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            if (!Evidence.ConfigurationVerified)
                throw new ScsSaveEditException("Die aktive Inhaltskonfiguration ist nicht verifiziert. " + string.Join(" ", Evidence.Diagnostics));
        }

        private static T GuardSourceFailures<T>(Func<T> action, bool allowMissing = false)
        {
            try { return action(); }
            catch (FileNotFoundException) when (allowMissing) { throw; }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
            {
                throw new ScsSaveEditException("Die effektiven Inhalte konnten nicht sicher gelesen oder erneut geprüft werden: " + exception.Message, exception);
            }
        }

        private static ScsSaveEditException Changed(string reason) => new(reason + " Bitte die Inhalte neu einlesen.");
        private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        private static string HashStream(FileStream stream, CancellationToken token)
        {
            if (stream.Length > MaximumEvidenceFileBytes) throw new InvalidDataException("Nachweisdatei überschreitet das Hash-Limit.");
            stream.Position = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long read = 0;
            int size;
            while ((size = stream.Read(buffer)) != 0)
            {
                token.ThrowIfCancellationRequested();
                read += size;
                if (read > MaximumEvidenceFileBytes) throw new InvalidDataException("Nachweisdatei überschreitet das Hash-Limit.");
                hash.AppendData(buffer, 0, size);
            }
            token.ThrowIfCancellationRequested();
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var source in _sources.Values) source.Reader.Dispose();
                _sources.Clear();
                _definitions.Clear();
                _listings.Clear();
            }
        }

        private sealed record Listing(IReadOnlyList<string> Files, string[][] PerLayer);

        private sealed class Source
        {
            private readonly string _path;
            private readonly long _length;
            private readonly DateTime _lastWriteTime;
            public ScsDefinitionSource Reader { get; }
            public bool IsFolder { get; }

            public Source(string path)
            {
                _path = path;
                ScsDefinitionSource.CheckNoReparsePoints(path);
                IsFolder = Directory.Exists(path);
                if (!IsFolder)
                {
                    var file = new FileInfo(path);
                    _length = file.Length;
                    _lastWriteTime = file.LastWriteTimeUtc;
                }
                Reader = ScsDefinitionSource.Open(path);
                try { VerifyPhysicalIdentity(); }
                catch { Reader.Dispose(); throw; }
            }

            public void VerifyPhysicalIdentity()
            {
                ScsDefinitionSource.CheckNoReparsePoints(_path);
                if (IsFolder)
                {
                    if (!Directory.Exists(_path)) throw Changed("Ein Quellordner ist nicht mehr verfügbar.");
                }
                else
                {
                    var current = new FileInfo(_path);
                    if (!current.Exists || current.Length != _length || current.LastWriteTimeUtc != _lastWriteTime)
                        throw Changed("Ein geöffnetes Quellarchiv wurde geändert.");
                }
            }

            public bool IsMissingFolderFile(string virtualPath)
            {
                VerifyPhysicalIdentity();
                var current = _path;
                foreach (var segment in virtualPath.TrimStart('/').Split('/'))
                {
                    current = Path.Combine(current, segment);
                    // Check attributes directly: File.Exists would conceal access failures and
                    // could classify an unreadable higher-priority file as absent.
                    try { ScsDefinitionSource.CheckNoReparsePoints(current); }
                    catch (FileNotFoundException) { VerifyPhysicalIdentity(); return true; }
                    catch (DirectoryNotFoundException) { VerifyPhysicalIdentity(); return true; }
                }
                return false;
            }
        }
    }
}
