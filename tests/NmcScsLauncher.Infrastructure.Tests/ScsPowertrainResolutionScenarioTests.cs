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

    private static string Unit(string type, string id, string attributes) => $"SiiNunit {{ {type} : {id} {{ {attributes} }} }}";
    private static string Engine(string name, int torque) => Unit("accessory_engine_data", "new.test.engine", $"name: \"{name}\" torque: {torque}");
    private static string Gearbox(string id, string ratios) => Unit("accessory_transmission_data", id, ratios + " ratios_reverse[]: -4 differential_ratio: 2.85");

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "NmcPowertrainScenarios", Guid.NewGuid().ToString("N"));
        public string ModRoot { get; }
        public ScsEffectiveContentRequest Request { get; }
        public WorkshopContentSnapshot Workshop { get; set; }
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
            var resolver = new ScsActiveContentResolver(codec, new Detector(_installation), new WorkshopLocator(Workshop), _ => "1.62.1.0");
            return new(new(codec), new ScsEffectiveContentService(resolver));
        }

        public void WriteMods(params string[] identities)
        {
            var active = string.Join('\n', identities.Select((id, index) => $"active_mods[{index}]: \"{id}|Synthetic mod {index}\""));
            File.WriteAllText(Request.Profile.ProfileSiiPath, Unit("user_profile", ".profile", $"active_mods: {identities.Length}\n{active}"));
            var dependencies = string.Join('\n', identities.Select((id, index) => $"dependencies[{index}]: \"mod|{id}|Synthetic mod {index}\""));
            File.WriteAllText(_infoPath, Unit("save_container", ".save", $"dependencies: {identities.Length}\n{dependencies}"));
        }

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

    private sealed class WorkshopLocator(WorkshopContentSnapshot snapshot) : IWorkshopContentLocator
    {
        public Task<WorkshopContentSnapshot> ScanAsync(GameType game, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
        public Task<WorkshopContentSnapshot> ScanAsync(GameType game, string? preferredInstallPath, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }
}
