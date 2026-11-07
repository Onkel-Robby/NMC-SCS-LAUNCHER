using System.IO.Compression;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsPowertrainDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Nmc-PowertrainDiscovery", Guid.NewGuid().ToString("N"));
    private const string Engine = "/def/vehicle/truck/volvo.fh16/engine/d13.sii";
    private const string Transmission = "/def/vehicle/truck/volvo.fh16/transmission/i_shift.sii";
    private static readonly ScsActiveTruckPowertrainCatalog EmptySave = new("/def/vehicle/truck/volvo.fh16/", [], []);

    [Fact]
    public async Task SeparatesGamesAndUsesActualDefinitionPathsAndNames()
    {
        var ets = DirectoryFor("ets");
        var ats = DirectoryFor("ats");
        WriteZip(ets, "def.scs", (Engine, Definition("accessory_engine_data", "d13.volvo.engine", "D13 460")));
        WriteZip(ats, "def.scs", ("/def/vehicle/truck/kenworth.w900/engine/cat.sii", Definition("accessory_engine_data", "cat.kw.engine", "CAT C15")));
        var service = Service(new Dictionary<GameType, string> { [GameType.Ets2] = ets, [GameType.Ats] = ats });

        var etsResult = await service.DiscoverAsync(Request());
        var atsResult = await service.DiscoverAsync(Request(GameType.Ats));

        var engine = Assert.Single(etsResult.Components);
        Assert.Equal(Engine, engine.DataPath);
        Assert.Equal("D13 460", engine.Name);
        Assert.Equal("volvo.fh16", engine.TruckModel);
        Assert.Equal(ScsPowertrainCompatibility.ModelMatchUnverified, engine.Compatibility);
        Assert.Equal(ScsPowertrainSourceKind.Game, Assert.Single(engine.Sources).Kind);
        Assert.Equal(GameType.Ats, Assert.Single(atsResult.Components).Game);
        Assert.DoesNotContain(atsResult.Components, item => item.DataPath == Engine);
    }

    [Fact]
    public async Task KeepsExistingSaveCatalogWhenSourcesFailAndDoesNotMutateIt()
    {
        var install = DirectoryFor("game");
        File.WriteAllBytes(Path.Combine(install, "broken.scs"), [1, 2, 3]);
        var save = new ScsActiveTruckPowertrainCatalog(EmptySave.TruckModelRoot,
            [new(Engine, "truck.1", true), new(Engine, "truck.2", false)], [new(Transmission, "truck.1", true)]);
        var result = await Service(install).DiscoverAsync(Request() with { SaveCatalog = save });

        Assert.Equal(2, result.Components.Count);
        Assert.All(result.Components, item => Assert.Equal(ScsPowertrainCompatibility.ObservedInSave, item.Compatibility));
        Assert.All(result.Components, item => Assert.True(item.IsCurrent));
        Assert.Equal(2, result.Components.Single(item => item.Kind == ScsPowertrainKind.Engine).Sources.Count);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Equal(2, save.Engines.Count);
    }

    [Fact]
    public async Task CorruptArchiveDoesNotPreventReadingAnotherValidSource()
    {
        var install = DirectoryFor("game");
        File.WriteAllBytes(Path.Combine(install, "a-broken.scs"), "PK invalid archive"u8.ToArray());
        WriteZip(install, "z-valid.scs", (Engine, Definition("accessory_engine_data", "d13.engine", "D13")));

        var result = await Service(install).DiscoverAsync(Request());

        Assert.Equal("D13", Assert.Single(result.Components).Name);
        Assert.Contains(result.Diagnostics, text => text.Contains("a-broken.scs"));
    }

    [Fact]
    public async Task OnlyExistingDlcFilesAreInventoriedAndPresenceNeverProvesAvailability()
    {
        var install = DirectoryFor("game");
        WriteZip(install, "dlc_volvo.scs", (Engine, Definition("accessory_engine_data", "d13.engine", "D13")));
        File.WriteAllText(Path.Combine(install, "dlc_missing.manifest"), "dlc_missing.scs");
        var result = await Service(install).DiscoverAsync(Request());

        var item = Assert.Single(result.Components);
        Assert.Equal(ScsPowertrainSourceKind.Dlc, Assert.Single(item.Sources).Kind);
        Assert.Equal(ScsPowertrainCompatibility.ModelMatchUnverified, item.Compatibility);
        Assert.Contains("DLC", item.CompatibilityReason);
        Assert.DoesNotContain(result.Components.SelectMany(item => item.Sources), source => source.Name.Contains("missing"));
    }

    [Fact]
    public async Task ReadsSelectedProfileModsAndKeepsOtherModelsNonCompatible()
    {
        var home = DirectoryFor("profilehome");
        var mods = DirectoryFor("profilehome/mod");
        WriteZip(mods, "tuning.scs", ("/def/vehicle/truck/scania.r/engine/v8.sii", Definition("accessory_engine_data", "v8.scania.engine", "V8")));
        var result = await Service().DiscoverAsync(Request() with { GameHomeDirectory = home });

        var item = Assert.Single(result.Components);
        Assert.Equal(ScsPowertrainCompatibility.DifferentModel, item.Compatibility);
        Assert.Equal(ScsPowertrainSourceKind.LocalMod, Assert.Single(item.Sources).Kind);
    }

    [Fact]
    public async Task InventoriesOnlySameGameAdditionalModDirectories()
    {
        var etsMods = DirectoryFor("etsmods");
        var atsMods = DirectoryFor("atsmods");
        WriteFolder(etsMods, "loose", Engine, Definition("accessory_engine_data", "d13.engine", "D13"));
        WriteFolder(atsMods, "loose", Transmission, Definition("accessory_transmission_data", "gear.transmission", "Wrong game"));

        var result = await Service().DiscoverAsync(Request() with
        {
            ModDirectories = [new(GameType.Ets2, "Europe", etsMods), new(GameType.Ats, "America", atsMods)]
        });

        var item = Assert.Single(result.Components);
        Assert.Equal(Engine, item.DataPath);
        Assert.Contains("Europe", Assert.Single(item.Sources).Name);
    }

    [Fact]
    public async Task DeduplicatesIdenticalDefinitionsAndRetainsEveryProvenance()
    {
        var install = DirectoryFor("game");
        var mods = DirectoryFor("home/mod");
        var text = Definition("accessory_engine_data", "d13.engine", "D13");
        WriteZip(install, "def.scs", (Engine, text));
        WriteZip(mods, "copy.scs", (Engine, text));
        var result = await Service(install).DiscoverAsync(Request() with { GameHomeDirectory = DirectoryFor("home") });

        var item = Assert.Single(result.Components);
        Assert.Equal(2, item.Sources.Count);
        Assert.Equal(ScsPowertrainCompatibility.ModelMatchUnverified, item.Compatibility);
    }

    [Fact]
    public async Task ConflictingOverridesAreAmbiguousWithoutInventingModPriority()
    {
        var mods = DirectoryFor("home/mod");
        WriteZip(mods, "a.scs", (Engine, Definition("accessory_engine_data", "d13.engine", "460 HP")));
        WriteZip(mods, "z.scs", (Engine, Definition("accessory_engine_data", "d13.engine", "900 HP")));
        var result = await Service().DiscoverAsync(Request() with { GameHomeDirectory = DirectoryFor("home") });

        var item = Assert.Single(result.Components);
        Assert.Equal(ScsPowertrainCompatibility.Ambiguous, item.Compatibility);
        Assert.Equal(2, item.Sources.Count);
        Assert.Contains("Reihenfolge", item.CompatibilityReason);
        Assert.DoesNotContain(item.Name, new[] { "460 HP", "900 HP" });
    }

    [Fact]
    public async Task IncludesResolveSourceLocallyAndRetainAccessoryRuleBoundary()
    {
        var install = DirectoryFor("game");
        WriteZip(install, "def.scs", (Engine, "SiiNunit { accessory_engine_data : d13.engine {\n@include \"../common.sui\"\n} }"),
            ("/def/vehicle/truck/volvo.fh16/common.sui", "# comment\nname: \"D13 with includes\"\nrequire[]: \".transmission\""));
        var result = await Service(install).DiscoverAsync(Request());

        var item = Assert.Single(result.Components);
        Assert.Equal("D13 with includes", item.Name);
        Assert.Equal(ScsPowertrainCompatibility.ModelMatchUnverified, item.Compatibility);
        Assert.Contains("Zubehör", item.CompatibilityReason);
    }

    [Fact]
    public async Task ConditionalModPackagesUseDiscoveredMountPathsAndSourceLocalIncludes()
    {
        var mods = DirectoryFor("home/mod");
        WriteZip(mods, "conditional.scs",
            ("/base" + Engine, "SiiNunit { accessory_engine_data : d13.engine {\n@include \"/def/shared.sui\"\n} }"),
            ("/base/def/shared.sui", "name: \"Base package engine\""),
            ("/dlc_tuning" + Transmission, Definition("accessory_transmission_data", "gear.transmission", "Conditional gearbox")));

        var result = await Service().DiscoverAsync(Request() with { GameHomeDirectory = DirectoryFor("home") });

        Assert.Equal(2, result.Components.Count);
        var engine = result.Components.Single(item => item.Kind == ScsPowertrainKind.Engine);
        Assert.Equal(Engine, engine.DataPath);
        Assert.Equal("Base package engine", engine.Name);
        Assert.Contains("!/base", Assert.Single(engine.Sources).Location);
        Assert.All(result.Components, item => Assert.Equal(ScsPowertrainCompatibility.ModelMatchUnverified, item.Compatibility));
        Assert.Contains("dlc_tuning", Assert.Single(result.Components.Single(item => item.Kind == ScsPowertrainKind.Transmission).Sources).Name);
    }

    [Fact]
    public async Task UnresolvedFileDoesNotPreventRecognizingOtherDefinitionsInSameArchive()
    {
        var install = DirectoryFor("game");
        WriteZip(install, "def.scs", (Engine, "SiiNunit { /* unterminated"),
            (Transmission, Definition("accessory_transmission_data", "gear.transmission", "I-Shift")));

        var result = await Service(install).DiscoverAsync(Request());

        Assert.Equal(2, result.Components.Count);
        Assert.Equal(ScsPowertrainCompatibility.Unresolved, result.Components.Single(item => item.Kind == ScsPowertrainKind.Engine).Compatibility);
        Assert.Equal("I-Shift", result.Components.Single(item => item.Kind == ScsPowertrainKind.Transmission).Name);
    }

    [Fact]
    public async Task ExpansionBudgetRejectsOversizedDefinitionWithoutLosingSaveFallback()
    {
        var install = DirectoryFor("game");
        WriteZip(install, "def.scs", (Engine, "/*" + new string('x', 2 * 1024 * 1024) + "*/\n" + Definition("accessory_engine_data", "d13.engine", "D13")));
        var save = new ScsActiveTruckPowertrainCatalog(EmptySave.TruckModelRoot, [new(Engine, "truck.1", true)], []);

        var result = await Service(install).DiscoverAsync(Request() with { SaveCatalog = save });

        var item = Assert.Single(result.Components);
        Assert.True(item.IsCurrent);
        Assert.Equal(ScsPowertrainCompatibility.Unresolved, item.Compatibility);
        Assert.Contains(item.Sources, source => source.Kind == ScsPowertrainSourceKind.Save);
        Assert.Contains("Textlimit", item.CompatibilityReason);
    }

    [Theory]
    [InlineData("@include \"missing.sui\"", null)]
    [InlineData("@include \"cycle.sui\"", "@include \"cycle.sui\"")]
    [InlineData("@include \"../../../../../../../../escape.sui\"", null)]
    public async Task MissingCyclicAndEscapingIncludesStayUnresolved(string include, string? content)
    {
        var install = DirectoryFor("game");
        var entries = new List<(string, string)> { (Engine, "SiiNunit { accessory_engine_data : d13.engine {\n" + include + "\n} }") };
        if (content is not null) entries.Add(("/def/vehicle/truck/volvo.fh16/engine/cycle.sui", content));
        WriteZip(install, "def.scs", entries.ToArray());

        var result = await Service(install).DiscoverAsync(Request());

        Assert.Equal(ScsPowertrainCompatibility.Unresolved, Assert.Single(result.Components).Compatibility);
    }

    [Fact]
    public async Task ParsesCommentsAndStringsWithoutInventingLocalization()
    {
        var install = DirectoryFor("game");
        WriteZip(install, "def.scs", (Engine, "SiiNunit { /* name: \"ignored\" */ accessory_engine_data : d13.engine {\n// name: \"wrong\"\nname: \"@@d13_name@@\" # tail\ninfo[]: \"http://example.test/#value\"\n} }"));

        var result = await Service(install).DiscoverAsync(Request());

        var item = Assert.Single(result.Components);
        Assert.Equal("d13", item.Name);
        Assert.Equal(ScsPowertrainCompatibility.ModelMatchUnverified, item.Compatibility);
    }

    [Theory]
    [InlineData("SiiNunit { accessory_engine_data : e { name: \"A\" } accessory_engine_data : f { name: \"B\" } }", ScsPowertrainCompatibility.Ambiguous)]
    [InlineData("SiiNunit { accessory_transmission_data : t { name: \"T\" } }", ScsPowertrainCompatibility.Unresolved)]
    [InlineData("SiiNunit { accessory_engine_data : e { name: \"A\" }", ScsPowertrainCompatibility.Unresolved)]
    [InlineData("SiiNunit { accessory_engine_data : e { /* missing comment close", ScsPowertrainCompatibility.Unresolved)]
    [InlineData("\"SiiNunit\" \"{\" accessory_engine_data \":\" engine.test \"{\" name \":\" \"Fake\" } \"}\"", ScsPowertrainCompatibility.Unresolved)]
    public async Task AmbiguousOrUnsupportedUnitsNeverGainCompatibility(string text, ScsPowertrainCompatibility expected)
    {
        var install = DirectoryFor("game");
        WriteZip(install, "def.scs", (Engine, text));

        var result = await Service(install).DiscoverAsync(Request());

        Assert.Equal(expected, Assert.Single(result.Components).Compatibility);
    }

    [Fact]
    public async Task DuplicateUnitIdsAtDifferentPathsAreAmbiguous()
    {
        var install = DirectoryFor("game");
        WriteZip(install, "def.scs", (Engine, Definition("accessory_engine_data", "same.engine", "One")),
            (Engine.Replace("d13.sii", "d16.sii"), Definition("accessory_engine_data", "same.engine", "Two")));

        var result = await Service(install).DiscoverAsync(Request());

        Assert.Equal(2, result.Components.Count);
        Assert.All(result.Components, item => Assert.Equal(ScsPowertrainCompatibility.Ambiguous, item.Compatibility));
    }

    [Fact]
    public async Task WorkshopVariantsRemainUnverifiedAndWrongGameItemsAreIgnored()
    {
        var workshop = DirectoryFor("workshop");
        File.WriteAllText(Path.Combine(workshop, "versions.sii"), "SiiNunit { package_version_info : .v1 { package_name: \"old\" } }");
        WriteFolder(workshop, "old", Engine, Definition("accessory_engine_data", "d13.engine", "Old package"));
        WriteFolder(workshop, "new", Engine, Definition("accessory_engine_data", "d13.engine", "New package"));
        var wrongGame = DirectoryFor("wrongworkshop");
        WriteFolder(wrongGame, "universal", Transmission, Definition("accessory_transmission_data", "t.transmission", "Wrong game"));
        var locator = new FakeWorkshop([
            new(GameType.Ets2, 123, workshop, _root, DateTimeOffset.UtcNow),
            new(GameType.Ats, 456, wrongGame, _root, DateTimeOffset.UtcNow)]);

        var result = await Service(workshop: locator).DiscoverAsync(Request());

        var item = Assert.Single(result.Components);
        Assert.Equal(ScsPowertrainCompatibility.Ambiguous, item.Compatibility);
        Assert.All(item.Sources, source => Assert.Equal(ScsPowertrainSourceKind.Workshop, source.Kind));
        Assert.Contains(result.Diagnostics, text => text.Contains("Version"));
    }

    [Fact]
    public async Task CancellationIsNotConvertedToAFallbackResult()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().DiscoverAsync(Request(), cancellation.Token));
    }

    [Fact]
    public async Task DefinitionDiscoveryDoesNotModifyAnyInputFile()
    {
        var install = DirectoryFor("game");
        var archive = WriteZip(install, "def.scs", (Engine, Definition("accessory_engine_data", "d13.engine", "D13")));
        var before = File.ReadAllBytes(archive);
        await Service(install).DiscoverAsync(Request());
        Assert.Equal(before, File.ReadAllBytes(archive));
        Assert.Single(Directory.GetFiles(install));
    }

    private ScsPowertrainDiscoveryService Service(string? install = null, IWorkshopContentLocator? workshop = null) =>
        Service(install is null ? [] : new Dictionary<GameType, string> { [GameType.Ets2] = install }, workshop);

    private static ScsPowertrainDiscoveryService Service(Dictionary<GameType, string> installs, IWorkshopContentLocator? workshop = null) =>
        new(new FakeDetector(installs), workshop ?? new FakeWorkshop([]));

    private static ScsPowertrainDiscoveryRequest Request(GameType game = GameType.Ets2) => new(game, null, null, EmptySave);
    private string DirectoryFor(string path) { var result = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(result); return result; }
    private static string Definition(string type, string id, string name) => $"SiiNunit {{ {type} : {id} {{ name: \"{name}\" }} }}";
    private static string WriteZip(string directory, string file, params (string Path, string Text)[] entries)
    {
        var path = Path.Combine(directory, file);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries) { using var writer = new StreamWriter(archive.CreateEntry(entry.Path.TrimStart('/')).Open()); writer.Write(entry.Text); }
        return path;
    }
    private static void WriteFolder(string directory, string package, string virtualPath, string text)
    {
        var path = Path.Combine(directory, package, virtualPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private sealed class FakeDetector(Dictionary<GameType, string> installs) : IGameInstallationDetector
    {
        public GameInstallation? Validate(GameType gameType, string installPath, GameInstallationSource source = GameInstallationSource.ManualSelection) =>
            installs.TryGetValue(gameType, out var path) ? new(gameType, path, Path.Combine(path, "game.exe"), source) : null;
        public Task<GameInstallation?> DetectAsync(GameType gameType, string? preferredInstallPath = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validate(gameType, preferredInstallPath ?? ""));
    }
    private sealed class FakeWorkshop(IReadOnlyList<WorkshopContentItem> items) : IWorkshopContentLocator
    {
        public Task<WorkshopContentSnapshot> ScanAsync(GameType game, CancellationToken cancellationToken = default) => ScanAsync(game, null, cancellationToken);
        public Task<WorkshopContentSnapshot> ScanAsync(GameType game, string? preferredInstallPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkshopContentSnapshot(game, items, [], []));
    }
}
