using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsActiveContentResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NmcActiveContent", Guid.NewGuid().ToString("N"));
    private readonly ScsEffectiveContentRequest _request;
    private readonly GameInstallation _installation;
    private readonly string _modRoot;
    private readonly string _infoPath;
    private WorkshopContentSnapshot _workshop = new(GameType.Ets2, [], [], []);
    private const string Definition = "def/vehicle/truck/test/engine/a.sii";
    private const string Manifest = "SiiNunit { mod_package : .package { package_version: \"1.0\" } }";

    public ScsActiveContentResolverTests()
    {
        var home = DirectoryFor("home");
        var profile = DirectoryFor("home/profiles/TEST");
        var save = DirectoryFor("home/profiles/TEST/save/1");
        _modRoot = DirectoryFor("home/mod");
        _infoPath = Path.Combine(save, "info.sii");
        File.WriteAllText(Path.Combine(save, "game.sii"), "SiiNunit { vehicle : .truck { } }");
        var profileReference = new ScsProfileReference(GameType.Ets2, ScsProfileStorageKind.Local, home, profile, "TEST", "Test", Path.Combine(profile, "profile.sii"));
        var saveReference = new ScsSaveReference(GameType.Ets2, profile, save, "1", "Test", ScsSaveKind.Manual, Path.Combine(save, "game.sii"), DateTimeOffset.UtcNow);
        _request = new(profileReference, saveReference, null);
        WriteMods();
        var install = DirectoryFor("install");
        var executable = Path.Combine(DirectoryFor("install/bin/win_x64"), "eurotrucks2.exe");
        File.WriteAllText(executable, "synthetic executable; version injected");
        _installation = new(GameType.Ets2, install, executable, GameInstallationSource.ManualSelection);
        Zip(install, "version.scs", ("version.sii", "SiiNunit { fs_pack_set : .version { application: eut2 version: \"1.62.1.0\" } }"));
        Zip(install, "def.scs", (Definition, "SiiNunit { accessory_engine_data : .engine { name: \"Base\" } }"));
    }

    [Fact]
    public async Task NoModsUsesOnlyValidatedGameAndHashesMetadataWithoutReadingGameSave()
    {
        var result = await Service().ResolveAsync(_request);
        Assert.True(result.ConfigurationVerified, string.Join(";", result.Diagnostics));
        Assert.Equal("1.62.1.0", result.GameVersion);
        Assert.Empty(result.ActiveMods);
        Assert.All(result.Layers, layer => { Assert.Equal(0, layer.Priority); Assert.True(layer.AvailabilityVerified); });
        Assert.Contains(result.Files, file => file.Path == _request.Profile.ProfileSiiPath && file.Sha256 == Hash(file.Path));
        Assert.Contains(result.Files, file => file.Path == _infoPath && file.Sha256 == Hash(file.Path));
        Assert.Contains(result.Files, file => file.VirtualPath == "/version.sii" && file.Sha256.Length == 64);
        Assert.DoesNotContain(result.Files, file => file.Path == _request.Save.GameSiiPath);
        Assert.Contains(result.Files, file => Path.GetFileName(file.Path) == "def.scs" && file.Sha256.Length == 0);
    }

    [Fact]
    public async Task ExactIdentityPreservesSpacesAndActiveOrderAndExcludesInactivePackages()
    {
        WriteMods("lower", "with trailing ");
        Package(_modRoot, "lower.scs");
        Package(_modRoot, "with trailing .scs");
        Package(_modRoot, "inactive.scs");
        var result = await Service().ResolveAsync(_request);
        Assert.True(result.ConfigurationVerified, string.Join(";", result.Diagnostics));
        var mods = result.Layers.Where(layer => layer.Kind == ScsPowertrainSourceKind.LocalMod).ToArray();
        Assert.Equal(["lower", "with trailing "], mods.Select(layer => layer.PackageIdentity));
        Assert.Equal([1000, 1010], mods.Select(layer => layer.Priority));
        Assert.DoesNotContain(result.Layers, layer => layer.PackageIdentity == "inactive");
        Assert.Contains(result.Files, file => file.VirtualPath == "/manifest.sii" && file.Sha256.Length == 64);
    }

    [Fact]
    public async Task HistoricalModOrderMustAgreeWithCurrentProfile()
    {
        WriteMods("first", "second");
        WriteInfo("second", "first");
        Package(_modRoot, "first.scs");
        Package(_modRoot, "second.scs");
        var result = await Service().ResolveAsync(_request);
        Assert.False(result.ConfigurationVerified);
        Assert.Contains(result.Diagnostics, message => message.Contains("Reihenfolge"));
    }

    [Theory]
    [InlineData("game")]
    [InlineData("profile")]
    [InlineData("save")]
    public async Task SelectionMustReferToSameGameProfileAndDirectSaveDirectory(string mismatch)
    {
        var save = mismatch switch
        {
            "game" => _request.Save with { GameType = GameType.Ats },
            "profile" => _request.Save with { ProfileDirectory = DirectoryFor("another-profile") },
            _ => _request.Save with { SaveDirectoryName = "2" }
        };
        Assert.False((await Service().ResolveAsync(_request with { Save = save })).ConfigurationVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrAmbiguousLocalPackageBlocksConfiguration(bool ambiguous)
    {
        WriteMods("active");
        if (ambiguous) { Package(_modRoot, "active.zip"); Directory.CreateDirectory(Path.Combine(_modRoot, "active")); }
        var result = await Service().ResolveAsync(_request);
        Assert.False(result.ConfigurationVerified);
        Assert.DoesNotContain(result.Layers, layer => layer.Kind == ScsPowertrainSourceKind.LocalMod);
    }

    [Fact]
    public async Task LocalPackageWithoutManifestPreservesUnknownVersionAndActualDefinitions()
    {
        WriteMods("legacy");
        Zip(_modRoot, "legacy.zip", (Definition, "legacy definition"));
        using var session = await new ScsEffectiveContentService(Service()).OpenAsync(_request);
        Assert.True(session.Evidence.ConfigurationVerified, string.Join(";", session.Evidence.Diagnostics));
        var layer = Assert.Single(session.Evidence.Layers, value => value.Kind == ScsPowertrainSourceKind.LocalMod);
        Assert.Equal(string.Empty, layer.PackageVersion);
        Assert.True(layer.AvailabilityVerified);
        Assert.Equal("legacy definition", session.ReadDefinition("/" + Definition).Content);
        await session.VerifyUnchangedAsync();
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("duplicate")]
    [InlineData("workshop")]
    [InlineData("casevariant")]
    public async Task AbsentLocalManifestSupportDoesNotHideInvalidOrRequiredMetadata(string failure)
    {
        string path;
        if (failure == "workshop")
        {
            var root = WorkshopRoot(); WriteVersions(root, ("current", null));
            Zip(root, "current.zip", (Definition, "component")); path = Path.Combine(root, "current.zip");
        }
        else
        {
            WriteMods("active"); path = Path.Combine(_modRoot, "active.zip");
            if (failure == "malformed") Zip(_modRoot, "active.zip", ("manifest.sii", "SiiNunit { incomplete"));
            else if (failure == "casevariant") Zip(_modRoot, "active.zip", ("Manifest.sii", "SiiNunit { mod_package : .p { dlc_dependencies[]: \"dlc_required\" } }"));
            else Zip(_modRoot, "active.zip", ("manifest.sii", Manifest), ("base/manifest.sii", Manifest));
        }
        var result = await Service().ResolveAsync(_request);
        Assert.False(result.ConfigurationVerified);
        var issue = Assert.Single(result.DiagnosticDetails!, item => item.BlocksConfiguration);
        Assert.Equal(failure is "malformed" or "casevariant" ? "manifest_invalid" : failure == "duplicate" ? "manifest_ambiguous" : "manifest_missing", issue.Code);
        Assert.Equal(path, issue.PhysicalPath);
        Assert.DoesNotContain(result.Layers, layer => layer.Kind is ScsPowertrainSourceKind.LocalMod or ScsPowertrainSourceKind.Workshop);
    }

    [Fact]
    public async Task UniqueLocalScsWinsOverSameStemZipWithoutReadingTheShadowedPackage()
    {
        WriteMods("active");
        Package(_modRoot, "active.scs"); File.WriteAllText(Path.Combine(_modRoot, "active.zip"), "unreadable shadowed archive");
        var result = await Service().ResolveAsync(_request);
        Assert.True(result.ConfigurationVerified, string.Join(";", result.Diagnostics));
        Assert.Equal(Path.Combine(_modRoot, "active.scs"), Assert.Single(result.Layers, layer => layer.Kind == ScsPowertrainSourceKind.LocalMod).PhysicalPath);
    }

    [Fact]
    public async Task LocalScsPrecedenceDoesNotResolveAnAdditionalDirectoryOrWorkshopDuplicate()
    {
        WriteMods("active"); Package(_modRoot, "active.scs"); Package(_modRoot, "active.zip");
        Directory.CreateDirectory(Path.Combine(_modRoot, "active"));
        var local = await Service().ResolveAsync(_request);
        Assert.False(local.ConfigurationVerified);
        Assert.Contains(local.DiagnosticDetails!, issue => issue.Code == "package_ambiguous" && issue.PhysicalPath == _modRoot);
        var root = WorkshopRoot(); WriteVersions(root, ("current", null)); Package(root, "current.scs"); Package(root, "current.zip");
        var workshop = await Service().ResolveAsync(_request);
        Assert.False(workshop.ConfigurationVerified);
        Assert.Contains(workshop.DiagnosticDetails!, issue => issue.Code == "package_ambiguous" && issue.PackageVersion == "current");
    }

    [Fact]
    public async Task ChangedLocalPrecedenceIsDetectedByFreshSessionRevalidation()
    {
        WriteMods("active"); Package(_modRoot, "active.zip");
        using var session = await new ScsEffectiveContentService(Service()).OpenAsync(_request);
        Assert.True(session.Evidence.ConfigurationVerified);
        Package(_modRoot, "active.scs");
        await Assert.ThrowsAsync<ScsSaveEditException>(() => session.VerifyUnchangedAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkshopMissingAndDuplicateRootsHaveDifferentContextualIssues(bool duplicate)
    {
        var root = WorkshopRoot();
        if (duplicate)
        {
            var other = DirectoryFor("second/steamapps/workshop/content/227300/123");
            _workshop = _workshop with { ScannedContentRoots = [Directory.GetParent(root)!.FullName, Directory.GetParent(other)!.FullName] };
        }
        else { Directory.Delete(root); _workshop = _workshop with { Items = [] }; }
        var result = await Service().ResolveAsync(_request);
        Assert.False(result.ConfigurationVerified);
        var issue = Assert.Single(result.DiagnosticDetails!, item => item.BlocksConfiguration);
        Assert.Equal(duplicate ? "workshop_package_ambiguous" : "workshop_package_missing", issue.Code);
        Assert.Equal("mod_workshop_package.000000000000007B", issue.PackageIdentity);
        Assert.Equal("Display 0", issue.DisplayName);
        Assert.NotNull(issue.PhysicalPath);
        Assert.Equal(ScsPowertrainDiagnosticScope.Source, issue.Scope);
        Assert.Equal(ScsPowertrainDiagnosticStage.ActiveConfiguration, issue.Stage);
    }

    [Theory]
    [InlineData("missing-file", "workshop_versions_invalid")]
    [InlineData("no-match", "workshop_version_missing")]
    [InlineData("multiple", "workshop_version_ambiguous")]
    [InlineData("package-missing", "package_missing")]
    public async Task WorkshopVersionFailuresRetainSelectedPackageAndVersionTableContext(string scenario, string code)
    {
        var root = WorkshopRoot();
        if (scenario == "no-match") WriteVersions(root, ("old", "1.40.*"));
        if (scenario == "multiple") WriteVersions(root, ("one", null), ("two", null));
        if (scenario == "package-missing") WriteVersions(root, ("current", "1.62.*"));
        var result = await Service().ResolveAsync(_request);
        var issue = Assert.Single(result.DiagnosticDetails!, item => item.BlocksConfiguration);
        Assert.Equal(code, issue.Code);
        Assert.Equal(scenario == "package-missing" ? root : Path.Combine(root, "versions.sii"), issue.PhysicalPath);
        if (scenario == "package-missing") Assert.Equal("current", issue.PackageVersion);
        Assert.DoesNotContain(result.Layers, layer => layer.Kind == ScsPowertrainSourceKind.Workshop);
    }

    [Fact]
    public async Task HighWorkshopIdUsesUnsignedConversionAndExactGameRoot()
    {
        const ulong id = 0xf0000000ffffffff;
        WriteMods($"mod_workshop_package.{id:X16}");
        var root = DirectoryFor($"library/steamapps/workshop/content/227300/{id}");
        _workshop = new(GameType.Ets2, [new(GameType.Ets2, id, root, _root, DateTimeOffset.UtcNow)], [Directory.GetParent(root)!.FullName], []);
        WriteVersions(root, ("universal", null)); Package(root, "universal.zip");
        var result = await Service().ResolveAsync(_request);
        Assert.True(result.ConfigurationVerified, string.Join(";", result.Diagnostics));
        Assert.Equal($"mod_workshop_package.{id:X16}", Assert.Single(result.Layers, layer => layer.Kind == ScsPowertrainSourceKind.Workshop).PackageIdentity);
    }

    [Fact]
    public async Task UnreadableActiveArchiveSurvivesAsTypedIssueWithoutInventingLayer()
    {
        WriteMods("active"); var path = Path.Combine(_modRoot, "active.scs"); File.WriteAllText(path, "AEM!unsupported synthetic content");
        var result = await Service().ResolveAsync(_request);
        var issue = Assert.Single(result.DiagnosticDetails!, item => item.BlocksConfiguration);
        Assert.Equal("package_archive_unreadable", issue.Code);
        Assert.Equal("active", issue.PackageIdentity); Assert.Equal("Display 0", issue.DisplayName); Assert.Equal(path, issue.PhysicalPath);
        Assert.DoesNotContain(result.Layers, layer => layer.PackageIdentity == "active");
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task ProfileAndInstallationFailuresAlwaysHaveBlockingTypedDiagnostics()
    {
        var mismatch = await Service().ResolveAsync(_request with { Save = _request.Save with { GameType = GameType.Ats } });
        Assert.Contains(mismatch.DiagnosticDetails!, issue => issue.BlocksConfiguration && issue.Code == "profile_selection_invalid");
        var installation = await Service("1.61.0.0").ResolveAsync(_request);
        var issue = Assert.Single(installation.DiagnosticDetails!, item => item.BlocksConfiguration && item.Code == "installation_unverified");
        Assert.Equal(Path.Combine(_installation.InstallPath, "version.scs"), issue.PhysicalPath);
        Assert.Equal("/version.sii", issue.VirtualPath);
    }

    [Fact]
    public async Task InvalidActiveModArrayPointsToProfileMetadataInsteadOfSaveInfo()
    {
        File.WriteAllText(_request.Profile.ProfileSiiPath, "SiiNunit { user_profile : .profile { active_mods: unknown } }");
        var result = await Service().ResolveAsync(_request);
        var issue = Assert.Single(result.DiagnosticDetails!, item => item.BlocksConfiguration);
        Assert.Equal("profile_metadata_invalid", issue.Code);
        Assert.Equal(_request.Profile.ProfileSiiPath, issue.PhysicalPath);
    }

    [Fact]
    public async Task DeletedWorkshopItemIsReportedMissingAtWorkshopRootInsteadOfLocalModRoot()
    {
        var root = WorkshopRoot(); Directory.Delete(root);
        var result = await Service().ResolveAsync(_request);
        var issue = Assert.Single(result.DiagnosticDetails!, item => item.BlocksConfiguration);
        Assert.Equal("workshop_package_missing", issue.Code);
        Assert.Equal(Directory.GetParent(root)!.FullName, issue.PhysicalPath);
    }

    [Fact]
    public async Task WorkshopInventoryItemMustUseTheExactGameAndPublishedIdDirectory()
    {
        WriteMods("mod_workshop_package.000000000000007B");
        var wrong = DirectoryFor("library/steamapps/workshop/content/270880/123");
        WriteVersions(wrong, ("universal", null)); Package(wrong, "universal.zip");
        _workshop = new(GameType.Ets2, [new(GameType.Ets2, 123, wrong, _root, DateTimeOffset.UtcNow)], [], []);
        var result = await Service().ResolveAsync(_request);
        Assert.False(result.ConfigurationVerified);
        Assert.Contains(result.DiagnosticDetails!, issue => issue.Code == "workshop_root_wrong_game" && issue.PhysicalPath == wrong);
        Assert.DoesNotContain(result.Layers, layer => layer.Kind == ScsPowertrainSourceKind.Workshop);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReparseCandidateKeepsExactRejectedPathAndSelectedWorkshopVariant(bool workshop)
    {
        var parent = _modRoot;
        var name = "active";
        WriteMods(name);
        if (workshop) { parent = WorkshopRoot(); name = "current"; WriteVersions(parent, (name, null)); }
        var target = DirectoryFor("synthetic-link-target");
        File.WriteAllText(Path.Combine(target, "manifest.sii"), Manifest);
        var link = Path.Combine(parent, name);
        if (OperatingSystem.IsWindows())
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/c mklink /J \"{link}\" \"{target}\"", CreateNoWindow = true, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            });
            Assert.NotNull(process); process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, target);
        try
        {
            var result = await Service().ResolveAsync(_request);
            Assert.False(result.ConfigurationVerified);
            var issue = Assert.Single(result.DiagnosticDetails!, item => item.BlocksConfiguration);
            Assert.Equal("package_path_invalid", issue.Code);
            Assert.Equal(link, issue.PhysicalPath);
            Assert.Equal(workshop ? "current" : null, issue.PackageVersion);
            Assert.DoesNotContain(result.Layers, layer => layer.Kind is ScsPowertrainSourceKind.Workshop or ScsPowertrainSourceKind.LocalMod);
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task DlcAvailabilityIssuesDoNotBlockUnrelatedConfiguration()
    {
        Zip(_installation.InstallPath, "dlc_synthetic.scs", ("dlc_synthetic.manifest.sii", "SiiNunit { dlc_package : .d { id: eut2_test game_id: eut2 } }"));
        var result = await Service().ResolveAsync(_request);
        Assert.True(result.ConfigurationVerified);
        var issue = Assert.Single(result.DiagnosticDetails!, item => item.PackageIdentity == "eut2_test");
        Assert.Equal("dlc_availability_unverified", issue.Code); Assert.False(issue.BlocksConfiguration);
    }

    [Fact]
    public async Task ManyUncertainDlcArchivesCannotHideBlockingPackageDiagnostics()
    {
        WriteMods("missing_a", "missing_b", "missing_c", "missing_d", "missing_e");
        for (var index = 0; index < 510; index++)
            Zip(_installation.InstallPath, $"dlc_test_{index}.scs", ("unexpected.sii", "no DLC metadata"));
        var result = await Service().ResolveAsync(_request);
        Assert.False(result.ConfigurationVerified);
        Assert.Equal(5, result.DiagnosticDetails!.Count(issue => issue.BlocksConfiguration && issue.Code == "package_missing"));
        Assert.InRange(result.DiagnosticDetails!.Count, 5, 1024);
    }

    [Fact]
    public async Task DistinctLocalRootsAreAmbiguousButEquivalentRootsAreDeduplicated()
    {
        WriteMods("active");
        Package(_modRoot, "active.scs");
        var ambiguous = _request with { ModDirectories = [new(GameType.Ets2, "A", _modRoot), new(GameType.Ets2, "B", DirectoryFor("other-mods"))] };
        Assert.False((await Service().ResolveAsync(ambiguous)).ConfigurationVerified);
        var same = _request with { ModDirectories = [new(GameType.Ets2, "A", _modRoot), new(GameType.Ets2, "B", _modRoot + Path.DirectorySeparatorChar)] };
        Assert.True((await Service().ResolveAsync(same)).ConfigurationVerified);
    }

    [Fact]
    public async Task OtherGameModDirectoriesCannotSupplyMissingActivePackage()
    {
        WriteMods("active");
        var other = DirectoryFor("ats-mods");
        Package(other, "active.scs");
        var result = await Service().ResolveAsync(_request with { ModDirectories = [new(GameType.Ats, "ATS", other)] });
        Assert.False(result.ConfigurationVerified);
        Assert.DoesNotContain(result.Layers, layer => layer.PhysicalPath.StartsWith(other, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("package_version: \"\"")]
    [InlineData("package_version: \"1\" compatible_versions[]: \"1.61.*\"")]
    [InlineData("package_version: \"1\" compatible_versions[]: \"1.62.*\" compatible_versions[]: \"unknown\"")]
    [InlineData("package_version: \"1\" unknown_dependency: true")]
    public async Task UnverifiedManifestVersionOrConditionsBlockConfiguration(string fields)
    {
        WriteMods("active");
        Package(_modRoot, "active.scs", $"SiiNunit {{ mod_package : .package {{ {fields} }} }}");
        Assert.False((await Service().ResolveAsync(_request)).ConfigurationVerified);
    }

    [Fact]
    public async Task DeclaredDlcDependenciesDoNotBecomeEntitlementEvidence()
    {
        WriteMods("active");
        Package(_modRoot, "active.scs", "SiiNunit { mod_package : .p { package_version: \"1\" dlc_dependencies[]: \"dlc_toys\" } }");
        var result = await Service().ResolveAsync(_request);
        Assert.True(result.ConfigurationVerified, string.Join(";", result.Diagnostics));
        Assert.False(Assert.Single(result.Layers, layer => layer.Kind == ScsPowertrainSourceKind.LocalMod).AvailabilityVerified);
    }

    [Fact]
    public async Task ConditionalLayersRemainPossibleOverridesEvenWhenDlcIsNotInstalled()
    {
        WriteMods("active");
        Zip(_modRoot, "active.scs", ("manifest.sii", Manifest), ("base/" + Definition, "base"), ("dlc_unowned/" + Definition, "conditional"));
        var result = await Service().ResolveAsync(_request);
        Assert.True(result.ConfigurationVerified, string.Join(";", result.Diagnostics));
        var package = result.Layers.Where(layer => layer.Kind == ScsPowertrainSourceKind.LocalMod).ToArray();
        Assert.Equal(2, package.Length);
        var unconditional = Assert.Single(package, layer => layer.VirtualRoot == "/base");
        var conditional = Assert.Single(package, layer => layer.VirtualRoot == "/dlc_unowned");
        Assert.True(unconditional.AvailabilityVerified);
        Assert.False(conditional.AvailabilityVerified);
        Assert.True(conditional.Priority > unconditional.Priority);
    }

    [Fact]
    public async Task DlcIdentityComesFromManifestButRemainsUnavailable()
    {
        Zip(_installation.InstallPath, "dlc_balkan_e.scs", ("dlc_balkan_e.manifest.sii", "SiiNunit { dlc_package : .d { id: eut2_balkane game_id: eut2 steam_app_id: 1056760 } }"), (Definition, "dlc"));
        var result = await Service().ResolveAsync(_request);
        Assert.True(result.ConfigurationVerified, string.Join(";", result.Diagnostics));
        var layer = Assert.Single(result.Layers, layer => layer.Kind == ScsPowertrainSourceKind.Dlc);
        Assert.Equal("eut2_balkane", layer.PackageIdentity);
        Assert.False(layer.AvailabilityVerified);
        Assert.Equal(100, layer.Priority);
    }

    [Fact]
    public async Task WorkshopSelectsUniqueMatchingVariantRatherThanUniversalOrNewest()
    {
        var root = WorkshopRoot();
        WriteVersions(root, ("old", "1.61.*"), ("current", "1.62.*"), ("universal", null));
        Package(root, "old.zip"); Package(root, "current.zip"); Package(root, "universal.zip");
        var result = await Service().ResolveAsync(_request);
        Assert.True(result.ConfigurationVerified, string.Join(";", result.Diagnostics));
        Assert.EndsWith("current.zip", Assert.Single(result.Layers, layer => layer.Kind == ScsPowertrainSourceKind.Workshop).PhysicalPath);
        Assert.Contains(result.Files, file => Path.GetFileName(file.Path) == "versions.sii" && file.Sha256.Length == 64);
    }

    [Fact]
    public async Task WorkshopUniversalCanResolveAnExtractedFolder()
    {
        var root = WorkshopRoot();
        WriteVersions(root, ("universal", null), ("old", "1.40.*"));
        var folder = Path.Combine(root, "universal");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.sii"), Manifest);
        var result = await Service().ResolveAsync(_request);
        Assert.True(result.ConfigurationVerified, string.Join(";", result.Diagnostics));
        Assert.Equal(folder, Assert.Single(result.Layers, layer => layer.Kind == ScsPowertrainSourceKind.Workshop).PhysicalPath);
    }

    [Theory]
    [InlineData("two-matching")]
    [InlineData("two-universal")]
    [InlineData("unmatched")]
    public async Task WorkshopVersionAmbiguityOrAbsenceBlocksConfiguration(string failure)
    {
        var root = WorkshopRoot();
        if (failure == "two-matching") WriteVersions(root, ("first", "1.62.*"), ("second", "1.62.1.*"));
        else if (failure == "two-universal") WriteVersions(root, ("first", null), ("second", null));
        else WriteVersions(root, ("first", "1.60.*"));
        Package(root, "first.zip"); Package(root, "second.zip");
        Assert.False((await Service().ResolveAsync(_request)).ConfigurationVerified);
    }

    [Fact]
    public async Task WorkshopDuplicatesAcrossLibrariesAreDetectedEvenIfLocatorCollapsedItems()
    {
        var root = WorkshopRoot();
        WriteVersions(root, ("universal", null)); Package(root, "universal.zip");
        var second = DirectoryFor("second-library/steamapps/workshop/content/227300/123");
        _workshop = _workshop with { ScannedContentRoots = [Directory.GetParent(root)!.FullName, Directory.GetParent(second)!.FullName] };
        Assert.False((await Service().ResolveAsync(_request)).ConfigurationVerified);
    }

    [Fact]
    public async Task ExecutableAndArchiveVersionsMustAgree()
    {
        var result = await Service("1.61.1.0").ResolveAsync(_request);
        Assert.False(result.ConfigurationVerified);
        Assert.Contains(result.Diagnostics, message => message.Contains("unterschiedliche Spielversionen"));
    }

    [Fact]
    public async Task SameLengthAndTimeChangedMetadataDoesNotReuseCodecCache()
    {
        WriteMods("first");
        var profilePath = _request.Profile.ProfileSiiPath;
        var infoPath = _infoPath;
        var codec = new CachedByPathCodec();
        codec.Text[profilePath] = File.ReadAllText(profilePath);
        codec.Text[infoPath] = File.ReadAllText(infoPath);
        var profileTime = File.GetLastWriteTimeUtc(profilePath);
        var infoTime = File.GetLastWriteTimeUtc(infoPath);
        var size = new FileInfo(profilePath).Length;
        WriteMods("other");
        File.SetLastWriteTimeUtc(profilePath, profileTime);
        File.SetLastWriteTimeUtc(infoPath, infoTime);
        Assert.Equal(size, new FileInfo(profilePath).Length);
        Package(_modRoot, "other.scs");
        var service = new ScsActiveContentResolver(codec, new Detector(_installation), new Workshop(_workshop), _ => "1.62.1.0");
        var result = await service.ResolveAsync(_request);
        Assert.True(result.ConfigurationVerified, string.Join(";", result.Diagnostics));
        Assert.Equal("other", Assert.Single(result.ActiveMods).Identity);
        Assert.DoesNotContain(profilePath, codec.ReadPaths);
        Assert.DoesNotContain(infoPath, codec.ReadPaths);
        Assert.All(codec.ReadPaths, path => Assert.False(File.Exists(path)));
        Assert.DoesNotContain(result.Files, file => codec.ReadPaths.Contains(file.Path));
    }

    [Fact]
    public async Task VersionMetadataMustIdentifySelectedGame()
    {
        File.Delete(Path.Combine(_installation.InstallPath, "version.scs"));
        Zip(_installation.InstallPath, "version.scs", ("version.sii", "SiiNunit { fs_pack_set : .v { application: ats version: \"1.62.1.0\" } }"));
        Assert.False((await Service().ResolveAsync(_request)).ConfigurationVerified);
    }

    [Fact]
    public async Task WorkshopUsesVersionsFileInsteadOfIgnoredManifestCompatibility()
    {
        var root = WorkshopRoot();
        WriteVersions(root, ("current", "1.62.*"));
        Package(root, "current.zip", "SiiNunit { mod_package : .p { package_version: \"1\" compatible_versions[]: \"1.40.*\" } }");
        Assert.True((await Service().ResolveAsync(_request)).ConfigurationVerified);
    }

    [Fact]
    public async Task InformationalWorkshopVariantCannotBecomeAnActiveLayer()
    {
        var root = WorkshopRoot();
        File.WriteAllText(Path.Combine(root, "versions.sii"), "SiiNunit { package_version_info : .info { package_name: \"info\" informational: true } }");
        Package(root, "info.zip");
        var result = await Service().ResolveAsync(_request);
        Assert.False(result.ConfigurationVerified);
        Assert.DoesNotContain(result.Layers, layer => layer.Kind == ScsPowertrainSourceKind.Workshop);
        Assert.Contains(result.Diagnostics, message => message.Contains("Information"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArbitraryInstallationArchiveDoesNotBecomeVerifiedContent(bool conflict)
    {
        var virtualPath = conflict ? Definition : "def/vehicle/truck/test/engine/unknown.sii";
        Zip(_installation.InstallPath, "copied_extra.scs", (virtualPath, "unverified content"));
        using var session = await new ScsEffectiveContentService(Service()).OpenAsync(_request);
        Assert.True(session.Evidence.ConfigurationVerified);
        Assert.False(Assert.Single(session.Evidence.Layers, layer => layer.PackageIdentity == "copied_extra.scs").AvailabilityVerified);
        if (conflict) Assert.ThrowsAny<ScsSaveEditException>(() => session.ReadDefinition("/" + virtualPath));
        else Assert.False(session.ReadDefinition("/" + virtualPath).AvailabilityVerified);
    }

    [Theory]
    [InlineData("base.zip")]
    [InlineData("dlc_toys.zip")]
    public async Task NestedConditionalArchivesCannotBeSilentlyOmitted(string nested)
    {
        WriteMods("active");
        Zip(_modRoot, "active.scs", ("manifest.sii", Manifest), (Definition, "base"), (nested, "nested archive bytes"));
        var result = await Service().ResolveAsync(_request);
        Assert.False(result.ConfigurationVerified);
        Assert.Contains(result.Diagnostics, message => message.Contains("Teilarchive"));
    }

    [Fact]
    public async Task ExcessiveConditionalLayersFailClosedWithinTheLayerBudget()
    {
        WriteMods("active");
        var entries = Enumerable.Range(0, 520).Select(index => ($"dlc_{index}/{Definition}", "conditional")).Append(("manifest.sii", Manifest)).ToArray();
        Zip(_modRoot, "active.scs", entries);
        var result = await Service().ResolveAsync(_request);
        Assert.False(result.ConfigurationVerified);
        Assert.InRange(result.Layers.Count, 0, 512);
        Assert.Contains(result.Diagnostics, message => message.Contains("Leselimit"));
    }

    [Theory]
    [InlineData("active_mods")]
    [InlineData("dependencies")]
    public async Task ProfileAndSaveArraysRequireDeclaredCount(string array)
    {
        WriteMods("active"); Package(_modRoot, "active.scs");
        var file = array == "active_mods" ? _request.Profile.ProfileSiiPath : _infoPath;
        File.WriteAllText(file, File.ReadAllText(file).Replace($"{array}: 1", string.Empty, StringComparison.Ordinal));
        Assert.False((await Service().ResolveAsync(_request)).ConfigurationVerified);
    }

    [Fact]
    public async Task ResolverDoesNotModifyInputsOrCreateFiles()
    {
        WriteMods("active"); Package(_modRoot, "active.scs");
        var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path, Hash);
        await Service().ResolveAsync(_request);
        var after = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path, Hash);
        Assert.Equal(before.OrderBy(pair => pair.Key), after.OrderBy(pair => pair.Key));
    }

    [Fact]
    public async Task CancellationIsNotHiddenAsAnUnverifiedResult()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().ResolveAsync(_request, cancelled.Token));
    }

    private ScsActiveContentResolver Service(string version = "1.62.1.0") =>
        new(new ScsPlainTextSaveCodec(), new Detector(_installation), new Workshop(_workshop), _ => version);

    private void WriteMods(params string[] mods)
    {
        var fields = string.Join("\n", mods.Select((mod, index) => $"active_mods[{index}]: \"{mod}|Display {index}\""));
        File.WriteAllText(_request.Profile.ProfileSiiPath, $"SiiNunit {{ user_profile : .profile {{ active_mods: {mods.Length}\n{fields}\n}} }}");
        WriteInfo(mods);
    }

    private void WriteInfo(params string[] mods)
    {
        var fields = string.Join("\n", mods.Select((mod, index) => $"dependencies[{index}]: \"mod|{mod}|Historical title {index}\""));
        File.WriteAllText(_infoPath, $"SiiNunit {{ save_container : .save {{ dependencies: {mods.Length}\n{fields}\n}} }}");
    }

    private string WorkshopRoot()
    {
        WriteMods("mod_workshop_package.000000000000007B");
        var root = DirectoryFor("library/steamapps/workshop/content/227300/123");
        _workshop = new(GameType.Ets2, [new(GameType.Ets2, 123, root, _root, DateTimeOffset.UtcNow)], [Directory.GetParent(root)!.FullName], []);
        return root;
    }

    private static void WriteVersions(string root, params (string Name, string? Pattern)[] versions)
    {
        var units = versions.Select((entry, index) => $"package_version_info : .v{index}\n{{ package_name: \"{entry.Name}\" " +
            (entry.Pattern is null ? "" : $"compatible_versions[]: \"{entry.Pattern}\" ") + "}");
        File.WriteAllText(Path.Combine(root, "versions.sii"), "SiiNunit { " + string.Join("\n", units) + " }");
    }

    private static void Package(string root, string name, string manifest = Manifest) => Zip(root, name, ("manifest.sii", manifest), (Definition, "component"));
    private static void Zip(string root, string name, params (string Path, string Content)[] entries)
    {
        using var archive = ZipFile.Open(Path.Combine(root, name), ZipArchiveMode.Create);
        foreach (var entry in entries) { using var writer = new StreamWriter(archive.CreateEntry(entry.Path).Open()); writer.Write(entry.Content); }
    }
    private string DirectoryFor(string relative) { var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(path); return path; }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class Detector(GameInstallation installation) : IGameInstallationDetector
    {
        public GameInstallation? Validate(GameType gameType, string installPath, GameInstallationSource source = GameInstallationSource.ManualSelection) => installation;
        public Task<GameInstallation?> DetectAsync(GameType gameType, string? preferredInstallPath = null, CancellationToken cancellationToken = default) => Task.FromResult<GameInstallation?>(installation);
    }
    private sealed class Workshop(WorkshopContentSnapshot snapshot) : IWorkshopContentLocator
    {
        public Task<WorkshopContentSnapshot> ScanAsync(GameType game, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
        public Task<WorkshopContentSnapshot> ScanAsync(GameType game, string? preferredInstallPath, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }
    private sealed class CachedByPathCodec : IScsSaveCodec
    {
        internal Dictionary<string, string> Text { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal List<string> ReadPaths { get; } = [];
        public Task<ScsSaveDocument> ReadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            ReadPaths.Add(filePath);
            if (!Text.TryGetValue(filePath, out var text)) Text[filePath] = text = File.ReadAllText(filePath);
            return Task.FromResult(new ScsSaveDocument(filePath, text, ScsSaveDocumentFormat.PlainText));
        }
    }
}
