using System.Security.Cryptography;
using System.Text;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

/// <summary>Installs only a freshly verified pair through the existing backup/replace transaction.</summary>
public sealed class ScsTruckPowertrainInstaller(
    ScsTruckPowertrainService powertrainService, IScsSaveEditService saveEditService,
    LauncherFeatureFlags? featureFlags = null) : IScsTruckPowertrainInstaller
{
    private readonly LauncherFeatureFlags _featureFlags = featureFlags ?? LauncherFeatureFlags.Current;

    public async Task<ScsSaveEditResult> InstallAsync(ScsTruckPowertrainInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        _featureFlags.RequirePowertrainEditing();
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSelection(request);
        using var verification = await powertrainService.OpenVerificationAsync(request.Content, cancellationToken).ConfigureAwait(false);
        var original = verification.Snapshot;
        if (!string.Equals(original.SaveSha256, request.ExpectedSaveSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(original.TruckId, request.ExpectedTruckId, StringComparison.Ordinal))
            throw new ScsSaveEditException("Save oder ausgewählter Truck stimmt nicht mehr mit der bestätigten Vorschau überein. Bitte neu laden.");
        if (original.CurrentEnginePath == request.EngineDataPath && original.CurrentTransmissionPath == request.TransmissionDataPath)
            throw new ScsSaveEditException("Motor und Getriebe sind bereits eingebaut; es gibt keine Änderung.");
        EnsurePair();
        var updated = verification.BuildUpdatedContent(request.EngineDataPath, request.TransmissionDataPath);
        var expectedUpdatedSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(updated)));
        var transaction = new ScsSaveTransactionValidation(original.SaveSha256,
            async token =>
            {
                ValidateSelection(request);
                await verification.VerifyUnchangedAsync(token).ConfigureAwait(false);
                EnsurePair();
            },
            async (written, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (!string.Equals(written, updated, StringComparison.Ordinal) ||
                    !string.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(written))), expectedUpdatedSha, StringComparison.Ordinal))
                    throw new ScsSaveEditException("Der geschriebene Save stimmt nicht vollständig mit der geprüften Änderung überein.");
                var after = ScsTruckPowertrainSnapshotReader.Parse(request.Content.Save, expectedUpdatedSha, written);
                if (after.Game != original.Game || after.TruckId != original.TruckId || after.TruckModelRoot != original.TruckModelRoot ||
                    after.TruckDataAccessoryId != original.TruckDataAccessoryId || after.EngineAccessoryId != original.EngineAccessoryId ||
                    after.TransmissionAccessoryId != original.TransmissionAccessoryId ||
                    after.CurrentEnginePath != request.EngineDataPath || after.CurrentTransmissionPath != request.TransmissionDataPath ||
                    !after.Accessories.Select(a => (a.UnitId, a.UnitType, a.DataPath)).SequenceEqual(original.Accessories.Select(a =>
                        (a.UnitId, a.UnitType, a.UnitId == original.EngineAccessoryId ? request.EngineDataPath :
                            a.UnitId == original.TransmissionAccessoryId ? request.TransmissionDataPath : a.DataPath))))
                    throw new ScsSaveEditException("Die Truck-Referenzen im geschriebenen Save stimmen nicht mit dem geprüften Zubehörgraphen überein.");
                await verification.VerifySourcesUnchangedAsync(token).ConfigureAwait(false);
            });
        return await saveEditService.ApplyAsync(new ScsSaveEditRequest(original.SaveFilePath,
            "Geprüften Truck-Antrieb einbauen", updated, ScsSaveWriteMode.RequireGameStopped,
            TransactionValidation: transaction), cancellationToken).ConfigureAwait(false);

        void EnsurePair()
        {
            var assessment = verification.AssessPair(request.EngineDataPath, request.TransmissionDataPath);
            if (!assessment.IsVerified) throw new ScsSaveEditException(string.Join(Environment.NewLine, assessment.Reasons));
        }
    }

    private static void ValidateSelection(ScsTruckPowertrainInstallRequest request)
    {
        try
        {
            if (request.Content?.Profile is not { } profile || request.Content.Save is not { } save ||
                !Enum.IsDefined(profile.GameType) || profile.GameType != save.GameType ||
                string.IsNullOrWhiteSpace(request.ExpectedTruckId) || string.IsNullOrWhiteSpace(request.EngineDataPath) ||
                string.IsNullOrWhiteSpace(request.TransmissionDataPath) || request.ExpectedSaveSha256 is not { Length: 64 } sha ||
                sha.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException("Die Einbauanfrage enthält keine eindeutige Spiel-, Truck- und Save-Bindung.");
            var directory = Absolute(profile.ProfileDirectory);
            var home = Absolute(profile.GameHomeDirectory);
            var profileFile = Absolute(profile.ProfileSiiPath);
            var saveDirectory = Absolute(save.SaveDirectory);
            var target = Absolute(save.GameSiiPath);
            if (profile.StorageKind == ScsProfileStorageKind.SteamCloudRemote ||
                ScsProfileSaveLocator.IsSteamCloudRemoteProfileDirectory(GameType.Ets2, directory) ||
                ScsProfileSaveLocator.IsSteamCloudRemoteProfileDirectory(GameType.Ats, directory))
                throw new InvalidDataException("Steam-userdata-Profile bleiben schreibgeschützt. Bitte eine lokale Profilkopie auswählen.");
            if (!Same(directory, save.ProfileDirectory) || !Same(profileFile, Path.Combine(directory, "profile.sii")) ||
                Path.GetFileName(directory) != profile.ProfileDirectoryName || !File.Exists(profileFile))
                throw new InvalidDataException("Profilidentität und ausgewählter Profilpfad stimmen nicht überein.");
            if (profile.StorageKind is ScsProfileStorageKind.Local or ScsProfileStorageKind.SteamCloud)
            {
                var expectedRoot = Path.Combine(home, profile.StorageKind == ScsProfileStorageKind.Local ? "profiles" : "steam_profiles");
                if (!Same(Path.GetDirectoryName(directory)!, expectedRoot)) throw new InvalidDataException("Profil liegt außerhalb des ausgewählten Spielverzeichnisses.");
            }
            else
                throw new InvalidDataException("Der angegebene Profilspeicher ist nicht bestätigt.");
            if (!Same(Path.GetDirectoryName(saveDirectory)!, Path.Combine(directory, "save")) ||
                Path.GetFileName(saveDirectory) != save.SaveDirectoryName || !Same(target, Path.Combine(saveDirectory, "game.sii")) || !File.Exists(target))
                throw new InvalidDataException("Der ausgewählte Save liegt nicht im bestätigten Profil.");
            foreach (var path in new[] { home, directory, profileFile, saveDirectory, target }) ScsDefinitionSource.CheckNoReparsePoints(path);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new ScsSaveEditException("Ungültiger Einbaukontext: " + exception.Message, exception); }
    }
    private static string Absolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new InvalidDataException("Ein absoluter Auswahlpfad fehlt.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    private static bool Same(string first, string second) => string.Equals(Absolute(first), Absolute(second), StringComparison.OrdinalIgnoreCase);
}
