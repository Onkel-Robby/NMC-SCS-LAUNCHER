namespace NmcScsLauncher.Core;

public sealed record ScsEffectiveContentRequest(
    ScsProfileReference Profile,
    ScsSaveReference Save,
    string? PreferredInstallPath,
    IReadOnlyList<ScsPowertrainModDirectory>? ModDirectories = null);

public sealed record ScsActiveMod(string Identity, string DisplayName, int LoadOrder);

/// <summary>Higher known priorities overlay lower ones. Equal priorities require identical content.</summary>
public sealed record ScsContentLayer(
    string PhysicalPath,
    string VirtualRoot,
    ScsPowertrainSourceKind Kind,
    string PackageIdentity,
    string PackageVersion,
    int Priority,
    bool AvailabilityVerified,
    string AvailabilityReason);

/// <summary>VirtualPath identifies archive text hashed as UTF-8; otherwise Sha256 hashes physical bytes.</summary>
public sealed record ScsEvidenceFile(string Path, long Length, DateTimeOffset LastWriteTimeUtc, string Sha256,
    string? VirtualPath = null);

public sealed record ScsActiveContentEvidence(
    GameType Game,
    string GameVersion,
    bool ConfigurationVerified,
    IReadOnlyList<ScsActiveMod> ActiveMods,
    IReadOnlyList<ScsContentLayer> Layers,
    IReadOnlyList<ScsEvidenceFile> Files,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<ScsPowertrainDiagnostic>? DiagnosticDetails = null);

public sealed record ScsResolvedDefinition(
    string VirtualPath,
    string Content,
    ScsContentLayer Source,
    string ContentSha256,
    bool AvailabilityVerified,
    string AvailabilityReason);

public interface IScsActiveContentResolver
{
    Task<ScsActiveContentEvidence> ResolveAsync(ScsEffectiveContentRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>A read-only snapshot. Callers must dispose it and revalidate before a save transaction.</summary>
public interface IScsEffectiveContentSession : IDisposable
{
    ScsActiveContentEvidence Evidence { get; }
    IReadOnlyList<string> GetFiles(string virtualDirectory, CancellationToken cancellationToken = default);
    ScsResolvedDefinition ReadDefinition(string virtualPath, CancellationToken cancellationToken = default);
    Task VerifyUnchangedAsync(CancellationToken cancellationToken = default);
}

public interface IScsEffectiveContentService
{
    Task<IScsEffectiveContentSession> OpenAsync(ScsEffectiveContentRequest request,
        CancellationToken cancellationToken = default);
}
