using System.Security.Cryptography;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ScsTruckPowertrainService(
    ScsTruckPowertrainSnapshotReader reader, IScsEffectiveContentService effectiveContent) : IScsTruckPowertrainService
{
    public async Task<IScsTruckPowertrainPreview> InspectAsync(ScsEffectiveContentRequest request,
        CancellationToken cancellationToken = default)
    {
        var read = await reader.ReadAsync(request.Save, cancellationToken).ConfigureAwait(false);
        try
        {
            using var session = await effectiveContent.OpenAsync(request, cancellationToken).ConfigureAwait(false);
            var preview = await Task.Run(() => ScsTruckPowertrainRules.Build(read.Snapshot, session, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (session.Evidence.ConfigurationVerified)
                await session.VerifyUnchangedAsync(cancellationToken).ConfigureAwait(false);
            await VerifySaveUnchangedAsync(read.Snapshot, cancellationToken).ConfigureAwait(false);
            return preview;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ScsSaveEditException)
        {
            using var unavailable = new UnavailableSession(read.Snapshot.Game, exception.Message);
            return ScsTruckPowertrainRules.Build(read.Snapshot, unavailable, cancellationToken);
        }
    }

    // D3 reacquires this context immediately before a transaction. Detached UI previews never authorize writes.
    internal async Task<Verification> OpenVerificationAsync(ScsEffectiveContentRequest request,
        CancellationToken cancellationToken = default)
    {
        var read = await reader.ReadAsync(request.Save, cancellationToken).ConfigureAwait(false);
        var session = await effectiveContent.OpenAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            var preview = await Task.Run(() => ScsTruckPowertrainRules.Build(read.Snapshot, session, cancellationToken), cancellationToken).ConfigureAwait(false);
            var verification = new Verification(read, session, preview);
            await verification.VerifyUnchangedAsync(cancellationToken).ConfigureAwait(false);
            return verification;
        }
        catch { session.Dispose(); throw; }
    }

    internal sealed class Verification(ScsTruckPowertrainReadResult read, IScsEffectiveContentSession session,
        IScsTruckPowertrainPreview preview) : IDisposable
    {
        private bool _disposed;
        public IScsTruckPowertrainPreview Preview { get; } = preview;
        public ScsTruckPowertrainSnapshot Snapshot => read.Snapshot;
        public ScsTruckPowertrainAssessment AssessPair(string enginePath, string transmissionPath)
        { ObjectDisposedException.ThrowIf(_disposed, this); return Preview.AssessPair(enginePath, transmissionPath); }
        public async Task VerifyUnchangedAsync(CancellationToken cancellationToken = default)
        {
            await VerifySourcesUnchangedAsync(cancellationToken).ConfigureAwait(false);
            await VerifySaveUnchangedAsync(Snapshot, cancellationToken).ConfigureAwait(false);
        }
        public Task VerifySourcesUnchangedAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return session.VerifyUnchangedAsync(cancellationToken);
        }
        public string BuildUpdatedContent(string enginePath, string transmissionPath)
        {
            var assessment = AssessPair(enginePath, transmissionPath);
            if (!assessment.IsVerified) throw new ScsSaveEditException(string.Join(Environment.NewLine, assessment.Reasons));
            return ScsTruckPowertrainSnapshotReader.BuildUpdatedContent(read, enginePath, transmissionPath);
        }
        public void Dispose() { if (_disposed) return; _disposed = true; session.Dispose(); }
    }

    private static async Task VerifySaveUnchangedAsync(ScsTruckPowertrainSnapshot snapshot, CancellationToken cancellationToken)
    {
        ScsDefinitionSource.CheckNoReparsePoints(snapshot.SaveFilePath);
        await using var stream = new FileStream(snapshot.SaveFilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > 128 * 1024 * 1024) throw new ScsSaveEditException("Die Save-Größe hat sich geändert oder überschreitet das Prüflimit.");
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(hash, snapshot.SaveSha256, StringComparison.OrdinalIgnoreCase))
            throw new ScsSaveEditException("Der ausgewählte Save hat sich während der Truck-Prüfung geändert. Bitte neu laden.");
    }

    private sealed class UnavailableSession(GameType game, string reason) : IScsEffectiveContentSession
    {
        public ScsActiveContentEvidence Evidence { get; } = new(game, string.Empty, false, [], [], [], [reason]);
        public IReadOnlyList<string> GetFiles(string virtualDirectory, CancellationToken cancellationToken = default) => [];
        public ScsResolvedDefinition ReadDefinition(string virtualPath, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
