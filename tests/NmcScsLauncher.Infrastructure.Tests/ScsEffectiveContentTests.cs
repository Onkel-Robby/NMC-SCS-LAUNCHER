using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsEffectiveContentTests : IDisposable
{
    private const string Definition = "/def/vehicle/truck/model/engine/test.sii";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NmcEffectiveContent", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task HigherPriorityWinsAndIncludesUseTheSameOverlay()
    {
        var lower = Folder("base", (Definition, "base"), ("/def/shared.sui", "base include"));
        var upper = Folder("mod", (Definition, "mod"), ("/def/shared.sui", "mod include"));
        using var session = await Open(Layer(lower), Layer(upper, 1000));
        Assert.Equal([Definition], session.GetFiles("/def/vehicle/truck"));
        Assert.Equal("mod", session.ReadDefinition(Definition).Content);
        Assert.Equal("mod include", session.ReadDefinition("/def/shared.sui").Content);
        Assert.True(session.ReadDefinition(Definition).AvailabilityVerified);
        await session.VerifyUnchangedAsync();
    }

    [Fact]
    public async Task UnknownHigherLayerPreventsVerificationButKnownHigherCanOverrideIt()
    {
        var lower = Layer(Folder("base", (Definition, "base")));
        var unknown = Layer(Folder("dlc", (Definition, "possible")), 100, false);
        using (var session = await Open(lower, unknown))
        {
            var result = session.ReadDefinition(Definition);
            Assert.False(result.AvailabilityVerified);
            Assert.Contains("unknown", result.AvailabilityReason);
        }
        using var verified = await Open(lower, unknown, Layer(Folder("mod", (Definition, "certain")), 1000));
        Assert.Equal("certain", verified.ReadDefinition(Definition).Content);
        Assert.True(verified.ReadDefinition(Definition).AvailabilityVerified);
    }

    [Fact]
    public async Task EqualPriorityRequiresIdenticalContent()
    {
        var first = Layer(Folder("one", (Definition, "same")));
        var second = Layer(Folder("two", (Definition, "same")));
        using (var identical = await Open(first, second))
            Assert.True(identical.ReadDefinition(Definition).AvailabilityVerified);
        File.WriteAllText(Physical(second.PhysicalPath, Definition), "different");
        using var conflicting = await Open(first, second);
        var error = Assert.Throws<ScsPowertrainDiagnosticException>(() => conflicting.ReadDefinition(Definition));
        Assert.Equal("definition_priority_ambiguous", error.Diagnostic.Code);
        Assert.Equal(Definition, error.Diagnostic.VirtualPath);
        Assert.Equal(second.PhysicalPath, error.Diagnostic.PhysicalPath);
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(0, true, true)]
    [InlineData(100, false, false)]
    [InlineData(100, true, false)]
    public async Task IdenticalUnknownDlcCannotBorrowAvailabilityFromLowerPriority(int priority, bool dlcFirst, bool expectedVerified)
    {
        var known = Layer(Folder("base", (Definition, "identical definition")));
        var uncertain = Layer(Folder("dlc", (Definition, "identical definition")), priority, false) with
        {
            Kind = ScsPowertrainSourceKind.Dlc, DlcMetadataVerified = true, DlcSteamAppId = 2720080
        };
        using var session = await Open(dlcFirst ? [uncertain, known] : [known, uncertain]);
        var resolved = session.ReadDefinition(Definition);
        Assert.Equal(expectedVerified, resolved.AvailabilityVerified);
        Assert.Equal(expectedVerified ? known : uncertain, resolved.Source);
        await session.VerifyUnchangedAsync();
    }

    [Fact]
    public async Task ChangedDlcIdentityEvidenceInvalidatesAnOpenSnapshot()
    {
        var layer = Layer(Folder("dlc", (Definition, "definition")), 100, false) with
        {
            Kind = ScsPowertrainSourceKind.Dlc, DlcMetadataVerified = true, DlcSteamAppId = 2720080
        };
        var resolver = new MutableResolver(Evidence(layer));
        using var session = await new ScsEffectiveContentService(resolver).OpenAsync(Request());
        Assert.False(session.ReadDefinition(Definition).AvailabilityVerified);
        resolver.Evidence = Evidence(layer with { DlcSteamAppId = 1056760 });
        await Assert.ThrowsAsync<ScsSaveEditException>(() => session.VerifyUnchangedAsync());
    }

    [Fact]
    public async Task MountRootStripsOnlyItsOwnPrefix()
    {
        var path = Folder("wrapper", ("/base" + Definition, "base"), ("/dlc_test" + Definition, "possible"), ("/base_extra/wrong.sii", "wrong"));
        using var session = await Open(Layer(path) with { VirtualRoot = "/base" });
        Assert.Equal([Definition], session.GetFiles("/"));
        Assert.Equal("base", session.ReadDefinition(Definition).Content);
        Assert.Throws<FileNotFoundException>(() => session.ReadDefinition("/base_extra/wrong.sii"));
    }

    [Fact]
    public async Task EmptyMountRootFromResolverMeansArchiveRoot()
    {
        using var session = await Open(Layer(Folder("base", (Definition, "content"))) with { VirtualRoot = string.Empty });
        Assert.Equal([Definition], session.GetFiles("/def"));
        Assert.Equal("content", session.ReadDefinition(Definition).Content);
        await session.VerifyUnchangedAsync();
    }

    [Fact]
    public async Task SaveTargetRemainsReplaceableAndMetadataContentChangesAreDetected()
    {
        var path = Folder("base", (Definition, "content"));
        var save = Request().Save.GameSiiPath;
        File.WriteAllText(save, "before");
        var evidence = Evidence(Layer(path)) with { Files = [Fingerprint(save)] };
        using var session = await Service(evidence).OpenAsync(Request());
        var time = File.GetLastWriteTimeUtc(save);
        File.WriteAllText(save, "after!");
        File.SetLastWriteTimeUtc(save, time);
        await Assert.ThrowsAsync<ScsSaveEditException>(() => session.VerifyUnchangedAsync());
    }

    [Theory]
    [InlineData("profile.sii")]
    [InlineData("info.sii")]
    public async Task MutableMetadataRemainsWritableAndSameSizeTimeChangesInvalidateSnapshot(string name)
    {
        var path = Folder("base", (Definition, "content"));
        var metadata = Path.Combine(_root, name);
        File.WriteAllText(metadata, "before");
        using var session = await Service(Evidence(Layer(path)) with { Files = [Fingerprint(metadata)] }).OpenAsync(Request());
        var time = File.GetLastWriteTimeUtc(metadata);
        File.WriteAllText(metadata, "after!");
        File.SetLastWriteTimeUtc(metadata, time);
        await Assert.ThrowsAsync<ScsSaveEditException>(() => session.VerifyUnchangedAsync());
    }

    [Fact]
    public async Task SameLengthAndTimestampChangesToReadTextAreDetected()
    {
        var path = Folder("folder", (Definition, "before"));
        using var session = await Open(Layer(path));
        session.ReadDefinition(Definition);
        var physical = Physical(path, Definition);
        var time = File.GetLastWriteTimeUtc(physical);
        File.WriteAllText(physical, "after!");
        File.SetLastWriteTimeUtc(physical, time);
        await Assert.ThrowsAsync<ScsSaveEditException>(() => session.VerifyUnchangedAsync());
    }

    [Fact]
    public async Task NewHigherShadowIsDetectedEvenWithoutAListing()
    {
        var lower = Folder("base", (Definition, "base"));
        var upper = Folder("mod");
        using var session = await Open(Layer(lower), Layer(upper, 1000));
        session.ReadDefinition(Definition);
        Write(upper, Definition, "new override");
        await Assert.ThrowsAsync<ScsSaveEditException>(() => session.VerifyUnchangedAsync());
    }

    [Fact]
    public async Task NewFilesAndSameUnionNewShadowsInvalidateListings()
    {
        var lower = Folder("base", (Definition, "base"));
        var upper = Folder("mod");
        using var session = await Open(Layer(lower), Layer(upper, 1000));
        session.GetFiles("/def/vehicle/truck");
        Write(upper, Definition, "base");
        await Assert.ThrowsAsync<ScsSaveEditException>(() => session.VerifyUnchangedAsync());
    }

    [Fact]
    public async Task MetadataHashDetectsSameLengthTimestampMutationBeforeOpen()
    {
        var path = Folder("folder", (Definition, "content"));
        var metadata = Path.Combine(_root, "profile.sii");
        File.WriteAllText(metadata, "before");
        var evidence = Evidence(Layer(path)) with { Files = [Fingerprint(metadata)] };
        var time = File.GetLastWriteTimeUtc(metadata);
        File.WriteAllText(metadata, "after!");
        File.SetLastWriteTimeUtc(metadata, time);
        var error = await Assert.ThrowsAsync<ScsPowertrainDiagnosticException>(() => Service(evidence).OpenAsync(Request()));
        Assert.Equal(evidence.GameVersion, error.Evidence!.GameVersion);
        Assert.Equal("source_snapshot_invalid", error.Diagnostic.Code);
    }

    [Fact]
    public async Task VirtualManifestHashIsCheckedAndArchiveStaysLocked()
    {
        Directory.CreateDirectory(_root);
        var zipPath = Path.Combine(_root, "mod.scs");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("manifest.sii").Open());
            writer.Write("manifest");
        }
        var file = new FileInfo(zipPath);
        var evidence = Evidence(Layer(zipPath)) with
        {
            Files = [new(zipPath, file.Length, file.LastWriteTimeUtc, Hash("manifest"), "/manifest.sii")]
        };
        using (var session = await Service(evidence).OpenAsync(Request()))
        {
            Assert.Equal("manifest", session.ReadDefinition("/manifest.sii").Content);
            if (OperatingSystem.IsWindows()) Assert.Throws<IOException>(() => File.OpenWrite(zipPath));
            await session.VerifyUnchangedAsync();
        }
        using var released = File.OpenWrite(zipPath);
    }

    [Fact]
    public async Task UnsupportedPotentialSourceIsNotOmitted()
    {
        var baseLayer = Layer(Folder("base", (Definition, "base")));
        var corrupt = Path.Combine(_root, "corrupt.scs");
        File.WriteAllText(corrupt, "invalid");
        using var session = await Open(baseLayer, Layer(corrupt, 100, false));
        var listingError = Assert.Throws<ScsPowertrainDiagnosticException>(() => session.GetFiles("/def"));
        var readError = Assert.Throws<ScsPowertrainDiagnosticException>(() => session.ReadDefinition(Definition));
        Assert.Equal("source_unreadable", listingError.Diagnostic.Code);
        Assert.Equal(corrupt, readError.Diagnostic.PhysicalPath);
        Assert.True(readError.Diagnostic.BlocksConfiguration);
    }

    [Fact]
    public async Task UnverifiedConfigurationRetainsEvidenceButCannotResolve()
    {
        var evidence = Evidence() with { ConfigurationVerified = false, Diagnostics = ["profile mismatch"] };
        using var session = await Service(evidence).OpenAsync(Request());
        Assert.Equal("profile mismatch", Assert.Single(session.Evidence.Diagnostics));
        Assert.Throws<ScsSaveEditException>(() => session.GetFiles("/def"));
        Assert.Throws<ScsSaveEditException>(() => session.ReadDefinition(Definition));
        await Assert.ThrowsAsync<ScsSaveEditException>(() => session.VerifyUnchangedAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReResolvingRootsDetectsNewLayersAndNewAmbiguousPackages(bool remainsVerified)
    {
        var layer = Layer(Folder("base", (Definition, "content")));
        var resolver = new MutableResolver(Evidence(layer));
        using var session = await new ScsEffectiveContentService(resolver).OpenAsync(Request());
        session.ReadDefinition(Definition);
        resolver.Evidence = remainsVerified
            ? Evidence(layer, Layer(Folder("new", (Definition, "override")), 1000))
            : Evidence(layer) with { ConfigurationVerified = false };
        await Assert.ThrowsAsync<ScsSaveEditException>(() => session.VerifyUnchangedAsync());
    }

    [Fact]
    public async Task RealResolverAndVfsAgreeOnFolderManifestAndDetectNewDuplicatePackage()
    {
        var home = Folder("integration/home");
        var profile = Folder("integration/home/profiles/TEST", ("/profile.sii", "SiiNunit { user_profile : .p { active_mods: 1 active_mods[0]: \"active|Active\" } }"));
        var save = Folder("integration/home/profiles/TEST/save/1",
            ("/game.sii", "SiiNunit { }"),
            ("/info.sii", "SiiNunit { save_container : .s { dependencies: 1 dependencies[0]: \"mod|active|Active\" } }"));
        var mod = Folder("integration/home/mod/active", ("/manifest.sii", "SiiNunit { mod_package : .p { package_version: \"1\" } }"), (Definition, "active definition"));
        var install = Folder("integration/install", ("/bin/win_x64/eurotrucks2.exe", "synthetic executable"));
        WriteZip(Path.Combine(install, "version.scs"), ("/version.sii", "SiiNunit { fs_pack_set : .v { application: eut2 version: \"1.62.1.0\" } }"));
        WriteZip(Path.Combine(install, "def.scs"), (Definition, "base definition"));
        var installation = new GameInstallation(GameType.Ets2, install, Path.Combine(install, "bin/win_x64/eurotrucks2.exe"), GameInstallationSource.ManualSelection);
        var request = new ScsEffectiveContentRequest(
            new(GameType.Ets2, ScsProfileStorageKind.Local, home, profile, "TEST", "Test", Path.Combine(profile, "profile.sii")),
            new(GameType.Ets2, profile, save, "1", "Test", ScsSaveKind.Manual, Path.Combine(save, "game.sii"), DateTimeOffset.UtcNow), install);
        var resolver = new ScsActiveContentResolver(new ScsPlainTextSaveCodec(), new Detector(installation), new EmptyWorkshop(), _ => "1.62.1.0");
        using var session = await new ScsEffectiveContentService(resolver).OpenAsync(request);
        Assert.True(session.Evidence.ConfigurationVerified, string.Join("; ", session.Evidence.Diagnostics));
        Assert.Equal([Definition], session.GetFiles("/def/vehicle/truck"));
        Assert.Equal("active definition", session.ReadDefinition(Definition).Content);
        await session.VerifyUnchangedAsync();
        WriteZip(mod + ".scs", ("/manifest.sii", "SiiNunit { mod_package : .p { package_version: \"1\" } }"));
        await Assert.ThrowsAsync<ScsSaveEditException>(() => session.VerifyUnchangedAsync());
    }

    [Fact]
    public async Task RejectsUnsafePathsAndRespectsCancellationAndDisposal()
    {
        using var session = await Open(Layer(Folder("base", (Definition, "base"))));
        Assert.Throws<InvalidDataException>(() => session.ReadDefinition("/../escape.sii"));
        Assert.Throws<InvalidDataException>(() => session.GetFiles("/def/../"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.ReadDefinition(Definition, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.VerifyUnchangedAsync(cancelled.Token));
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.ReadDefinition(Definition));
    }

    [Fact]
    public async Task RejectsReparsePointIntroducedAfterOpeningFolder()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Folder("base", (Definition, "base"));
        var outside = Folder("outside", (Definition, "outside"));
        using var session = await Open(Layer(path));
        session.ReadDefinition(Definition);
        var original = Path.Combine(path, "def");
        Directory.Move(original, original + "-old");
        Directory.CreateSymbolicLink(original, Path.Combine(outside, "def"));
        try { await Assert.ThrowsAsync<ScsSaveEditException>(() => session.VerifyUnchangedAsync()); }
        finally { Directory.Delete(original); }
    }

    private Task<IScsEffectiveContentSession> Open(params ScsContentLayer[] layers) => Service(Evidence(layers)).OpenAsync(Request());

    [Fact]
    public async Task SaveDependencyEvidenceIsDetachedFromCallerOwnedLists()
    {
        var original = new ScsSaveDependency(ScsSaveDependencyKind.Rdlc, "ats_toys", "DLC - Cabin Accessories", 0);
        var dependencies = new List<ScsSaveDependency> { original };
        using var session = await Service(Evidence() with { ConfigurationVerified = false, SaveDependencies = dependencies }).OpenAsync(Request());
        dependencies.Clear();
        Assert.Equal(original, Assert.Single(session.Evidence.SaveDependencies!));
        Assert.False(session.Evidence.ConfigurationVerified);
    }
    private static ScsEffectiveContentService Service(ScsActiveContentEvidence evidence) => new(new Resolver(evidence));
    private static ScsActiveContentEvidence Evidence(params ScsContentLayer[] layers) => new(GameType.Ets2, "1.62.1.0", true, [], layers, [], []);
    private static ScsContentLayer Layer(string path, int priority = 0, bool verified = true) =>
        new(path, "/", ScsPowertrainSourceKind.Game, Path.GetFileName(path), "1", priority, verified, verified ? "verified" : "unknown availability");
    private ScsEffectiveContentRequest Request() => new(
        new(GameType.Ets2, ScsProfileStorageKind.Local, _root, _root, "profile", "Profile", Path.Combine(_root, "profile.sii")),
        new(GameType.Ets2, _root, _root, "save", "Save", ScsSaveKind.AutoSave, Path.Combine(_root, "game.sii"), DateTimeOffset.UtcNow), null);
    private string Folder(string name, params (string Path, string Text)[] files)
    {
        var root = Path.Combine(_root, name);
        Directory.CreateDirectory(root);
        foreach (var file in files) Write(root, file.Path, file.Text);
        return root;
    }
    private static void Write(string root, string virtualPath, string text)
    {
        var physical = Physical(root, virtualPath);
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        File.WriteAllText(physical, text);
    }
    private static string Physical(string root, string path) => Path.Combine(root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
    private static void WriteZip(string path, params (string Path, string Text)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry.Path.TrimStart('/')).Open());
            writer.Write(entry.Text);
        }
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static ScsEvidenceFile Fingerprint(string path)
    {
        var file = new FileInfo(path);
        return new(path, file.Length, file.LastWriteTimeUtc, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }
    private sealed class Resolver(ScsActiveContentEvidence evidence) : IScsActiveContentResolver
    {
        public Task<ScsActiveContentEvidence> ResolveAsync(ScsEffectiveContentRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(evidence);
        }
    }
    private sealed class MutableResolver(ScsActiveContentEvidence evidence) : IScsActiveContentResolver
    {
        public ScsActiveContentEvidence Evidence { get; set; } = evidence;
        public Task<ScsActiveContentEvidence> ResolveAsync(ScsEffectiveContentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Evidence);
    }
    private sealed class Detector(GameInstallation installation) : IGameInstallationDetector
    {
        public GameInstallation? Validate(GameType gameType, string installPath, GameInstallationSource source = GameInstallationSource.ManualSelection) => installation;
        public Task<GameInstallation?> DetectAsync(GameType gameType, string? preferredInstallPath = null, CancellationToken cancellationToken = default) => Task.FromResult<GameInstallation?>(installation);
    }
    private sealed class EmptyWorkshop : IWorkshopContentLocator
    {
        public Task<WorkshopContentSnapshot> ScanAsync(GameType game, CancellationToken cancellationToken = default) => Task.FromResult(new WorkshopContentSnapshot(game, [], [], []));
        public Task<WorkshopContentSnapshot> ScanAsync(GameType game, string? preferredInstallPath, CancellationToken cancellationToken = default) => ScanAsync(game, cancellationToken);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
