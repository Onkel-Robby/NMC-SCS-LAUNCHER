using System.IO.Compression;
using System.Security.Cryptography;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

/// <summary>Scenarios A–D exercise actual metadata, ZIP, VFS, snapshot and rule services.</summary>
public sealed class ScsPowertrainResolutionScenarioTests
{
    private const string Model = "/def/vehicle/truck/test";
    private const string NewEngine = Model + "/engine/new.sii";
    private const string NewTransmission = Model + "/transmission/new.sii";
    private const string WorkshopIdentity = "mod_workshop_package.000000000000007B";

    [Theory]
    [InlineData(GameType.Ets2)]
    [InlineData(GameType.Ats)]
    public async Task ScenariosAAndC_NoModsExposeAndVerifyBothOriginalAlternatives(GameType game)
    {
        using var fixture = new Fixture(game);
        var original = File.ReadAllBytes(fixture.Request.Save.GameSiiPath);
        var preview = await fixture.Service().InspectAsync(fixture.Request);
        Assert.Equal(game, preview.Snapshot.Game);
        Assert.True(preview.InspectionReport!.ConfigurationVerified);
        Assert.True(preview.InspectionReport.DefinitionsAttempted);
        Assert.Equal(2, preview.Engines.Count);
        Assert.Equal(2, preview.Transmissions.Count);
        Assert.All(preview.Engines.Concat(preview.Transmissions), option => Assert.True(option.DefinitionVerified, string.Join(";", option.Reasons)));
        var assessment = preview.AssessPair(NewEngine, NewTransmission);
        Assert.True(assessment.IsVerified, string.Join(";", assessment.Reasons));
        Assert.Equal(original, File.ReadAllBytes(fixture.Request.Save.GameSiiPath));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.bak", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScenarioB_LocalAndWorkshopPriorityDeterminesTheEffectiveEngine(bool localHigher)
    {
        using var fixture = new Fixture(GameType.Ets2);
        fixture.Package(fixture.ModRoot, "local.scs", (NewEngine, Engine("Local engine", 2300)));
        var workshop = fixture.WorkshopRoot();
        fixture.Package(workshop, "current.zip", (NewEngine, Engine("Workshop engine", 2500)));
        fixture.WriteMods(localHigher ? [WorkshopIdentity, "local"] : ["local", WorkshopIdentity]);
        var preview = await fixture.Service().InspectAsync(fixture.Request);
        Assert.True(preview.InspectionReport!.ConfigurationVerified, string.Join(";", preview.Diagnostics));
        Assert.True(preview.InspectionReport.DefinitionsAttempted);
        Assert.Equal(2, preview.Engines.Count);
        Assert.Equal(2, preview.Transmissions.Count);
        var selected = Assert.Single(preview.Engines, option => option.DataPath == NewEngine);
        Assert.Equal(localHigher ? "Local engine" : "Workshop engine", selected.Name);
        Assert.Contains(localHigher ? "2300" : "2500", selected.TechnicalInfo);
        Assert.Equal(localHigher ? "local" : WorkshopIdentity, preview.InspectionReport.ActiveMods.Last().Identity);
        Assert.True(preview.AssessPair(NewEngine, NewTransmission).IsVerified);
    }

    [Theory]
    [InlineData("valid", true, null)]
    [InlineData("missing", false, "workshop_package_missing")]
    [InlineData("duplicate", false, "workshop_package_ambiguous")]
    public async Task ScenarioD_AtsWorkshopAvailabilitySeparatesDiscoveryFromVerifiedSelection(string state, bool expectedAvailable, string? expectedCode)
    {
        using var fixture = new Fixture(GameType.Ats);
        fixture.WriteMods(WorkshopIdentity);
        var workshop = fixture.WorkshopRoot();
        fixture.Package(workshop, "current.zip", (NewEngine, Engine("ATS Workshop engine", 2500)));
        if (state == "missing")
        {
            // Rebuild the inventory as a complete scan without a matching package.
            fixture.Workshop = fixture.Workshop with { Items = [] };
            File.Delete(Path.Combine(workshop, "current.zip"));
            File.Delete(Path.Combine(workshop, "versions.sii"));
            Directory.Delete(workshop);
        }
        if (state == "duplicate") fixture.AddDuplicateWorkshopRoot();
        var preview = await fixture.Service().InspectAsync(fixture.Request);
        Assert.Equal(expectedAvailable, preview.InspectionReport!.ConfigurationVerified);
        Assert.Equal(expectedAvailable, preview.InspectionReport.DefinitionsAttempted);
        Assert.Equal(expectedAvailable ? 2 : 1, preview.Engines.Count);
        Assert.Equal(expectedAvailable ? 2 : 1, preview.Transmissions.Count);
        Assert.Equal(expectedAvailable, preview.AssessPair(NewEngine, NewTransmission).IsVerified);
        if (expectedAvailable)
            Assert.Equal("ATS Workshop engine", Assert.Single(preview.Engines, option => option.DataPath == NewEngine).Name);
        else
        {
            Assert.All(preview.Engines.Concat(preview.Transmissions), option => { Assert.True(option.IsCurrent); Assert.False(option.DefinitionVerified); });
            Assert.Contains(preview.InspectionReport.Issues, issue => issue.Code == expectedCode && issue.BlocksConfiguration && issue.PackageIdentity == WorkshopIdentity);
        }
    }

    [Fact]
    public async Task UnknownActiveSourceBlocksEvenWhenOtherArchivesContainCompatibleAlternatives()
    {
        using var fixture = new Fixture(GameType.Ets2);
        fixture.WriteMods("unreadable");
        File.WriteAllText(Path.Combine(fixture.ModRoot, "unreadable.scs"), "AEM! synthetic unsupported archive");
        var preview = await fixture.Service().InspectAsync(fixture.Request);
        Assert.False(preview.InspectionReport!.ConfigurationVerified);
        Assert.False(preview.InspectionReport.DefinitionsAttempted);
        Assert.Single(preview.Engines);
        Assert.Single(preview.Transmissions);
        Assert.False(preview.AssessPair(NewEngine, NewTransmission).IsVerified);
        Assert.Contains(preview.InspectionReport.Issues, issue => issue.Code == "package_archive_unreadable" && issue.BlocksConfiguration);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("invalid")]
    [InlineData("missing")]
    public async Task AtsUnreadDependenciesAreNotReportedAsKnownEmptyHistory(string state)
    {
        using var fixture = new Fixture(GameType.Ats);
        if (state == "invalid") fixture.WriteDependencies("unknown|ats_test|Synthetic invalid type");
        if (state == "missing") File.Delete(Path.Combine(Path.GetDirectoryName(fixture.Request.Save.GameSiiPath)!, "info.sii"));
        var preview = await fixture.Service().InspectAsync(fixture.Request);
        var report = Assert.IsType<ScsTruckPowertrainInspectionReport>(preview.InspectionReport);
        if (state == "empty") Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<ScsSaveDependency>>(report.SaveDependencies));
        else Assert.Null(report.SaveDependencies);
        Assert.Equal(state == "empty", report.ConfigurationVerified);
        Assert.Equal(state == "empty", preview.AssessPair(NewEngine, NewTransmission).IsVerified);
        Assert.Equal(0, fixture.WorkshopScanCalls);
    }

