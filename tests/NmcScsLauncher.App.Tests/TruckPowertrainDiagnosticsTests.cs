using System.Text.Json;
using System.IO;
using System.Windows.Controls;
using NmcScsLauncher.App.Services;
using NmcScsLauncher.App.Views;
using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.App.Tests;

public sealed partial class TruckPowertrainSelectionTests
{
    private static readonly ScsPowertrainDiagnostic MissingPackage = new("active_package_missing",
        ScsPowertrainDiagnosticStage.ActiveConfiguration, ScsPowertrainDiagnosticScope.Source,
        "Das aktive Paket fehlt.", "workshop.1234567890", "Example Truck", @"C:\Private\mod.scs", "1.2",
        TechnicalDetails: "Lokale technische Details", BlocksConfiguration: true);

    [Fact]
    public void SelectedProfileChangeUpdatesDisplayedGameHome()
    {
        var harness = new SaveEditorTestHarness();
        harness.ViewModel.SaveEditorSelectedProfile = SaveEditorTestHarness.Profile("local") with
            { GameHomeDirectory = "local-game-home" };
        Assert.Equal("local-game-home", harness.ViewModel.SaveEditorGameHome);
        harness.ViewModel.SaveEditorSelectedProfile = SaveEditorTestHarness.Profile("remote") with
            { GameHomeDirectory = "remote-game-home", StorageKind = ScsProfileStorageKind.SteamCloudRemote };
        Assert.Equal("remote-game-home", harness.ViewModel.SaveEditorGameHome);
    }

