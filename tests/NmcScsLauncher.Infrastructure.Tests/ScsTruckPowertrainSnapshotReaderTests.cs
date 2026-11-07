using System.Security.Cryptography;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsTruckPowertrainSnapshotReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nmc-truck-snapshot-tests", Guid.NewGuid().ToString("N"));
    private readonly ScsSaveReference _save;
    private static readonly string Sha = new('A', 64);

    public ScsTruckPowertrainSnapshotReaderTests()
    {
        var profile = Path.Combine(_root, "profiles", "TEST");
        var directory = Path.Combine(profile, "save", "1");
        Directory.CreateDirectory(directory);
        _save = new(GameType.Ets2, profile, directory, "1", "Test", ScsSaveKind.Manual,
            Path.Combine(directory, "game.sii"), DateTimeOffset.UtcNow);
    }

    [Fact]
    public void SnapshotBindsIndependentModelAndRetainsSharedWheelDefinitions()
    {
        var snapshot = Parse(ValidSave());
        Assert.Equal("/def/vehicle/truck/test", snapshot.TruckModelRoot);
        Assert.Equal(".truck", snapshot.TruckId);
        Assert.Equal(".data", snapshot.TruckDataAccessoryId);
        Assert.Equal(".engine", snapshot.EngineAccessoryId);
        Assert.Equal(".trans", snapshot.TransmissionAccessoryId);
        Assert.Equal(4, snapshot.Accessories.Count);
        Assert.Contains(snapshot.Accessories, accessory => accessory.DataPath == "/def/vehicle/f_wheel/test.sii");
        Assert.All(snapshot.Accessories, accessory => Assert.Empty(accessory.UnsupportedReasons));
        Assert.Equal(Sha, snapshot.SaveSha256);
    }

    [Fact]
    public void ModernAssignmentIsResolvedThroughPlayerVehicles()
    {
        var text = ValidSave().Replace(" assigned_truck: .truck", " assigned_vehicles: .assignment", StringComparison.Ordinal);
        text = AddUnit(text, "player_vehicles : .assignment {\n vehicle: .truck\n trailer: null\n}");
        Assert.Equal(".truck", Parse(text).TruckId);
    }

    [Theory]
    [InlineData("player: .player", "player: .truck")]
    [InlineData("assigned_truck: .truck", "assigned_truck: .missing")]
    [InlineData("trucks[0]: .truck", "trucks[0]: .other")]
    [InlineData("accessories[1]: .engine", "accessories[1]: .missing")]
    [InlineData("accessories[1]: .engine", "accessories[1]: null")]
    [InlineData("accessories[1]: .engine", "accessories[1]: \".engine\"")]
    [InlineData("accessories[1]: .engine", "accessories[1]: .data")]
    [InlineData("accessories: 4", "accessories: 3")]
    [InlineData("accessories: 4", "")]
    [InlineData("accessories[1]:", "accessories[4]:")]
    [InlineData("accessories[1]:", "accessories[-1]:")]
    [InlineData("trucks: 1", "")]
    [InlineData("/truck/test/engine/old.sii", "/truck/other/engine/old.sii")]
    [InlineData("/truck/test/data.sii", "/truck/test/interior/data.sii")]
    [InlineData("/def/vehicle/f_wheel/test.sii", "/def/vehicle/truck/test/data.sii")]
    [InlineData("/truck/test/engine/old.sii", "/truck/test/engine/../old.sii")]
    public void MalformedOrAmbiguousGraphFailsClosed(string before, string after) =>
        Assert.Throws<ScsSaveEditException>(() => Parse(ValidSave().Replace(before, after, StringComparison.Ordinal)));

    [Fact]
    public void ConflictingModernAndLegacyAssignmentIsRejected()
    {
        var text = ValidSave().Replace(" assigned_truck: .truck", " assigned_truck: .truck\n assigned_vehicles: .assignment", StringComparison.Ordinal);
        text = AddUnit(text, "player_vehicles : .assignment {\n vehicle: .other\n}");
        Assert.Throws<ScsSaveEditException>(() => Parse(text));
    }

    [Fact]
    public void AccessorySharedByAnotherVehicleCannotBeEdited()
    {
        var text = AddUnit(ValidSave(), "vehicle : .other {\n accessories: 1\n accessories[0]: .engine\n}");
        Assert.Throws<ScsSaveEditException>(() => Parse(text));
    }

    [Fact]
    public void AccessorySharedByTrailerCannotBeEdited()
    {
        var text = AddUnit(ValidSave(), "trailer : .other {\n accessories: 1\n accessories[0]: .engine\n}");
        Assert.Throws<ScsSaveEditException>(() => Parse(text));
    }

    [Fact]
    public void DuplicateUnitAndAlternativeHeaderCannotHideAConflictingAccessory()
    {
        var text = AddUnit(ValidSave(), "vehicle_accessory : .engine\n{\n data_path: \"/def/vehicle/truck/test/engine/evil.sii\"\n}");
        Assert.Throws<ScsSaveEditException>(() => Parse(text));
        Assert.Throws<ScsSaveEditException>(() => Parse(text.Replace(".engine\n{", ".engine {", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(".data")]
    [InlineData(".engine")]
    [InlineData(".trans")]
    public void CriticalAccessoriesRequireTheSavedRuntimeType(string unitId)
    {
        var text = ValidSave().Replace("vehicle_accessory : " + unitId, "vehicle_wheel_accessory : " + unitId, StringComparison.Ordinal);
        Assert.Throws<ScsSaveEditException>(() => Parse(text));
    }

    [Theory]
    [InlineData("vehicle_wheel_accessory", "vehicle_unknown_accessory", "unbekannt")]
    [InlineData(" offset: 0", " offset: 0\n mystery_slot: .hook", "mystery_slot")]
    public void UnsupportedAccessorySemanticsRemainVisibleAsBlockers(string before, string after, string reason)
    {
        var snapshot = Parse(ValidSave().Replace(before, after, StringComparison.Ordinal));
        Assert.Contains(snapshot.Accessories.SelectMany(accessory => accessory.UnsupportedReasons), message => message.Contains(reason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NonEmptyHookupsAreExplicitlyBlockedButEmptyArraysAreAccepted()
    {
        var text = ValidSave().Replace("vehicle_wheel_accessory", "vehicle_addon_accessory", StringComparison.Ordinal)
            .Replace(" offset: 0", " slot_name: 0\n slot_hookup: 0", StringComparison.Ordinal);
        Assert.All(Parse(text).Accessories, accessory => Assert.Empty(accessory.UnsupportedReasons));
        text = text.Replace("slot_name: 0", "slot_name: 1\n slot_name[0]: \"roof\"", StringComparison.Ordinal)
            .Replace("slot_hookup: 0", "slot_hookup: 1\n slot_hookup[0]: \"light.addon_hookup\"", StringComparison.Ordinal);
        Assert.Contains(Parse(text).Accessories, accessory => accessory.UnsupportedReasons.Any(reason => reason.Contains("Hookup")));
    }

    [Fact]
    public void ActualUnquotedSlotNamesRemainReadableWithExplicitHookupBlockers()
    {
        var text = ValidSave().Replace("vehicle_wheel_accessory", "vehicle_addon_accessory", StringComparison.Ordinal)
            .Replace(" offset: 0", " slot_name: 1\n slot_name[0]: slot1\n slot_hookup: 1\n slot_hookup[0]: \"light.addon_hookup\"", StringComparison.Ordinal);
        Assert.Contains(Parse(text).Accessories, accessory => accessory.UnsupportedReasons.Any(reason => reason.Contains("Hookup")));
    }

    [Fact]
    public void WholeSaveMayExceedMetadataParserLimit()
    {
        var text = AddUnit(ValidSave(), "unrelated : .large {\n value: \"" + new string('a', 9 * 1024 * 1024) + "\"\n}");
        Assert.Equal(".truck", Parse(text).TruckId);
    }

    [Fact]
    public void ContentBuilderChangesOnlyTheTwoSelectedDataPathLines()
    {
        var original = ValidSave();
        var read = new ScsTruckPowertrainReadResult(Parse(original), original);
        var updated = ScsTruckPowertrainSnapshotReader.BuildUpdatedContent(read,
            "/def/vehicle/truck/test/engine/new.sii", "/def/vehicle/truck/test/transmission/new.sii");
        Assert.Equal(original.Replace("engine/old.sii", "engine/new.sii", StringComparison.Ordinal)
            .Replace("transmission/old.sii", "transmission/new.sii", StringComparison.Ordinal), updated);
        Assert.Throws<ScsSaveEditException>(() => ScsTruckPowertrainSnapshotReader.BuildUpdatedContent(read,
            "/def/vehicle/truck/other/engine/new.sii", "/def/vehicle/truck/test/transmission/new.sii"));
    }

    [Fact]
    public async Task FreshTemporaryCopyDefeatsCodecCacheWithSameSizeAndTimestamp()
    {
        var original = ValidSave();
        File.WriteAllText(_save.GameSiiPath, original);
        var timestamp = File.GetLastWriteTimeUtc(_save.GameSiiPath);
        var codec = new CachedCodec();
        codec.Cache[_save.GameSiiPath] = original;
        var reader = new ScsTruckPowertrainSnapshotReader(codec);
        var before = await reader.ReadAsync(_save);
        File.WriteAllText(_save.GameSiiPath, original.Replace("engine/old.sii", "engine/new.sii", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(_save.GameSiiPath, timestamp);
        var after = await reader.ReadAsync(_save);
        Assert.EndsWith("/new.sii", after.Snapshot.CurrentEnginePath);
        Assert.NotEqual(before.Snapshot.SaveSha256, after.Snapshot.SaveSha256);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_save.GameSiiPath))), after.Snapshot.SaveSha256);
        Assert.DoesNotContain(_save.GameSiiPath, codec.ReadPaths);
        Assert.Equal(2, codec.ReadPaths.Distinct().Count());
        Assert.All(codec.ReadPaths, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public async Task SourceMutationDuringDecodeIsRejectedWithoutHoldingAutosaveLock()
    {
        File.WriteAllText(_save.GameSiiPath, ValidSave());
        var timestamp = File.GetLastWriteTimeUtc(_save.GameSiiPath);
        var codec = new CachedCodec(() =>
        {
            File.WriteAllText(_save.GameSiiPath, ValidSave().Replace("engine/old.sii", "engine/new.sii", StringComparison.Ordinal));
            File.SetLastWriteTimeUtc(_save.GameSiiPath, timestamp);
        });
        await Assert.ThrowsAsync<ScsSaveEditException>(() => new ScsTruckPowertrainSnapshotReader(codec).ReadAsync(_save));
        Assert.All(codec.ReadPaths, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public async Task ForgedSelectedPathIsRejectedBeforeCodecRuns()
    {
        File.WriteAllText(_save.GameSiiPath, ValidSave());
        var codec = new CachedCodec();
        await Assert.ThrowsAsync<ScsSaveEditException>(() => new ScsTruckPowertrainSnapshotReader(codec)
            .ReadAsync(_save with { ProfileDirectory = Path.Combine(_root, "wrong") }));
        Assert.Empty(codec.ReadPaths);
    }

    [Fact]
    public async Task OriginalBytesAndFileSetAreUnchangedByReading()
    {
        File.WriteAllText(_save.GameSiiPath, ValidSave());
        var before = File.ReadAllBytes(_save.GameSiiPath);
        await new ScsTruckPowertrainSnapshotReader(new ScsPlainTextSaveCodec()).ReadAsync(_save);
        Assert.Equal(before, File.ReadAllBytes(_save.GameSiiPath));
        Assert.Single(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    private ScsTruckPowertrainSnapshot Parse(string text) => ScsTruckPowertrainSnapshotReader.Parse(_save, Sha, text);
    private static string AddUnit(string text, string unit) => text[..text.LastIndexOf('}')] + unit + "\n}\n";

    internal static string ValidSave() => """
        SiiNunit
        {
        economy : .economy {
         player: .player
        }
        player : .player {
         assigned_truck: .truck
         trucks: 1
         trucks[0]: .truck
        }
        vehicle : .truck {
         accessories: 4
         accessories[0]: .data
         accessories[1]: .engine
         accessories[2]: .trans
         accessories[3]: .wheel
        }
        vehicle_accessory : .data {
         data_path: "/def/vehicle/truck/test/data.sii"
         refund: 0
        }
        vehicle_accessory : .engine {
         data_path: "/def/vehicle/truck/test/engine/old.sii"
         refund: 0
        }
        vehicle_accessory : .trans {
         data_path: "/def/vehicle/truck/test/transmission/old.sii"
         refund: 0
        }
        vehicle_wheel_accessory : .wheel {
         data_path: "/def/vehicle/f_wheel/test.sii"
         refund: 0
         offset: 0
         paint_color: (0, 0, 0)
        }
        }
        """;

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class CachedCodec(Action? duringRead = null) : IScsSaveCodec
    {
        internal Dictionary<string, string> Cache { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal List<string> ReadPaths { get; } = [];
        public Task<ScsSaveDocument> ReadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            ReadPaths.Add(filePath);
            if (!Cache.TryGetValue(filePath, out var content)) Cache[filePath] = content = File.ReadAllText(filePath);
            duringRead?.Invoke();
            return Task.FromResult(new ScsSaveDocument(filePath, content, ScsSaveDocumentFormat.PlainText));
        }
    }
}