    [Fact]
    public async Task AtsDlcOnlySavePreservesDependencyKindsWithoutScanningWorkshopOrBlockingOriginalPairs()
    {
        using var fixture = new Fixture(GameType.Ats);
        fixture.Dlc("dlc_oversize.scs", "ats_oversize", "Shared package title", "962750");
        fixture.Dlc("dlc_truck.scs", "ats_test_truck", "Shared package title", "123");
        fixture.Dlc("dlc_map.scs", "ats_test_map", "Shared package title", "456");
        fixture.WriteDependencies("dlc|ats_oversize|Shared package title", "rdlc|ats_test_truck|Shared package title",
            "dlc|ats_test_map|Shared package title");
        var expected = new ScsSaveDependency[]
        {
            new(ScsSaveDependencyKind.Dlc, "ats_oversize", "Shared package title", 0),
            new(ScsSaveDependencyKind.Rdlc, "ats_test_truck", "Shared package title", 1),
            new(ScsSaveDependencyKind.Dlc, "ats_test_map", "Shared package title", 2)
        };
        var original = fixture.HashFiles();

        using var session = await new ScsEffectiveContentService(fixture.Resolver()).OpenAsync(fixture.Request);
        Assert.Equal(expected, session.Evidence.SaveDependencies);
        Assert.True(session.Evidence.ConfigurationVerified);
        Assert.Empty(session.Evidence.ActiveMods);
        Assert.Equal(3, session.Evidence.Layers.Count(layer => layer.Kind == ScsPowertrainSourceKind.Dlc));
        Assert.All(session.Evidence.Layers.Where(layer => layer.Kind == ScsPowertrainSourceKind.Dlc),
            layer => { Assert.True(layer.DlcMetadataVerified); Assert.False(layer.AvailabilityVerified); });
        Assert.Equal(ScsPowertrainSourceKind.Game, session.ReadDefinition(NewEngine).Source.Kind);
        await session.VerifyUnchangedAsync();

        var preview = await fixture.Service().InspectAsync(fixture.Request);
        Assert.Equal(expected, preview.InspectionReport!.SaveDependencies);
        Assert.True(preview.InspectionReport.ConfigurationVerified);
        Assert.True(preview.InspectionReport.DefinitionsAttempted);
        Assert.Equal(2, preview.Engines.Count);
        Assert.Equal(2, preview.Transmissions.Count);
        foreach (var engine in preview.Engines)
            foreach (var transmission in preview.Transmissions)
                Assert.True(preview.AssessPair(engine.DataPath, transmission.DataPath).IsVerified);
        Assert.DoesNotContain(preview.InspectionReport.Issues, issue => issue.BlocksConfiguration || issue.Code.StartsWith("workshop_", StringComparison.Ordinal));
        Assert.Equal(0, fixture.WorkshopScanCalls);
        Assert.Equal(original, fixture.HashFiles());
    }