    [Fact]
    public async Task LocalDiagnosticsIdentifySelectedProfileAndSaveWithoutClaimingGameSelection()
    {
        var (harness, service) = Create((request, _) => Task.FromResult<IScsTruckPowertrainPreview>(new Preview(request)
        { InspectionReport = new("1.62.1.0", true, true, [], [], []) }));
        await Loaded(harness.ViewModel);
        var request = service.Requests.Last();
        var details = harness.ViewModel.TruckPowertrainSourceDetails;
        Assert.Contains(request.Profile.DisplayName, details);
        Assert.Contains(request.Profile.ProfileSiiPath, details);
        Assert.Contains(request.Save.GameSiiPath, details);
        Assert.Contains("1970-01-01", details);
        Assert.Contains("Local", details);
        Assert.Contains("Auswahl im Launcher", details);
        Assert.Contains("zuletzt im Spiel verwendeter Save nicht bestätigt", details);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownAndKnownEmptyDependencyHistoryRemainDistinctInUiAndExport(bool read)
    {
        var (harness, service) = Create((request, _) => Task.FromResult<IScsTruckPowertrainPreview>(new Preview(request)
        { InspectionReport = new("1.62.1.0", false, false, [], [], [], SaveDependencies: read ? [] : null) }));
        await Loaded(harness.ViewModel);
        Assert.Contains(read ? "DLC-Abhängigkeiten aus info.sii (0)" : "Save-Abhängigkeiten aus info.sii: nicht ermittelt.",
            harness.ViewModel.TruckPowertrainSourceDetails);
        var request = service.Requests.Last();
        using var json = JsonDocument.Parse(TruckPowertrainDiagnosticReport.Create(request, new Preview(request)
        { InspectionReport = new("1.62.1.0", false, false, [], [], [], SaveDependencies: read ? [] : null) }));
        Assert.Equal(read, json.RootElement.GetProperty("saveDependenciesRead").GetBoolean());
    }

    [Fact]
    public async Task DiagnosticsSeparateActiveModsSavedHistoryAndDlcArchiveEvidence()
    {
        var (harness, _) = Create((request, _) => Task.FromResult<IScsTruckPowertrainPreview>(new Preview(request)
            { InspectionReport = DependencyReport() }));
        await Loaded(harness.ViewModel);
        var details = harness.ViewModel.TruckPowertrainSourceDetails;
        Assert.Contains("Aktive Mods aus profile.sii (1)", details);
        Assert.Contains("Mod-Historie aus info.sii (2)", details);
        Assert.Contains("im ausgewählten Profil als aktiv eingetragen", details);
        Assert.Contains("nur im Save vermerkt", details);
        Assert.Contains("DLC-Abhängigkeiten aus info.sii (4)", details);
        Assert.Contains("RDLC-Abhängigkeiten aus info.sii (1)", details);
        Assert.Contains("1 Archiv mit exakt passender Manifest-ID", details);
        Assert.Contains("2 Archive mit exakt passender Manifest-ID", details);
        Assert.Contains("kein Archiv mit exakt passender Manifest-ID", details);
        Assert.Contains("Datei vorhanden bestätigt keine aktuelle Berechtigung oder Aktivierung", details);
        Assert.Contains("Spielarchive: 1", details);
        Assert.Contains("DLC-Archive: 4", details);
        Assert.Contains("Lokale Mod-Quellen: 1", details);
        Assert.Contains("Workshop-Quellen: 1", details);
        Assert.False(harness.ViewModel.TruckPowertrainConfigurationBlocked);
    }

    [Fact]
    public void ExportMapsTypedDependenciesByExactIdentityWithoutPrivateContext()
    {
        var request = new ScsEffectiveContentRequest(SaveEditorTestHarness.Profile("PRIVATE_PROFILE"),
            SaveEditorTestHarness.Save("PRIVATE_SAVE"), @"C:\PRIVATE_USER\Game");
        var preview = new Preview(request) { InspectionReport = DependencyReport() };
        var report = TruckPowertrainDiagnosticReport.Create(request, preview);
        using var json = JsonDocument.Parse(report);
        var dependencies = json.RootElement.GetProperty("saveDependencies");
        Assert.Equal("Active", dependencies[0].GetProperty("status").GetString());
        Assert.Equal("Historical", dependencies[1].GetProperty("status").GetString());
        Assert.Equal("Dlc", dependencies[2].GetProperty("kind").GetString());
        Assert.Equal("Matched", dependencies[2].GetProperty("status").GetString());
        Assert.Equal(1, dependencies[2].GetProperty("matchingSourceCount").GetInt32());
        Assert.StartsWith("<GameInstall>/", dependencies[2].GetProperty("matchingSources")[0].GetString());
        Assert.Equal("Rdlc", dependencies[3].GetProperty("kind").GetString());
        Assert.Equal("Ambiguous", dependencies[3].GetProperty("status").GetString());
        Assert.Equal(2, dependencies[3].GetProperty("matchingSourceCount").GetInt32());
        Assert.Equal("NoMatch", dependencies[4].GetProperty("status").GetString()); // wrong case
        Assert.Equal("NoMatch", dependencies[5].GetProperty("status").GetString()); // mod source is not DLC
        Assert.Equal("NoMatch", dependencies[6].GetProperty("status").GetString()); // invalid DLC manifest
        Assert.DoesNotContain("PRIVATE", report);
        Assert.DoesNotContain("DisplayNameMustStayLocal", report);
        Assert.DoesNotContain("1970-01-01", report);
        Assert.False(json.RootElement.TryGetProperty("profile", out _));
        Assert.False(json.RootElement.TryGetProperty("save", out _));
    }

    [Fact]
    public void TruncatedDiagnosticsCannotTurnInvalidDlcMetadataIntoAMatch()
    {
        var request = new ScsEffectiveContentRequest(SaveEditorTestHarness.Profile("preview"),
            SaveEditorTestHarness.Save("preview"), null);
        // Source evidence survives even when the bounded report omits its invalid-manifest issue.
        var preview = new Preview(request) { InspectionReport = DependencyReport() with { Issues = [] } };
        using var json = JsonDocument.Parse(TruckPowertrainDiagnosticReport.Create(request, preview));
        var rows = json.RootElement.GetProperty("saveDependencies");
        Assert.Equal("Matched", rows[2].GetProperty("status").GetString());
        Assert.Equal("NoMatch", rows[6].GetProperty("status").GetString());
    }

    [Fact]
    public void ExportBoundsDependenciesAndAppliesIdentityPrivacyFilter()
    {
        var request = new ScsEffectiveContentRequest(SaveEditorTestHarness.Profile("PRIVATE_PROFILE"),
            SaveEditorTestHarness.Save("PRIVATE_SAVE"), null);
        var dependencies = new[]
        {
            new ScsSaveDependency(ScsSaveDependencyKind.Dlc, "PRIVATE_PROFILE", "Private display", 0),
            new ScsSaveDependency(ScsSaveDependencyKind.Mod, "76561198000000000", "Account", 1),
            new ScsSaveDependency(ScsSaveDependencyKind.Mod, "mod_workshop_package.1234567890abcdef", "Workshop", 2)
        }.Concat(Enumerable.Repeat(new ScsSaveDependency(ScsSaveDependencyKind.Dlc, "safe_dlc", "Safe", 3), 150)).ToArray();
        var preview = new Preview(request) { InspectionReport = new("1.62.1.0", true, true, [], [], [], SaveDependencies: dependencies) };
        var report = TruckPowertrainDiagnosticReport.Create(request, preview);
        using var json = JsonDocument.Parse(report);
        var rows = json.RootElement.GetProperty("saveDependencies");
        Assert.True(rows.GetArrayLength() <= 128);
        Assert.True(json.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("packageId").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("packageId").ValueKind);
        Assert.Equal("mod_workshop_package.1234567890abcdef", rows[2].GetProperty("packageId").GetString());
        Assert.DoesNotContain("PRIVATE", report);
        Assert.DoesNotContain("76561198000000000", report);
    }

    private static ScsTruckPowertrainInspectionReport DependencyReport() => new("1.62.1.0", true, true,
        [new("active_mod", "Active mod", 0)],
        [new(@"C:\PRIVATE_USER\Game\base.scs", "/", ScsPowertrainSourceKind.Game, "base", "1.62.1.0", 0, true, "stock"),
         new(@"C:\PRIVATE_USER\Game\one.scs", "/", ScsPowertrainSourceKind.Dlc, "dlc_one", "1.62.1.0", 100, false, "unknown", DlcMetadataVerified: true),
         new(@"C:\PRIVATE_USER\Game\shared1.scs", "/", ScsPowertrainSourceKind.Dlc, "dlc_shared", "1.62.1.0", 100, false, "unknown", DlcMetadataVerified: true),
         new(@"C:\PRIVATE_USER\Game\shared2.scs", "/", ScsPowertrainSourceKind.Dlc, "dlc_shared", "1.62.1.0", 100, false, "unknown", DlcMetadataVerified: true),
         new(@"C:\PRIVATE_USER\Game\invalid.scs", "/", ScsPowertrainSourceKind.Dlc, "invalid.scs", "1.62.1.0", 100, false, "invalid metadata"),
         new(@"C:\PRIVATE_USER\mod.scs", "/", ScsPowertrainSourceKind.LocalMod, "mod_only", "1.0", 200, true, "mod"),
         new(@"C:\PRIVATE_USER\workshop.scs", "/", ScsPowertrainSourceKind.Workshop, "active_mod", "1.0", 300, true, "mod")],
        [MissingPackage with { Code = "dlc_metadata_invalid", PhysicalPath = @"C:\PRIVATE_USER\Game\invalid.scs", BlocksConfiguration = false }],
        SaveDependencies:
        [new(ScsSaveDependencyKind.Mod, "active_mod", "Active DisplayNameMustStayLocal", 0),
         new(ScsSaveDependencyKind.Mod, "old_mod", "Historical DisplayNameMustStayLocal", 1),
         new(ScsSaveDependencyKind.Dlc, "dlc_one", "One DisplayNameMustStayLocal", 0),
         new(ScsSaveDependencyKind.Rdlc, "dlc_shared", "Shared DisplayNameMustStayLocal", 0),
         new(ScsSaveDependencyKind.Dlc, "DLC_ONE", "Case DisplayNameMustStayLocal", 1),
         new(ScsSaveDependencyKind.Dlc, "mod_only", "NotDlc DisplayNameMustStayLocal", 2),
         new(ScsSaveDependencyKind.Dlc, "invalid.scs", "Invalid DisplayNameMustStayLocal", 3)]);

    [Fact]
    public async Task GlobalBlockExplainsThatDefinitionsWereNotAttemptedAndNamesPackage()
    {
        var (harness, _) = Create((request, _) => Task.FromResult<IScsTruckPowertrainPreview>(new Preview(request)
        { InspectionReport = new("1.62.1.0", false, false, [new("workshop.1234567890", "Example Truck", 0)], [], [MissingPackage]) }));
        await Loaded(harness.ViewModel);
        Assert.True(harness.ViewModel.TruckPowertrainConfigurationBlocked);
        Assert.Contains("nicht begonnen", harness.ViewModel.TruckPowertrainInspectionSummary);
        Assert.Contains("1 Paket", harness.ViewModel.TruckPowertrainInspectionSummary);
        var row = Assert.Single(harness.ViewModel.TruckPowertrainDiagnosticIssues);
        Assert.Contains("Example Truck", row.Heading);
        Assert.Contains("fehlt", row.Message);
        Assert.Contains(@"C:\Private\mod.scs", row.TechnicalText);
    }

    [Fact]
    public async Task UnusedUnknownDlcDoesNotBecomeGlobalConfigurationBlock()
    {
        var (harness, _) = Create((request, _) => Task.FromResult<IScsTruckPowertrainPreview>(new Preview(request)
        { InspectionReport = new("1.62.1.0", true, true, [], [], [MissingPackage with { BlocksConfiguration = false }]) }));
        await Loaded(harness.ViewModel);
        Assert.False(harness.ViewModel.TruckPowertrainConfigurationBlocked);
        Assert.Contains("geprüft", harness.ViewModel.TruckPowertrainInspectionSummary);
        Assert.DoesNotContain("nicht begonnen", harness.ViewModel.TruckPowertrainInspectionSummary);
    }

    [Fact]
    public async Task SkippedDefinitionCheckDoesNotBlameVerifiedActiveConfiguration()
    {
        var (harness, _) = Create((request, _) => Task.FromResult<IScsTruckPowertrainPreview>(new Preview(request)
        { InspectionReport = new("9.99.0", true, false, [], [], []) }));
        await Loaded(harness.ViewModel);
        Assert.True(harness.ViewModel.TruckPowertrainConfigurationBlocked);
        Assert.Contains("nicht begonnen", harness.ViewModel.TruckPowertrainInspectionSummary);
        Assert.DoesNotContain("aktive Konfiguration ist nicht", harness.ViewModel.TruckPowertrainInspectionSummary);
    }

    [Fact]
    public async Task InvalidatedResultSaysChecksWereDiscardedAndExportsThatState()
    {
        var (harness, _) = Create((request, _) => Task.FromResult<IScsTruckPowertrainPreview>(new Preview(request)
        { InspectionReport = new("1.62.1.0", true, true, [], [], [], ResultsInvalidated: true) }));
        var export = new DiagnosticExport { Destination = "diagnostic.json" };
        harness.ViewModel.ConfigureTruckPowertrainDiagnosticExport(export);
        await Loaded(harness.ViewModel);
        Assert.True(harness.ViewModel.TruckPowertrainConfigurationBlocked);
        Assert.Contains("Prüfergebnis verworfen", harness.ViewModel.TruckPowertrainInspectionSummary);
        Assert.Contains("bereits geprüft", harness.ViewModel.TruckPowertrainInspectionSummary);
        Assert.DoesNotContain("nicht begonnen", harness.ViewModel.TruckPowertrainInspectionSummary);
        await harness.ViewModel.ExportTruckPowertrainDiagnosticsCommand.ExecuteAsync(null);
        using var json = JsonDocument.Parse(Assert.Single(export.Writes));
        Assert.True(json.RootElement.GetProperty("resultsInvalidated").GetBoolean());
    }

    [Fact]
    public async Task GlobalAndComponentIssuesSurviveManySourceNoticesInUiAndExport()
    {
        var issues = Enumerable.Repeat(MissingPackage with { Code = "dlc_availability_unverified", BlocksConfiguration = false }, 140)
            .Concat(new[]
            {
                MissingPackage with { Code = "source_listing_failed", Scope = ScsPowertrainDiagnosticScope.Component, BlocksConfiguration = false },
                MissingPackage with { Code = "game_version_unsupported", Scope = ScsPowertrainDiagnosticScope.Global, BlocksConfiguration = false }
            }).ToArray();
        var (harness, _) = Create((request, _) => Task.FromResult<IScsTruckPowertrainPreview>(new Preview(request)
        { InspectionReport = new("9.99.0", true, false, [], [], issues) }));
        var export = new DiagnosticExport { Destination = "diagnostic.json" };
        harness.ViewModel.ConfigureTruckPowertrainDiagnosticExport(export);
        await Loaded(harness.ViewModel);
        Assert.Contains("game_version_unsupported", harness.ViewModel.TruckPowertrainDiagnosticIssues[0].TechnicalText);
        Assert.Contains("source_listing_failed", harness.ViewModel.TruckPowertrainDiagnosticIssues[1].TechnicalText);
        await harness.ViewModel.ExportTruckPowertrainDiagnosticsCommand.ExecuteAsync(null);
        using var json = JsonDocument.Parse(Assert.Single(export.Writes));
        var exported = json.RootElement.GetProperty("issues");
        Assert.Equal("game_version_unsupported", exported[0].GetProperty("code").GetString());
        Assert.Equal("source_listing_failed", exported[1].GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExportRequiresExplicitCommandAndCancelDoesNotWrite()
    {
        var (harness, _) = Create();
        var export = new DiagnosticExport();
        harness.ViewModel.ConfigureTruckPowertrainDiagnosticExport(export);
        await Loaded(harness.ViewModel);
        Assert.Equal(0, export.DialogCalls);
        Assert.Empty(export.Writes);
        await harness.ViewModel.ExportTruckPowertrainDiagnosticsCommand.ExecuteAsync(null);
        Assert.Equal(1, export.DialogCalls);
        Assert.Empty(export.Writes);
    }

    [Fact]
    public async Task ExportWritesWhitelistedDetachedReportAfterDialog()
    {
        var (harness, _) = Create();
        var export = new DiagnosticExport { Destination = "diagnostic.json" };
        harness.ViewModel.ConfigureTruckPowertrainDiagnosticExport(export);
        await Loaded(harness.ViewModel);
        await harness.ViewModel.ExportTruckPowertrainDiagnosticsCommand.ExecuteAsync(null);
        var written = Assert.Single(export.Writes);
        using var json = JsonDocument.Parse(written);
        Assert.Equal("Ets2", json.RootElement.GetProperty("game").GetString());
        Assert.False(json.RootElement.TryGetProperty("saveSha256", out _));
        Assert.DoesNotContain("synthetic-home", written);
        Assert.DoesNotContain("synthetic-save", written);
        var pair = json.RootElement.GetProperty("selectedPair");
        Assert.Equal(harness.ViewModel.TruckPowertrainSelectedEngine!.DataPath, pair.GetProperty("engine").GetString());
        Assert.Equal(harness.ViewModel.TruckPowertrainSelectedTransmission!.DataPath, pair.GetProperty("transmission").GetString());
        Assert.True(pair.TryGetProperty("verified", out _));
    }

    [Fact]
    public async Task InvalidExportDataShowsLocalErrorWithoutWriting()
    {
        var (harness, _) = Create();
        var export = new DiagnosticExport { Destination = "diagnostic.json",
            OnWrite = _ => throw new InvalidDataException("private exception details") };
        harness.ViewModel.ConfigureTruckPowertrainDiagnosticExport(export);
        await Loaded(harness.ViewModel);
        await harness.ViewModel.ExportTruckPowertrainDiagnosticsCommand.ExecuteAsync(null);
        Assert.Empty(export.Writes);
        Assert.Contains("konnte nicht gespeichert", harness.ViewModel.TruckPowertrainExportStatus);
        Assert.DoesNotContain("private exception", harness.ViewModel.TruckPowertrainExportStatus);
    }

    [Fact]
    public async Task ChangedContextDuringExportDialogDoesNotWrite()
    {
        var (harness, _) = Create();
        var export = new DiagnosticExport { Destination = "diagnostic.json", OnDialog = () => harness.ViewModel.IsBusy = true };
        harness.ViewModel.ConfigureTruckPowertrainDiagnosticExport(export);
        await Loaded(harness.ViewModel);
        await harness.ViewModel.ExportTruckPowertrainDiagnosticsCommand.ExecuteAsync(null);
        Assert.Empty(export.Writes);
        Assert.False(harness.ViewModel.ExportTruckPowertrainDiagnosticsCommand.CanExecute(null));
    }

    [Fact]
    public async Task PendingInspectionDisablesExportAndContextChangeCancelsPendingWrite()
    {
        var completion = new TaskCompletionSource<IScsTruckPowertrainPreview>();
        var (harness, service) = Create((_, _) => completion.Task);
        var export = new DiagnosticExport { Destination = "diagnostic.json" };
        harness.ViewModel.ConfigureTruckPowertrainDiagnosticExport(export);
        for (var i = 0; i < 100 && service.Requests.IsEmpty; i++) await Task.Delay(10);
        Assert.False(harness.ViewModel.ExportTruckPowertrainDiagnosticsCommand.CanExecute(null));
        completion.SetResult(new Preview(service.Requests.Last()));
        await Loaded(harness.ViewModel);
        var writing = new TaskCompletionSource();
        export.OnWrite = async token => { writing.SetResult(); await Task.Delay(Timeout.Infinite, token); };
        var command = harness.ViewModel.ExportTruckPowertrainDiagnosticsCommand.ExecuteAsync(null);
        await writing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        harness.ViewModel.IsBusy = true;
        await command;
        Assert.True(export.LastToken.IsCancellationRequested);
    }

    [Fact]
    public void ExportOmitsRawStringsSecretsAndPhysicalPathsButKeepsUsefulCodes()
    {
        const string secret = "secret_key=NEVER_EXPORT_ME";
        var request = new ScsEffectiveContentRequest(SaveEditorTestHarness.Profile("PRIVATE_PROFILE"),
            SaveEditorTestHarness.Save("PRIVATE_SAVE"), @"C:\PRIVATE_USER\Game",
            [new(GameType.Ets2, "PRIVATE_MODSET", @"C:\PRIVATE_USER\Mods")]);
        var preview = new Preview(request) { InspectionReport = new("1.62.1.0", false, false,
            [new("76561198000000000", "C:\\PRIVATE_USER\\Profile", 0), new("workshop.1234567890", "Example Truck", 1)],
            [new(@"C:\PRIVATE_USER\Game\def.scs", "/", ScsPowertrainSourceKind.Game, "base", "1.62.1.0", 0, true, secret)],
            [MissingPackage with { Message = secret, TechnicalDetails = secret, PhysicalPath = @"C:\PRIVATE_USER\Mods\PRIVATE_FILE.scs" },
             MissingPackage with { Code = secret, DisplayName = secret, PackageIdentity = "76561198000000000", PackageVersion = secret,
                 VirtualPath = "/home/profiles/PRIVATE_PROFILE/game.sii" }]) };
        var report = TruckPowertrainDiagnosticReport.Create(request, preview);
        Assert.DoesNotContain("PRIVATE", report);
        Assert.DoesNotContain("NEVER_EXPORT", report);
        Assert.DoesNotContain("secret_key", report);
        Assert.DoesNotContain("76561198000000000", report);
        Assert.Contains("active_package_missing", report);
        Assert.Contains("Example Truck", report);
        Assert.Contains("GameInstall", report);
        Assert.Contains("ModRoot1", report);
        Assert.Contains("/def/vehicle/truck/test/", report);
    }

    [Fact]
    public void ExportBoundsLargeInputsAndRejectsEncodedTraversalAndRootNames()
    {
        var request = new ScsEffectiveContentRequest(SaveEditorTestHarness.Profile("private"), SaveEditorTestHarness.Save("private"), null);
        var issue = MissingPackage with { DisplayName = new string('X', 10000), VirtualPath = "/def/vehicle/truck/%2e%2e/secret.sii" };
        var preview = new Preview(request) { InspectionReport = new("not-a-version", false, false,
            [], [], Enumerable.Repeat(issue, 10000).ToArray()) };
        var report = TruckPowertrainDiagnosticReport.Create(request, preview);
        Assert.True(report.Length < 262144);
        Assert.DoesNotContain("%2e", report);
        Assert.DoesNotContain("not-a-version", report);
        using var json = JsonDocument.Parse(report);
        Assert.True(json.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void ExportPrioritizesBlockersAndRejectsSecretVersionSuffix()
    {
        var request = new ScsEffectiveContentRequest(SaveEditorTestHarness.Profile("private"), SaveEditorTestHarness.Save("private"), null);
        var preview = new Preview(request) { InspectionReport = new("1.62.1.0s", false, false,
            [], [], Enumerable.Repeat(MissingPackage with { Code = "dlc_availability_unverified", BlocksConfiguration = false }, 140)
                .Append(MissingPackage with { Code = "source_snapshot_invalid", PackageVersion = "1.2MySecret" }).ToArray()) };
        var report = TruckPowertrainDiagnosticReport.Create(request, preview);
        Assert.Contains("source_snapshot_invalid", report);
        Assert.DoesNotContain("MySecret", report);
        Assert.Contains("1.62.1.0s", report);
        using var json = JsonDocument.Parse(report);
        Assert.Equal("source_snapshot_invalid", json.RootElement.GetProperty("issues")[0].GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("ABCDEF012345")]
    [InlineData("secret_key")]
    public void ExportOmitsAccountOrProfileLikePackageLabelsAndVirtualPaths(string sensitiveValue)
    {
        var request = new ScsEffectiveContentRequest(SaveEditorTestHarness.Profile("private"), SaveEditorTestHarness.Save("private"), null);
        var issue = MissingPackage with { PackageIdentity = sensitiveValue, DisplayName = sensitiveValue,
            VirtualPath = Model + "engine/" + sensitiveValue + ".sii" };
        var preview = new Preview(request) { InspectionReport = new("1.62.1.0", false, false, [], [], [issue]) };
        Assert.DoesNotContain(sensitiveValue, TruckPowertrainDiagnosticReport.Create(request, preview));
    }

    [Fact]
    public void SelectedPairExportUsesWhitelistWithoutRawReasonsOrExceptions()
    {
        var request = new ScsEffectiveContentRequest(SaveEditorTestHarness.Profile("private"), SaveEditorTestHarness.Save("private"), null);
        var issue = MissingPackage with { Code = "package_path_invalid", Message = "DO_NOT_EXPORT", TechnicalDetails = "DO_NOT_EXPORT",
            PhysicalPath = @"C:\DO_NOT_EXPORT\package.scs" };
        var pair = new ScsTruckPowertrainAssessment(Model + "engine/current.sii", Model + "transmission/current.sii", false,
            ["DO_NOT_EXPORT"], [issue]);
        var report = TruckPowertrainDiagnosticReport.Create(request, new Preview(request), pair);
        Assert.DoesNotContain("DO_NOT_EXPORT", report);
        using var json = JsonDocument.Parse(report);
        var selected = json.RootElement.GetProperty("selectedPair");
        Assert.False(selected.GetProperty("verified").GetBoolean());
        Assert.Equal(1, selected.GetProperty("issueCount").GetInt32());
        Assert.Equal("package_path_invalid", selected.GetProperty("issues")[0].GetProperty("code").GetString());
    }

    [Fact]
    public void ComponentDiagnosticRetainsSourceContextWithoutChangingSelectability()
    {
        var issue = MissingPackage with { Stage = ScsPowertrainDiagnosticStage.EffectiveDefinition,
            Scope = ScsPowertrainDiagnosticScope.Component, BlocksConfiguration = false,
            VirtualPath = Model + "engine/current.sii" };
        var row = new NmcScsLauncher.App.ViewModels.TruckPowertrainOptionViewModel(new(ScsPowertrainKind.Engine,
            issue.VirtualPath, "Current", true, "Example Truck", "", false, ["Include fehlt."], [issue]));
        Assert.False(row.IsSelectable);
        Assert.Contains("Include fehlt", row.ReasonText);
        Assert.Contains("Example Truck", row.DiagnosticText);
        Assert.Contains(@"C:\Private\mod.scs", row.DiagnosticText);
    }

    [Fact]
    public async Task LocalExportWriterHonorsCancellationBeforeCreatingFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nmc-diagnostics-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new TruckPowertrainDiagnosticExportService();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.WriteAsync(Path.Combine(directory, "report.json"), "{}", cancellation.Token));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory); }
    }

    [Fact]
    public Task DiagnosticViewBindsSummaryIssuesAndExplicitExport() => OnStaThread(async () =>
    {
        var (harness, _) = Create();
        harness.ViewModel.ConfigureTruckPowertrainDiagnosticExport(new DiagnosticExport());
        await Loaded(harness.ViewModel);
        var view = new TruckPowertrainSelection { DataContext = harness.ViewModel };
        Assert.IsType<TextBlock>(view.FindName("InspectionSummary"));
        Assert.IsType<ItemsControl>(view.FindName("DiagnosticIssues"));
        var button = Assert.IsType<Button>(view.FindName("ExportDiagnostics"));
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        Assert.Same(harness.ViewModel.ExportTruckPowertrainDiagnosticsCommand, button.Command);
    });

    private sealed class DiagnosticExport : ITruckPowertrainDiagnosticExportService
    {
        public string? Destination { get; set; }
        public int DialogCalls { get; private set; }
        public Action? OnDialog { get; set; }
        public Func<CancellationToken, Task>? OnWrite { get; set; }
        public CancellationToken LastToken { get; private set; }
        public List<string> Writes { get; } = [];
        public string? SelectDestination(GameType game) { DialogCalls++; OnDialog?.Invoke(); return Destination; }
        public async Task WriteAsync(string destination, string report, CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            if (OnWrite is not null) await OnWrite(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Writes.Add(report);
        }
    }
}
