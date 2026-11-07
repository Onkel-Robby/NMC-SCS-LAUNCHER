using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed partial class ScsTruckPowertrainRulesTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HistoricalDependenciesRemainDetachedDiagnosticEvidenceWithoutGrantingAvailability(bool verified)
    {
        using var source = Source();
        var dependencies = new List<ScsSaveDependency> { new(ScsSaveDependencyKind.Dlc, "ats_oversize", "DLC - Special Transport", 0),
            new(ScsSaveDependencyKind.Rdlc, "ats_toys", "DLC - Cabin Accessories", 1) };
        source.Evidence = source.Evidence with { ConfigurationVerified = verified, SaveDependencies = dependencies };
        var preview = Build(source);
        dependencies.Clear();
        Assert.Equal(2, preview.InspectionReport!.SaveDependencies!.Count);
        Assert.Equal(ScsSaveDependencyKind.Dlc, preview.InspectionReport.SaveDependencies[0].Kind);
        Assert.Equal(ScsSaveDependencyKind.Rdlc, preview.InspectionReport.SaveDependencies[1].Kind);
        Assert.Equal(verified, preview.AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void MissingActivePackageReportsNotAttemptedInsteadOfComponentFailure()
    {
        using var source = Source();
        var issue = new ScsPowertrainDiagnostic("workshop_package_missing", ScsPowertrainDiagnosticStage.ActiveConfiguration,
            ScsPowertrainDiagnosticScope.Source, "Aktives Workshop-Paket fehlt.", "3088384046", "Testmod", BlocksConfiguration: true);
        source.Evidence = source.Evidence with { ConfigurationVerified = false, DiagnosticDetails = [issue] };
        var preview = Build(source);
        var report = Assert.IsType<ScsTruckPowertrainInspectionReport>(preview.InspectionReport);
        Assert.False(report.ConfigurationVerified);
        Assert.False(report.DefinitionsAttempted);
        Assert.Contains(issue, report.Issues);
        Assert.Equal(0, source.Reads);
        Assert.Contains("Noch nicht geprüft", Assert.Single(preview.Engines).Reasons.Single());
        Assert.False(preview.AssessPair(Engine, Gearbox).IsVerified);
    }

    [Fact]
    public void UnknownVersionAfterSourceFailureDoesNotInventUnsupportedSchemaDiagnosis()
    {
        using var source = Source();
        source.Evidence = source.Evidence with { ConfigurationVerified = false, GameVersion = "" };
        var preview = Build(source);
        Assert.DoesNotContain(preview.Diagnostics, value => value.Contains("außerhalb des geprüften"));
        Assert.False(preview.InspectionReport!.DefinitionsAttempted);
        Assert.Null(preview.InspectionReport.SaveDependencies);
    }

    [Fact]
    public void UnsupportedKnownVersionStopsBeforeDefinitionReads()
    {
        using var source = Source();
        source.Evidence = source.Evidence with { GameVersion = "1.99.0.0" };
        var preview = Build(source);
        Assert.Equal(0, source.Reads);
        Assert.False(preview.InspectionReport!.DefinitionsAttempted);
        Assert.Contains(preview.InspectionReport.Issues, issue => issue.Code == "game_version_unsupported");
    }

    [Fact]
    public void ReferencedUnavailableIncludeRetainsPackageAndVirtualPath()
    {
        using var source = Source();
        const string include = "/def/test/engine.sui";
        source.Files[NewEngine] = "@include \"" + include + "\"";
        source.Set(include, "accessory_engine_data", "new.test.engine", "torque: 2400");
        source.Unavailable.Add(include);
        var preview = Build(source);
        var option = Assert.Single(preview.Engines, option => option.DataPath == NewEngine);
        var issue = Assert.Single(option.DiagnosticDetails!);
        Assert.Equal("definition_availability_unverified", issue.Code);
        Assert.Equal(include, issue.VirtualPath);
        Assert.Equal("fixture", issue.PackageIdentity);
        Assert.Equal("fixture", issue.PhysicalPath);
        Assert.False(option.DefinitionVerified);
        Assert.True(preview.InspectionReport!.DefinitionsAttempted);
        Assert.Contains(issue, preview.InspectionReport.Issues);
        Assert.False(preview.AssessPair(NewEngine, NewGearbox).IsVerified);
    }

    [Fact]
    public void MissingIncludeIsAttributedToActualMissingPath()
    {
        using var source = Source();
        source.Files[NewEngine] = "@include \"/def/missing.sui\"";
        var option = Assert.Single(Build(source).Engines, option => option.DataPath == NewEngine);
        var issue = Assert.Single(option.DiagnosticDetails!);
        Assert.Equal("definition_missing", issue.Code);
        Assert.Equal("/def/missing.sui", issue.VirtualPath);
        Assert.Equal("fixture", issue.PackageIdentity);
        Assert.Contains(NewEngine, issue.TechnicalDetails);
    }

    [Theory]
    [InlineData(GameType.Ets2)]
    [InlineData(GameType.Ats)]
    public void VerifiedBaseConfigurationExposesCandidatesAndPairStatusSeparately(GameType game)
    {
        using var source = Source();
        source.Evidence = source.Evidence with { Game = game, DiagnosticDetails = [new("dlc_availability_unverified",
            ScsPowertrainDiagnosticStage.ArchiveDiscovery, ScsPowertrainDiagnosticScope.Source, "Ungenutztes DLC unbestätigt.")] };
        var preview = ScsTruckPowertrainRules.Build(Snapshot() with { Game = game }, source);
        Assert.True(preview.InspectionReport!.ConfigurationVerified);
        Assert.True(preview.InspectionReport.DefinitionsAttempted);
        Assert.Equal(2, preview.Engines.Count);
        Assert.Equal(2, preview.Transmissions.Count);
        Assert.True(preview.AssessPair(NewEngine, NewGearbox).IsVerified);
        Assert.DoesNotContain(preview.InspectionReport.Issues, issue => issue.BlocksConfiguration);
    }
}