    [Theory]
    [InlineData("old")]
    [InlineData("new")]
    public async Task AtsDlcShadowLeavesAvailabilityFailureOnAffectedEngine(string engineName)
    {
        using var fixture = new Fixture(GameType.Ats);
        var path = Model + "/engine/" + engineName + ".sii";
        fixture.Dlc("dlc_truck.scs", "ats_test_truck", "Shared package title", "123",
            (path, Engine("DLC engine", 2500)));
        fixture.WriteDependencies("rdlc|ats_test_truck|Shared package title");

        var preview = await fixture.Service().InspectAsync(fixture.Request);
        Assert.True(preview.InspectionReport!.ConfigurationVerified);
        Assert.True(preview.InspectionReport.DefinitionsAttempted);
        Assert.Equal(2, preview.Engines.Count);
        Assert.Equal(2, preview.Transmissions.Count);
        Assert.False(Assert.Single(preview.Engines, option => option.DataPath == path).DefinitionVerified);
        Assert.True(Assert.Single(preview.Engines, option => option.DataPath != path).DefinitionVerified);
        Assert.False(preview.AssessPair(path, NewTransmission).IsVerified);
        Assert.Contains(preview.InspectionReport.Issues, issue => issue.Code == "definition_availability_unverified" &&
            issue.Scope == ScsPowertrainDiagnosticScope.Component && issue.VirtualPath == path &&
            issue.PackageIdentity == "ats_test_truck" && !issue.BlocksConfiguration);
        Assert.DoesNotContain(preview.InspectionReport.Issues, issue => issue.BlocksConfiguration || issue.Code.StartsWith("workshop_", StringComparison.Ordinal));
        Assert.Equal(0, fixture.WorkshopScanCalls);
    }

