using System.Security.Cryptography;
using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsTruckPowertrainInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NmcTruckInstall", Guid.NewGuid().ToString("N"));
    private const string Model = "/def/vehicle/truck/test";
    private const string NewEngine = Model + "/engine/new.sii";
    private const string NewTransmission = Model + "/transmission/new.sii";

    [Theory]
    [InlineData(GameType.Ets2)]
    [InlineData(GameType.Ats)]
    public async Task VerifiedPairUsesExistingBackupTransactionAndPreservesEveryOtherSavedField(GameType game)
    {
        var fixture = Create(game);
        var result = await fixture.Installer.InstallAsync(fixture.Request);
        var expected = fixture.Original.Replace(Model + "/engine/old.sii", NewEngine, StringComparison.Ordinal)
            .Replace(Model + "/transmission/old.sii", NewTransmission, StringComparison.Ordinal);
        Assert.Equal(expected, await File.ReadAllTextAsync(fixture.Request.Content.Save.GameSiiPath));
        Assert.Equal(fixture.Original, await File.ReadAllTextAsync(result.BackupPath));
        Assert.Equal(fixture.Request.ExpectedSaveSha256, result.OriginalSha256, ignoreCase: true);
        Assert.NotEqual(result.OriginalSha256, result.UpdatedSha256);
        Assert.True(fixture.Source.Disposed);
        Assert.True(fixture.Source.Verifications >= 4);
    }

    [Theory]
    [InlineData("sha")]
    [InlineData("truck")]
    [InlineData("engine")]
    [InlineData("game")]
    [InlineData("profile")]
    [InlineData("storage")]
    [InlineData("profile_file")]
    [InlineData("noop")]
    public async Task ForgedOrStaleContextCannotReachTheBackupOrWrite(string invalid)
    {
        var fixture = Create(GameType.Ets2);
        var request = invalid switch
        {
            "sha" => fixture.Request with { ExpectedSaveSha256 = new string('0', 64) },
            "truck" => fixture.Request with { ExpectedTruckId = ".other" },
            "engine" => fixture.Request with { EngineDataPath = Model + "/engine/arbitrary.sii" },
            "game" => fixture.Request with { Content = fixture.Request.Content with { Profile = fixture.Request.Content.Profile with { GameType = GameType.Ats } } },
            "profile" => fixture.Request with { Content = fixture.Request.Content with { Profile = fixture.Request.Content.Profile with { ProfileDirectory = Path.Combine(_root, "other") } } },
            "storage" => fixture.Request with { Content = fixture.Request.Content with { Profile = fixture.Request.Content.Profile with { StorageKind = ScsProfileStorageKind.SteamCloudRemote } } },
            "profile_file" => fixture.Request with { Content = fixture.Request.Content with { Profile = fixture.Request.Content.Profile with { ProfileSiiPath = fixture.Request.Content.Save.GameSiiPath } } },
            _ => fixture.Request with { EngineDataPath = Model + "/engine/old.sii", TransmissionDataPath = Model + "/transmission/old.sii" }
        };
        await Assert.ThrowsAsync<ScsSaveEditException>(() => fixture.Installer.InstallAsync(request));
        Assert.Equal(fixture.Original, await File.ReadAllTextAsync(fixture.Request.Content.Save.GameSiiPath));
        Assert.False(Directory.Exists(Path.Combine(_root, "backups")));
    }

    [Fact]
    public async Task RunningGameIsAlwaysRejectedWithoutBackup()
    {
        var fixture = Create(GameType.Ets2, gameRunning: true);
        await Assert.ThrowsAsync<ScsSaveEditException>(() => fixture.Installer.InstallAsync(fixture.Request));
        Assert.Equal(fixture.Original, await File.ReadAllTextAsync(fixture.Request.Content.Save.GameSiiPath));
        Assert.False(Directory.Exists(Path.Combine(_root, "backups")));
    }

    [Fact]
    public async Task SourceChangeAfterPrecommitTriggersRollbackAndPreservesOriginalBackup()
    {
        var fixture = Create(GameType.Ets2);
        fixture.Source.FailVerificationNumber = 4;
        await Assert.ThrowsAsync<ScsSaveEditException>(() => fixture.Installer.InstallAsync(fixture.Request));
        Assert.Equal(fixture.Original, await File.ReadAllTextAsync(fixture.Request.Content.Save.GameSiiPath));
        var backups = Directory.GetFiles(Path.Combine(_root, "backups"), "game.sii", SearchOption.AllDirectories);
        Assert.Equal(fixture.Original, await File.ReadAllTextAsync(Assert.Single(backups)));
        Assert.True(fixture.Source.Disposed);
    }

    [Fact]
    public async Task ChangedControlsCannotBypassThePublicPreviewAssessment()
    {
        var fixture = Create(GameType.Ets2);
        fixture.Source.Files[NewTransmission] = "SiiNunit { accessory_transmission_data : new.test.transmission { ratios_forward[]: 2 ratios_reverse[]: -3 retarder: 4 } }";
        await Assert.ThrowsAsync<ScsSaveEditException>(() => fixture.Installer.InstallAsync(fixture.Request));
        Assert.False(Directory.Exists(Path.Combine(_root, "backups")));
    }

    [Theory]
    [InlineData(GameType.Ets2, ScsProfileStorageKind.SteamCloudRemote)]
    [InlineData(GameType.Ats, ScsProfileStorageKind.SteamCloudRemote)]
    [InlineData(GameType.Ets2, ScsProfileStorageKind.Local)]
    [InlineData(GameType.Ats, ScsProfileStorageKind.Local)]
    public async Task PhysicalSteamUserdataIsRejectedBeforeOpeningSourcesEvenWithForgedStorageKind(GameType game, ScsProfileStorageKind claimedKind)
    {
        var fixture = Create(game, storage: claimedKind, physicalRemote: true);
        await Assert.ThrowsAsync<ScsSaveEditException>(() => fixture.Installer.InstallAsync(fixture.Request));
        Assert.Equal(0, fixture.Source.Opens);
        Assert.Equal(fixture.Original, await File.ReadAllTextAsync(fixture.Request.Content.Save.GameSiiPath));
        Assert.False(Directory.Exists(Path.Combine(_root, "backups")));
    }

    private Fixture Create(GameType game, bool gameRunning = false, ScsProfileStorageKind storage = ScsProfileStorageKind.Local, bool physicalRemote = false)
    {
        var home = physicalRemote ? Path.Combine(_root, "userdata", "12345", GameDefinition.For(game).SteamAppId.ToString(), "remote") : Path.Combine(_root, "home");
        var profileDirectory = Path.Combine(home, "profiles", "ABCD");
        var saveDirectory = Path.Combine(profileDirectory, "save", "1");
        Directory.CreateDirectory(saveDirectory);
        var path = Path.Combine(saveDirectory, "game.sii");
        var original = ScsTruckPowertrainSnapshotReaderTests.ValidSave().Replace(" player: .player",
            " player: .player\n stored_actor_state: 1234567\n stored_actor_retarder: 0\n stored_actor_aux: &76543210", StringComparison.Ordinal);
        File.WriteAllText(path, original);
        var profilePath = Path.Combine(profileDirectory, "profile.sii");
        File.WriteAllText(profilePath, "SiiNunit { user_profile : profile { active_mods: 0 } }");
        var profile = new ScsProfileReference(game, storage, home, profileDirectory, "ABCD", "Fixture", profilePath);
        var save = new ScsSaveReference(game, profileDirectory, saveDirectory, "1", "Fixture", ScsSaveKind.Manual, path, new DateTimeOffset(File.GetLastWriteTimeUtc(path)));
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var source = new Source(game);
        var codec = new ScsPlainTextSaveCodec();
        var service = new ScsTruckPowertrainService(new(codec), new Effective(source));
        var installer = new ScsTruckPowertrainInstaller(service, new ScsSaveEditService(codec, new Guard(gameRunning), Path.Combine(_root, "backups")));
        return new(installer, new(new(profile, save, null), sha, ".truck", NewEngine, NewTransmission), original, source);
    }

    private sealed record Fixture(ScsTruckPowertrainInstaller Installer, ScsTruckPowertrainInstallRequest Request, string Original, Source Source);
    private sealed class Guard(bool running) : IScsGameProcessGuard { public bool IsAnyScsGameRunning() => running; }
    private sealed class Effective(Source source) : IScsEffectiveContentService
    { public Task<IScsEffectiveContentSession> OpenAsync(ScsEffectiveContentRequest request, CancellationToken token = default) { source.Opens++; return Task.FromResult<IScsEffectiveContentSession>(source); } }
    private sealed class Source(GameType game) : IScsEffectiveContentSession
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal)
        {
            [Model + "/data.sii"] = "SiiNunit { accessory_truck_data : test.data { require[]: engine require[]: transmission } }",
            [Model + "/engine/old.sii"] = "SiiNunit { accessory_engine_data : old.test.engine { torque: 2000 } }",
            [NewEngine] = "SiiNunit { accessory_engine_data : new.test.engine { torque: 2400 } }",
            [Model + "/transmission/old.sii"] = "SiiNunit { accessory_transmission_data : old.test.transmission { ratios_forward[]: 3 ratios_reverse[]: -4 } }",
            [NewTransmission] = "SiiNunit { accessory_transmission_data : new.test.transmission { ratios_forward[]: 2 ratios_reverse[]: -3 differential_ratio: 2.85 } }",
            ["/def/vehicle/f_wheel/test.sii"] = "SiiNunit { accessory_wheel_data : test.fwheel { model: \"/wheel.pmd\" } }"
        };
        public ScsActiveContentEvidence Evidence { get; } = new(game, "1.62.1.0", true, [], [], [], []);
        public int Verifications { get; private set; }
        public int Opens { get; set; }
        public int FailVerificationNumber { get; set; }
        public bool Disposed { get; private set; }
        public IReadOnlyList<string> GetFiles(string directory, CancellationToken token = default) => Files.Keys.Where(key => key.StartsWith(directory + "/", StringComparison.Ordinal)).ToArray();
        public ScsResolvedDefinition ReadDefinition(string path, CancellationToken token = default) => new(path, Files[path],
            new("fixture", "", ScsPowertrainSourceKind.Game, "fixture", "", 0, true, "verified"), "sha", true, "verified");
        public Task VerifyUnchangedAsync(CancellationToken token = default)
        {
            if (++Verifications == FailVerificationNumber) throw new ScsSaveEditException("Quellen wurden während des Einbaus geändert.");
            return Task.CompletedTask;
        }
        public void Dispose() { Disposed = true; }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
