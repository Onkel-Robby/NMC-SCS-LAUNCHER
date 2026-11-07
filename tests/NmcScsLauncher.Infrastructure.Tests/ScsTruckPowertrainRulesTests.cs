using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed partial class ScsTruckPowertrainRulesTests
{
    private const string Root = "/def/vehicle/truck/test";
    private const string Engine = Root + "/engine/old.sii";
    private const string NewEngine = Root + "/engine/new.sii";
    private const string Gearbox = Root + "/transmission/old.sii";
    private const string NewGearbox = Root + "/transmission/new.sii";
    private const string Cabin = Root + "/cabin/standard.sii";

    [Fact]
    public void VerifiedSimplePairHasTechnicalDataAndRetainsCurrentSelection()
    {
        using var source = Source();
        var preview = Build(source);
        Assert.True(preview.AssessPair(NewEngine, NewGearbox).IsVerified);
        Assert.True(Assert.Single(preview.Engines, option => option.IsCurrent).DefinitionVerified);
        Assert.Contains("2400", Assert.Single(preview.Engines, option => option.DataPath == NewEngine).TechnicalInfo);
        Assert.Contains("2", Assert.Single(preview.Transmissions, option => option.DataPath == NewGearbox).TechnicalInfo);
    }

    [Fact]
    public void PairRestrictionsAreEvaluatedWithBothSelectedParts()
    {
        using var source = Source();
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", "torque: 2400 suitable_for[]: \"new.test.transmission\"");
        var preview = Build(source);
        Assert.False(preview.AssessPair(NewEngine, Gearbox).IsVerified);
        Assert.True(preview.AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Theory]
    [InlineData("conflict_with[]: \"new.*.engine\"")]
    [InlineData("require[]: absent")]
    [InlineData("suitable_for[]: \"old.test.engine\"")]
    [InlineData("mystery_rule: true")]
    public void RetainedAccessoryRulesCannotBeIgnored(string rules)
    {
        using var source = Source();
        source.Set(Cabin, "accessory_cabin_data", "standard.test.cabin", rules);
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void EverySuitablePatternAndRequiredSuffixMustBePresent()
    {
        using var source = Source();
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine",
            "torque: 2400 suitable_for[]: \"*.cabin\" suitable_for[]: \"missing.*\" require[]: transmission");
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", "torque: 2400 require[]: transmission");
        Assert.True(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void OverridesPermitOnlyTheAlreadySelectedExactTarget()
    {
        using var source = Source();
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", $"torque: 2400 overrides[]: \"{NewGearbox}\"");
        var preview = Build(source);
        Assert.False(preview.AssessPair(NewEngine, Gearbox).IsVerified);
        Assert.True(preview.AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void DefaultsDoNotAuthorizeAnAdditionalAccessoryChange()
    {
        using var source = Source();
        var badge = Root + "/accessory/badge/one.sii";
        source.Set(badge, "accessory_addon_data", "one.test.badge", "");
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", $"torque: 2400 defaults[]: \"{badge}\"");
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", $"torque: 2400 defaults[]: \"{Gearbox}\"");
        Assert.True(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Theory]
    [InlineData("type: electric")]
    [InlineData("unknown_attribute: 42")]
    [InlineData("torque: nan")]
    [InlineData("engine_brake_positions: 2 auxiliary_brakes[]: (1, 0, 0)")]
    public void UnsupportedOrMalformedEngineCannotBeVerified(string attributes)
    {
        using var source = Source();
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", attributes);
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void UnverifiedAvailabilityAndUnknownVersionBlockThePair()
    {
        using var source = Source();
        source.Unavailable.Add(NewEngine);
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
        source.Unavailable.Clear();
        source.Evidence = source.Evidence with { GameVersion = "unknown" };
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void IncludesAreReadThroughTheEffectiveOverlayAndAvailabilityChecked()
    {
        using var source = Source();
        source.Files[NewEngine] = "SiiNunit\n{\naccessory_engine_data : new.test.engine {\n@include \"/def/shared.sui\"\n}\n}";
        source.Files["/def/shared.sui"] = "torque: 2400 conflict_with[]: \"*.cabin\"";
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
        source.Files["/def/shared.sui"] = "torque: 2400";
        Assert.True(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
        source.Unavailable.Add("/def/shared.sui");
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void MissingCyclicAndEscapingIncludesRemainDisabled()
    {
        foreach (var target in new[] { "/def/missing.sui", NewEngine, "../../../../../../escape.sui" })
        {
            using var source = Source();
            source.Files[NewEngine] = $"SiiNunit\n{{\n@include \"{target}\"\n}}";
            Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
        }
    }

    [Fact]
    public void OwnerPointerRequiresMatchingTypedUnit()
    {
        using var source = Source();
        source.Set(NewGearbox, "accessory_transmission_data", "new.test.transmission", "ratios_forward[]: 3 ratios_forward[]: 1 ratios_reverse[]: -4 transmission_names: .names");
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
        source.Files[NewGearbox] = source.Files[NewGearbox][..^1] + " transmission_names : .names { forward[]: \"1\" forward[]: \"2\" reverse[]: \"R\" } }";
        Assert.True(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void UnlistedWrongModelOrWrongTypeCannotBecomeASelection()
    {
        using var source = Source();
        source.Set(NewEngine, "accessory_transmission_data", "new.test.engine", "ratios_forward[]: 3 ratios_reverse[]: -4");
        var preview = Build(source);
        Assert.False(preview.AssessPair(NewEngine, NewGearbox).IsVerified);
        Assert.False(preview.AssessPair("/def/vehicle/truck/other/engine/a.sii", NewGearbox).IsVerified);
    }

    [Fact]
    public void UnsupportedSavedHookupsBlockEveryPair()
    {
        using var source = Source();
        var snapshot = Snapshot();
        snapshot = snapshot with { Accessories = snapshot.Accessories.Select(a => a.UnitId == "cabin" ? a with { UnsupportedReasons = new[] { "Hookup unbekannt" } } : a).ToArray() };
        Assert.False(ScsTruckPowertrainRules.Build(snapshot, source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void CancellationIsPropagated()
    {
        using var source = Source();
        Assert.Throws<OperationCanceledException>(() => ScsTruckPowertrainRules.Build(Snapshot(), source, new CancellationToken(true)));
    }

    [Fact]
    public void InformationalUnusedPackageDiagnosticDoesNotInvalidateKnownDefinitions()
    {
        using var source = Source();
        source.Evidence = source.Evidence with { Diagnostics = ["Nicht verwendetes DLC ist unbestätigt."] };
        var preview = Build(source);
        Assert.NotEmpty(preview.Diagnostics);
        Assert.True(preview.AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void UnverifiedConfigurationKeepsCurrentRowsWithoutReadingDefinitions()
    {
        using var source = Source();
        source.Evidence = source.Evidence with { ConfigurationVerified = false, Diagnostics = ["Aktive Mod fehlt."] };
        var preview = Build(source);
        Assert.False(Assert.Single(preview.Engines).DefinitionVerified);
        Assert.False(Assert.Single(preview.Transmissions).DefinitionVerified);
        Assert.False(preview.AssessPair(Engine, Gearbox).IsVerified);
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public void KnownRenderOwnersAndTheirPointersAreValidated()
    {
        using var source = Source();
        source.Set(Cabin, "accessory_interior_data", "standard.test.interior", "glass_panes[]: .glass effect_reverb_window_closed: .reverb");
        source.Files[Cabin] = source.Files[Cabin][..^1] + " glass_pane_data : .glass { locator: windshield size: (2, 1) wipers[]: .wiper } wiper_data : .wiper { center_bone: wiper length: 0.5 } sound_reverb_data : .reverb { decay_time: 1 dry_level: 0 } }";
        Assert.True(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
        source.Files[Cabin] = source.Files[Cabin].Replace("wipers[]: .wiper", "wipers[]: .missing", StringComparison.Ordinal);
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Theory]
    [InlineData("steam_inventory_id: 123")]
    [InlineData("academy_reward: \"test_reward\"")]
    public void UnconfirmedOwnershipRequirementBlocks(string requirement)
    {
        using var source = Source();
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", "torque: 2400 " + requirement);
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void IncludeCommentsAreIgnoredAndParentRelativePathResolves()
    {
        using var source = Source();
        source.Files[NewEngine] = "SiiNunit\n{\n/*\n@include \"/def/missing.sui\"\n*/\naccessory_engine_data : new.test.engine {\n@include \"../common.sui\" // real include\n}\n}";
        source.Files[Root + "/common.sui"] = "torque: 2400 name: \"@@unresolved@@\"";
        var preview = Build(source);
        Assert.True(preview.AssessPair(NewEngine, NewGearbox).IsVerified);
        Assert.Equal("new", Assert.Single(preview.Engines, option => option.DataPath == NewEngine).Name);
    }

    [Fact]
    public void FieldsIntroducedAfterDetectedGameVersionAreNotGuessed()
    {
        using var source = Source();
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", "torque: 2400 cold_start_rpm: 700");
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
        source.Evidence = source.Evidence with { GameVersion = "1.61.0.0" };
        Assert.True(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Theory]
    [InlineData("torque: 2400 torque_curve[]: (1000, 1.1)")]
    [InlineData("torque: 2400 rpm_idle: 100")]
    [InlineData("torque: 2400 volume: 0.1")]
    [InlineData("torque: 1e100")]
    [InlineData("torque: 1e-100")]
    [InlineData("torque: 2400 engine_brake_positions: 4294967296")]
    public void InvalidDocumentedPhysicalValuesRemainDisabled(string attributes)
    {
        using var source = Source();
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", attributes);
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Theory]
    [InlineData("accessory_engine_data", "wrong.notengine", "torque: 2400")]
    [InlineData("accessory_transmission_data", "wrong.nottrans", "ratios_forward[]: 3 ratios_reverse[]: -4")]
    public void PowertrainUnitNameMustHaveItsDocumentedTypeSuffix(string type, string id, string attributes)
    {
        using var source = Source();
        source.Set(Root + "/data.sii", "accessory_truck_data", "test.data", "");
        source.Set(type == "accessory_engine_data" ? NewEngine : NewGearbox, type, id, attributes);
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void PowerDisplayUsesOnlyAnExplicitUnambiguousKilowattRating()
    {
        using var source = Source();
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", "torque: 2400 info[]: \"338@@kw@@ (460@@hp@@)\"");
        var info = Assert.Single(Build(source).Engines, option => option.DataPath == NewEngine).TechnicalInfo;
        Assert.Contains("338 kW", info);
        Assert.Contains("460 PS", info);
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", "torque: 2400 info[]: \"460@@hp@@\"");
        Assert.DoesNotContain("PS", Assert.Single(Build(source).Engines, option => option.DataPath == NewEngine).TechnicalInfo);
    }

    [Fact]
    public void BrokenOldTransmissionPreventsControlStateVerification()
    {
        using var source = Source();
        source.Files.Remove(Gearbox);
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
        Assert.False(Build(source).AssessPair(NewEngine, Gearbox).IsVerified);
    }

    [Fact]
    public void ExcessiveIncludeDepthFailsWithoutRecursingUnboundedly()
    {
        using var source = Source();
        source.Files[NewEngine] = "@include \"/def/chain0.sui\"";
        for (var index = 0; index < 20; index++) source.Files[$"/def/chain{index}.sui"] = $"@include \"/def/chain{index + 1}.sui\"";
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
        Assert.InRange(source.Reads, 1, 30);
    }

    [Theory]
    [InlineData("accessory_addon_data", "vehicle_addon_accessory", "exterior_model_uk: \"/mirror.pmd\" electric_type: vehicle_interior hide_in: 4")]
    [InlineData("accessory_addon_data", "vehicle_addon_accessory", "exterior_model_uk: \"/mirror.pmd\" hide_in: 0x7E00")]
    [InlineData("accessory_head_lights_data", "vehicle_accessory", "reflectors_offset: (0, 1, 2)")]
    public void StockRenderingFieldsDoNotPreventAnUnrelatedPowertrainChange(string type, string savedType, string attributes)
    {
        using var source = Source();
        source.Set(Cabin, type, "standard.test.render", attributes);
        var snapshot = Snapshot();
        snapshot = snapshot with { Accessories = snapshot.Accessories.Select(a => a.UnitId == "cabin" ? a with { UnitType = savedType } : a).ToArray() };
        Assert.True(ScsTruckPowertrainRules.Build(snapshot, source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void RetainedTrailerCablesRequireTypedOwnerTargets()
    {
        using var source = Source();
        source.Set(Cabin, "accessory_addon_trailer_cables_data", "stock.test.trlr_cables", "cables[]: .air");
        source.Files[Cabin] = source.Files[Cabin][..^1] + " trailer_cable_data : .air { locator_prefix: air_cable_b width: 0.0175 ring_count: 10 color: (0.1, 0.2, 0.3) } }";
        var snapshot = Snapshot();
        snapshot = snapshot with { Accessories = snapshot.Accessories.Select(a => a.UnitId == "cabin" ? a with { UnitType = "vehicle_addon_accessory" } : a).ToArray() };
        Assert.True(ScsTruckPowertrainRules.Build(snapshot, source).AssessPair(NewEngine, NewGearbox).IsVerified);
        source.Files[Cabin] = source.Files[Cabin].Replace("cables[]: .air", "cables[]: .missing", StringComparison.Ordinal);
        Assert.False(ScsTruckPowertrainRules.Build(snapshot, source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void ExcessiveAccessoryIdentifierDoesNotEnterTheRuleMatcher()
    {
        using var source = Source();
        source.Set(NewEngine, "accessory_engine_data", new string('a', 257) + ".engine", "torque: 2400");
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void SuitableAlternativesOfTheSameExplicitAccessoryTypeAreDisjunctive()
    {
        using var source = Source();
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine",
            "torque: 2400 suitable_for[]: \"other.test.cabin\" suitable_for[]: \"standard.test.cabin\"");
        Assert.True(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
        source.Set(Cabin, "accessory_cabin_data", "unsupported.test.cabin", "");
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void SuitableAlternativesDoNotRemoveRequirementsForOtherAccessoryTypes()
    {
        using var source = Source();
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine",
            "torque: 2400 suitable_for[]: \"other.test.cabin\" suitable_for[]: \"standard.test.cabin\" suitable_for[]: \"new.test.transmission\"");
        var preview = Build(source);
        Assert.True(preview.AssessPair(NewEngine, NewGearbox).IsVerified);
        Assert.False(preview.AssessPair(NewEngine, Gearbox).IsVerified);
    }

    [Theory]
    [InlineData("suitable_for[]: \"*\" suitable_for[]: \"*.cabin\"")]
    [InlineData("suitable_for[]: \"standard.test.*\" suitable_for[]: \"*.cabin\"")]
    [InlineData("suitable_for[]: \"[standard].test.cabin\"")]
    public void AmbiguousTypeGroupingAndUnknownPatternSyntaxAreNotGuessed(string rules)
    {
        using var source = Source();
        source.Set(NewEngine, "accessory_engine_data", "new.test.engine", "torque: 2400 " + rules);
        Assert.False(Build(source).AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ServiceDisposesSessionAndKeepsPreviewUsableWhenSourceBecomesUnavailable(bool unavailable, bool saveChanges)
    {
        var directory = Path.Combine(Path.GetTempPath(), "NmcPowertrainRules", Guid.NewGuid().ToString("N"));
        var saveDirectory = Path.Combine(directory, "save", "1");
        Directory.CreateDirectory(saveDirectory);
        try
        {
            var path = Path.Combine(saveDirectory, "game.sii");
            await File.WriteAllTextAsync(path, ScsTruckPowertrainSnapshotReaderTests.ValidSave());
            var save = new ScsSaveReference(GameType.Ets2, directory, saveDirectory, "1", "Save", ScsSaveKind.Manual, path, DateTimeOffset.UtcNow);
            var profile = new ScsProfileReference(GameType.Ets2, ScsProfileStorageKind.Local, directory, directory, "profile", "Profile", Path.Combine(directory, "profile.sii"));
            var session = Source();
            session.Set("/def/vehicle/f_wheel/test.sii", "accessory_wheel_data", "standard.test.fwheel", "model: \"/model.pmd\"");
            var service = new ScsTruckPowertrainService(new(new ScsPlainTextSaveCodec()), new FakeService(session, unavailable,
                saveChanges ? () => File.AppendAllText(path, "\n# concurrent save change\n") : null));
            var preview = await service.InspectAsync(new(profile, save, null));
            Assert.Equal(!unavailable, session.Disposed);
            if (unavailable || saveChanges)
            {
                Assert.Single(preview.Engines);
                Assert.False(preview.AssessPair(Engine, Gearbox).IsVerified);
                Assert.Equal(saveChanges, preview.InspectionReport!.DefinitionsAttempted);
                Assert.Equal(saveChanges, preview.InspectionReport.ResultsInvalidated);
                if (saveChanges) Assert.Contains("verworfen", Assert.Single(preview.Engines).Reasons.Single());
                Assert.Equal(saveChanges ? "1.58.1.0" : "", preview.InspectionReport.GameVersion);
                Assert.DoesNotContain(preview.Diagnostics, value => value.Contains("außerhalb des geprüften", StringComparison.Ordinal));
            }
            else Assert.True(preview.AssessPair(NewEngine, NewGearbox).IsVerified);
            // No source session or original-save lease survives the returned preview.
            using var writable = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static IScsTruckPowertrainPreview Build(FakeSession source) => ScsTruckPowertrainRules.Build(Snapshot(), source);
    private static ScsTruckPowertrainSnapshot Snapshot() => new(GameType.Ets2, "save", "sha", "truck", Root,
        "data", "engine", "gearbox", Engine, Gearbox,
        new[] { new ScsTruckAccessorySnapshot("data", "vehicle_accessory", Root + "/data.sii", Array.Empty<string>()),
            new ScsTruckAccessorySnapshot("engine", "vehicle_accessory", Engine, Array.Empty<string>()),
            new ScsTruckAccessorySnapshot("gearbox", "vehicle_accessory", Gearbox, Array.Empty<string>()),
            new ScsTruckAccessorySnapshot("cabin", "vehicle_accessory", Cabin, Array.Empty<string>()) });
    private static FakeSession Source()
    {
        var result = new FakeSession();
        result.Set(Root + "/data.sii", "accessory_truck_data", "test.data", "require[]: engine require[]: transmission");
        result.Set(Engine, "accessory_engine_data", "old.test.engine", "torque: 2000");
        result.Set(NewEngine, "accessory_engine_data", "new.test.engine", "name: \"New engine\" torque: 2400");
        result.Set(Gearbox, "accessory_transmission_data", "old.test.transmission", "ratios_forward[]: 2 ratios_forward[]: 1 ratios_reverse[]: -3");
        result.Set(NewGearbox, "accessory_transmission_data", "new.test.transmission", "ratios_forward[]: 3 ratios_forward[]: 1 ratios_reverse[]: -4 differential_ratio: 2.85");
        result.Set(Cabin, "accessory_cabin_data", "standard.test.cabin", "");
        return result;
    }
    private sealed class FakeSession : IScsEffectiveContentSession
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Unavailable { get; } = new(StringComparer.Ordinal);
        public int Reads { get; private set; }
        public bool Disposed { get; private set; }
        public ScsActiveContentEvidence Evidence { get; set; } = new(GameType.Ets2, "1.58.1.0", true, [], [], [], []);
        public void Set(string path, string type, string id, string attrs) => Files[path] = $"SiiNunit {{ {type} : {id} {{ {attrs} }} }}";
        public IReadOnlyList<string> GetFiles(string path, CancellationToken token = default) => Files.Keys.Where(p => p.StartsWith(path + "/", StringComparison.Ordinal)).ToArray();
        public ScsResolvedDefinition ReadDefinition(string path, CancellationToken token = default) { Reads++; return new(path,
            Files.TryGetValue(path, out var text) ? text : throw new FileNotFoundException(path),
            new("fixture", "", ScsPowertrainSourceKind.Game, "fixture", "", 0, true, "verified"), "sha", !Unavailable.Contains(path), "Verfügbarkeit unbestätigt"); }
        public Task VerifyUnchangedAsync(CancellationToken token = default) => Task.CompletedTask;
        public void Dispose() { Disposed = true; }
    }
    private sealed class FakeService(FakeSession session, bool unavailable, Action? duringOpen = null) : IScsEffectiveContentService
    {
        public Task<IScsEffectiveContentSession> OpenAsync(ScsEffectiveContentRequest request, CancellationToken token = default)
        {
            duringOpen?.Invoke();
            return unavailable ? throw new ScsSaveEditException("Aktive Quelle fehlt.") : Task.FromResult<IScsEffectiveContentSession>(session);
        }
    }
}