    [Fact]
    public async Task AtsActiveWorkshopIdentityCannotBeSatisfiedByDlcWithSameTitleOrNumericAppId()
    {
        using var fixture = new Fixture(GameType.Ats);
        fixture.WriteActiveMods((WorkshopIdentity, "Shared package title"));
        fixture.Dlc("dlc_truck.scs", "ats_test_truck", "Shared package title", "123");
        fixture.WriteDependencies("dlc|ats_test_truck|Shared package title", $"mod|{WorkshopIdentity}|Shared package title");

        var preview = await fixture.Service().InspectAsync(fixture.Request);
        var report = preview.InspectionReport!;
        Assert.False(report.ConfigurationVerified);
        Assert.False(report.DefinitionsAttempted);
        Assert.Equal(WorkshopIdentity, Assert.Single(report.ActiveMods).Identity);
        Assert.Equal(new ScsSaveDependency[]
        {
            new(ScsSaveDependencyKind.Dlc, "ats_test_truck", "Shared package title", 0),
            new(ScsSaveDependencyKind.Mod, WorkshopIdentity, "Shared package title", 1)
        }, report.SaveDependencies);
        Assert.Equal("ats_test_truck", Assert.Single(report.Sources, layer => layer.Kind == ScsPowertrainSourceKind.Dlc).PackageIdentity);
        Assert.DoesNotContain(report.Sources, layer => layer.Kind == ScsPowertrainSourceKind.Workshop);
        Assert.Contains(report.Issues, issue => issue.Code == "workshop_package_missing" && issue.BlocksConfiguration && issue.PackageIdentity == WorkshopIdentity);
        Assert.DoesNotContain(report.Issues, issue => issue.Code == "profile_save_mod_order_mismatch");
        Assert.Equal(1, fixture.WorkshopScanCalls);
    }

    [Fact]
    public async Task AtsHistoricalWorkshopDependencyWithNoActiveModsBlocksMismatchWithoutWorkshopScan()
    {
        using var fixture = new Fixture(GameType.Ats);
        fixture.Dlc("dlc_truck.scs", "ats_test_truck", "Shared package title", "123");
        fixture.WriteDependencies($"mod|{WorkshopIdentity}|Shared package title", "rdlc|ats_test_truck|Shared package title");

        var preview = await fixture.Service().InspectAsync(fixture.Request);
        var report = preview.InspectionReport!;
        Assert.False(report.ConfigurationVerified);
        Assert.False(report.DefinitionsAttempted);
        Assert.Empty(report.ActiveMods);
        Assert.Equal(new ScsSaveDependency[]
        {
            new(ScsSaveDependencyKind.Mod, WorkshopIdentity, "Shared package title", 0),
            new(ScsSaveDependencyKind.Rdlc, "ats_test_truck", "Shared package title", 1)
        }, report.SaveDependencies);
        Assert.Contains(report.Issues, issue => issue.Code == "profile_save_mod_order_mismatch" && issue.BlocksConfiguration);
        Assert.DoesNotContain(report.Issues, issue => issue.Code.StartsWith("workshop_", StringComparison.Ordinal));
        Assert.Equal(0, fixture.WorkshopScanCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtsMissingOrDuplicateDlcManifestIdentityDoesNotGuessEntitlement(bool duplicate)
    {
        using var fixture = new Fixture(GameType.Ats);
        fixture.Dlc("dlc_first.scs", "ats_test_truck", "Shared package title", "123");
        fixture.Dlc("dlc_second.scs", duplicate ? "ats_test_truck" : null, "Shared package title", "123");
        fixture.WriteDependencies("dlc|ats_test_truck|Shared package title");

        var evidence = await fixture.Resolver().ResolveAsync(fixture.Request);
        Assert.True(evidence.ConfigurationVerified);
        var layers = evidence.Layers.Where(layer => layer.Kind == ScsPowertrainSourceKind.Dlc).ToArray();
        Assert.Equal(2, layers.Length);
        Assert.All(layers, layer => Assert.False(layer.AvailabilityVerified));
        Assert.Equal(duplicate ? 2 : 1, layers.Count(layer => layer.PackageIdentity == "ats_test_truck"));
        if (!duplicate)
        {
            Assert.Contains(layers, layer => layer.PackageIdentity == "dlc_second.scs");
            Assert.Contains(evidence.DiagnosticDetails!, issue => issue.Code == "dlc_metadata_invalid" && !issue.BlocksConfiguration);
        }
        Assert.DoesNotContain(evidence.DiagnosticDetails!, issue => issue.Code.StartsWith("workshop_", StringComparison.Ordinal));
        Assert.Equal(0, fixture.WorkshopScanCalls);
    }

    private static string Unit(string type, string id, string attributes) => $"SiiNunit {{ {type} : {id} {{ {attributes} }} }}";
    private static string Engine(string name, int torque) => Unit("accessory_engine_data", "new.test.engine", $"name: \"{name}\" torque: {torque}");
    private static string Gearbox(string id, string ratios) => Unit("accessory_transmission_data", id, ratios + " ratios_reverse[]: -4 differential_ratio: 2.85");

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "NmcPowertrainScenarios", Guid.NewGuid().ToString("N"));
        public string ModRoot { get; }
        public ScsEffectiveContentRequest Request { get; }
        public WorkshopContentSnapshot Workshop { get; set; }
        public int WorkshopScanCalls { get; private set; }
        private readonly GameInstallation _installation;
        private readonly GameType _game;
        private readonly string _infoPath;

        public Fixture(GameType game)
        {
            _game = game;
            var home = DirectoryFor("home");
            var profile = DirectoryFor("home/profiles/SCENARIO");
            var save = DirectoryFor("home/profiles/SCENARIO/save/1");
            ModRoot = DirectoryFor("home/mod");
            _infoPath = Path.Combine(save, "info.sii");
            var gamePath = Path.Combine(save, "game.sii");
            File.WriteAllText(gamePath, ScsTruckPowertrainSnapshotReaderTests.ValidSave());
            Request = new(new(game, ScsProfileStorageKind.Local, home, profile, "SCENARIO", "Synthetic", Path.Combine(profile, "profile.sii")),
                new(game, profile, save, "1", "Synthetic", ScsSaveKind.Manual, gamePath, File.GetLastWriteTimeUtc(gamePath)), null);
            WriteMods();
            var install = DirectoryFor("install");
            var executable = Path.Combine(install, GameDefinition.For(game).ExecutableRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllText(executable, "Synthetic installation; injected version reader");
            _installation = new(game, install, executable, GameInstallationSource.ManualSelection);
            Zip(install, "version.scs", ("/version.sii", Unit("fs_pack_set", ".version", $"application: {(game == GameType.Ets2 ? "eut2" : "ats")} version: \"1.62.1.0\"")));
            Zip(install, "def.scs",
                (Model + "/data.sii", Unit("accessory_truck_data", "test.data", "require[]: engine require[]: transmission")),
                (Model + "/engine/old.sii", Unit("accessory_engine_data", "old.test.engine", "name: \"Original engine\" torque: 2000")),
                (NewEngine, Engine("Original upgrade", 2400)),
                (Model + "/transmission/old.sii", Gearbox("old.test.transmission", "ratios_forward[]: 2 ratios_forward[]: 1")),
                (NewTransmission, Gearbox("new.test.transmission", "ratios_forward[]: 3 ratios_forward[]: 1")),
                ("/def/vehicle/f_wheel/test.sii", Unit("accessory_wheel_data", "front.test.fwheel", "")));
            Workshop = new(game, [], [], []);
        }

        public ScsTruckPowertrainService Service()
        {
            var codec = new ScsPlainTextSaveCodec();
            return new(new(codec), new ScsEffectiveContentService(Resolver()));
        }

        public ScsActiveContentResolver Resolver() => new(new ScsPlainTextSaveCodec(), new Detector(_installation),
            new WorkshopLocator(Workshop, () => WorkshopScanCalls++), _ => "1.62.1.0");

        public void WriteMods(params string[] identities)
        {
            WriteActiveMods(identities.Select((id, index) => (id, $"Synthetic mod {index}")).ToArray());
            WriteDependencies(identities.Select((id, index) => $"mod|{id}|Synthetic mod {index}").ToArray());
        }

        public void WriteActiveMods(params (string Identity, string Title)[] mods)
        {
            var active = string.Join('\n', mods.Select((mod, index) => $"active_mods[{index}]: \"{mod.Identity}|{mod.Title}\""));
            File.WriteAllText(Request.Profile.ProfileSiiPath, Unit("user_profile", ".profile", $"active_mods: {mods.Length}\n{active}"));
        }

        public void WriteDependencies(params string[] values)
        {
            var dependencies = string.Join('\n', values.Select((value, index) => $"dependencies[{index}]: \"{value}\""));
            File.WriteAllText(_infoPath, Unit("save_container", ".save", $"dependencies: {values.Length}\n{dependencies}"));
        }

        public void Dlc(string fileName, string? identity, string title, string steamAppId, params (string Path, string Content)[] entries) =>
            Zip(_installation.InstallPath, fileName, [("/" + Path.GetFileNameWithoutExtension(fileName) + ".manifest.sii",
                Unit("dlc_package", ".package", $"{(identity is null ? "" : "id: " + identity)} game_id: ats display_name: \"{title}\" steam_app_id: {steamAppId}")), .. entries]);

        public KeyValuePair<string, string>[] HashFiles() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Select(path => new KeyValuePair<string, string>(path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))).ToArray();

        public string WorkshopRoot()
        {
            var root = DirectoryFor($"library/steamapps/workshop/content/{GameDefinition.For(_game).SteamAppId}/123");
            File.WriteAllText(Path.Combine(root, "versions.sii"), Unit("package_version_info", ".current", "package_name: \"current\" compatible_versions[]: \"1.62.*\""));
            Workshop = new(_game, [new(_game, 123, root, Root, DateTimeOffset.UtcNow)], [Directory.GetParent(root)!.FullName], []);
            return root;
        }

        public void AddDuplicateWorkshopRoot()
        {
            var other = DirectoryFor($"second/steamapps/workshop/content/{GameDefinition.For(_game).SteamAppId}/123");
            Workshop = Workshop with { ScannedContentRoots = [.. Workshop.ScannedContentRoots, Directory.GetParent(other)!.FullName] };
        }

        public void Package(string directory, string name, params (string Path, string Content)[] entries) =>
            Zip(directory, name, [("/manifest.sii", Unit("mod_package", ".package", "package_version: \"1.0\"")), .. entries]);

        private static void Zip(string directory, string name, params (string Path, string Content)[] entries)
        {
            using var zip = ZipFile.Open(Path.Combine(directory, name), ZipArchiveMode.Create);
            foreach (var entry in entries) { using var writer = new StreamWriter(zip.CreateEntry(entry.Path.TrimStart('/')).Open()); writer.Write(entry.Content); }
        }

        private string DirectoryFor(string relative) { var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(path); return path; }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    private sealed class Detector(GameInstallation installation) : IGameInstallationDetector
    {
        public GameInstallation? Validate(GameType gameType, string installPath, GameInstallationSource source = GameInstallationSource.ManualSelection) => installation;
        public Task<GameInstallation?> DetectAsync(GameType gameType, string? preferredInstallPath = null, CancellationToken cancellationToken = default) => Task.FromResult<GameInstallation?>(installation);
    }

    private sealed class WorkshopLocator(WorkshopContentSnapshot snapshot, Action scanned) : IWorkshopContentLocator
    {
        public Task<WorkshopContentSnapshot> ScanAsync(GameType game, CancellationToken cancellationToken = default)
        {
            scanned();
            return Task.FromResult(snapshot);
        }
        public Task<WorkshopContentSnapshot> ScanAsync(GameType game, string? preferredInstallPath, CancellationToken cancellationToken = default) => ScanAsync(game, cancellationToken);
    }
}
