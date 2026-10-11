using System.Globalization;
using System.Text;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ScsProfileEditService : IScsProfileEditService
{
    private readonly IScsSaveCodec _codec;
    private readonly IScsSaveEditService _saveEditService;

    public ScsProfileEditService(
        IScsSaveCodec codec,
        IScsSaveEditService saveEditService)
    {
        _codec = codec;
        _saveEditService = saveEditService;
    }

    public async Task<ScsSaveEditResult> RenameProfileAsync(
        ScsProfileReference profile,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateProfileReference(profile);

        var normalizedName = ValidateProfileName(newName);
        var sourceDirectory = Path.GetFullPath(profile.ProfileDirectory);
        var targetDirectory = BuildRenamedProfileDirectory(profile, normalizedName);
        var sourceProfileSii = Path.GetFullPath(profile.ProfileSiiPath);
        var targetProfileSii = Path.Combine(targetDirectory, "profile.sii");
        var directoryChanges = !PathsEqual(sourceDirectory, targetDirectory);

        if (directoryChanges && Directory.Exists(targetDirectory))
        {
            throw new ScsSaveEditException(
                $"Ein Profil mit dem Namen '{normalizedName}' existiert bereits.");
        }

        var source = await _codec.ReadAsync(sourceProfileSii, cancellationToken);
        var document = ScsSiiTextDocument.Parse(source.Content);
        var updated = document
            .SetScalar("profile_name", QuoteString(normalizedName))
            .ToText();

        var result = await _saveEditService.ApplyAsync(
            new ScsSaveEditRequest(
                sourceProfileSii,
                $"Profilname ändern: {normalizedName}",
                updated),
            cancellationToken);

        if (!directoryChanges)
            return result;

        try
        {
            Directory.Move(sourceDirectory, targetDirectory);

            if (!File.Exists(targetProfileSii))
            {
                throw new ScsSaveEditException(
                    "Der umbenannte Profilordner enthält keine profile.sii.");
            }

            var verified = await _codec.ReadAsync(targetProfileSii, cancellationToken);
            var verifiedDocument = ScsSiiTextDocument.Parse(verified.Content);
            if (!verifiedDocument.TryGetScalar("profile_name", out var profileNameValue) ||
                !string.Equals(
                    UnquoteSiiString(profileNameValue),
                    normalizedName,
                    StringComparison.Ordinal))
            {
                throw new ScsSaveEditException(
                    "Der neue Profilname konnte nach dem Umbenennen nicht validiert werden.");
            }

            return result with { FilePath = targetProfileSii };
        }
        catch (Exception renameException)
        {
            try
            {
                if (Directory.Exists(targetDirectory) && !Directory.Exists(sourceDirectory))
                    Directory.Move(targetDirectory, sourceDirectory);

                if (File.Exists(sourceProfileSii))
                {
                    await _saveEditService.RestoreBackupAsync(
                        result.BackupPath,
                        sourceProfileSii,
                        cancellationToken);
                }
            }
            catch (Exception rollbackException)
            {
                throw new ScsSaveEditException(
                    $"Profilumbenennung fehlgeschlagen und konnte nicht vollständig zurückgerollt werden. " +
                    $"Backup: {result.BackupPath}",
                    new AggregateException(renameException, rollbackException));
            }

            if (renameException is ScsSaveEditException saveEditException)
                throw saveEditException;

            throw new ScsSaveEditException(
                "Der Profilordner konnte nicht sicher auf den neuen Namen umbenannt werden.",
                renameException);
        }
    }

    public async Task<long> GetMoneyAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken = default)
    {
        var document = await ReadSaveDocumentAsync(save, cancellationToken);
        var bank = ScsSaveStructureResolver.ResolveBankUnit(document);
        return ParseLongScalar(document, bank, "money_account", "Geld");
    }

    public async Task<ScsSaveEditResult> SetMoneyAsync(
        ScsSaveReference save,
        long amount,
        CancellationToken cancellationToken = default)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Geld darf nicht negativ sein.");

        var document = await ReadSaveDocumentAsync(save, cancellationToken);
        var bank = ScsSaveStructureResolver.ResolveBankUnit(document);
        var updated = document.SetRequiredUnitScalar(
            bank,
            "money_account",
            amount.ToString(CultureInfo.InvariantCulture));

        return await ApplySaveDocumentAsync(
            save,
            updated,
            $"Geld ändern: {amount}",
            cancellationToken);
    }

    public async Task<long> GetExperienceAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken = default)
    {
        var document = await ReadSaveDocumentAsync(save, cancellationToken);
        var economy = ScsSaveStructureResolver.ResolveEconomyUnit(document);
        return ParseLongScalar(document, economy, "experience_points", "Erfahrungspunkte");
    }

    public async Task<ScsSaveEditResult> SetExperienceAsync(
        ScsSaveReference save,
        long experiencePoints,
        CancellationToken cancellationToken = default)
    {
        if (experiencePoints < 0)
            throw new ArgumentOutOfRangeException(
                nameof(experiencePoints),
                "Erfahrungspunkte dürfen nicht negativ sein.");

        var document = await ReadSaveDocumentAsync(save, cancellationToken);
        var economy = ScsSaveStructureResolver.ResolveEconomyUnit(document);
        var updated = document.SetRequiredUnitScalar(
            economy,
            "experience_points",
            experiencePoints.ToString(CultureInfo.InvariantCulture));

        return await ApplySaveDocumentAsync(
            save,
            updated,
            $"Erfahrung ändern: {experiencePoints}",
            cancellationToken);
    }

    public async Task<ScsCareerSkills> GetCareerSkillsAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken = default)
    {
        var document = await ReadSaveDocumentAsync(save, cancellationToken);
        var economy = ScsSaveStructureResolver.ResolveEconomyUnit(document);

        return new ScsCareerSkills(
            ParseSkillScalar(document, economy, "adr", 0, 63),
            ParseSkillScalar(document, economy, "long_dist", 0, 6),
            ParseSkillScalar(document, economy, "heavy", 0, 6),
            ParseSkillScalar(document, economy, "fragile", 0, 6),
            ParseSkillScalar(document, economy, "urgent", 0, 6),
            ParseSkillScalar(document, economy, "mechanical", 0, 6));
    }

    public async Task<ScsSaveEditResult> SetCareerSkillsAsync(
        ScsSaveReference save,
        ScsCareerSkills skills,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skills);
        ValidateSkills(skills);

        var document = await ReadSaveDocumentAsync(save, cancellationToken);
        var economy = ScsSaveStructureResolver.ResolveEconomyUnit(document);

        var updated = document
            .SetRequiredUnitScalar(
                economy,
                "adr",
                skills.AdrMask.ToString(CultureInfo.InvariantCulture));

        economy = ScsSaveStructureResolver.ResolveEconomyUnit(updated);
        updated = updated.SetRequiredUnitScalar(
            economy,
            "long_dist",
            skills.LongDistance.ToString(CultureInfo.InvariantCulture));

        economy = ScsSaveStructureResolver.ResolveEconomyUnit(updated);
        updated = updated.SetRequiredUnitScalar(
            economy,
            "heavy",
            skills.HighValueCargo.ToString(CultureInfo.InvariantCulture));

        economy = ScsSaveStructureResolver.ResolveEconomyUnit(updated);
        updated = updated.SetRequiredUnitScalar(
            economy,
            "fragile",
            skills.FragileCargo.ToString(CultureInfo.InvariantCulture));

        economy = ScsSaveStructureResolver.ResolveEconomyUnit(updated);
        updated = updated.SetRequiredUnitScalar(
            economy,
            "urgent",
            skills.UrgentDelivery.ToString(CultureInfo.InvariantCulture));

        economy = ScsSaveStructureResolver.ResolveEconomyUnit(updated);
        updated = updated.SetRequiredUnitScalar(
            economy,
            "mechanical",
            skills.EcoDriving.ToString(CultureInfo.InvariantCulture));

        return await ApplySaveDocumentAsync(
            save,
            updated,
            "Karriere-Skills ändern",
            cancellationToken);
    }

    private async Task<ScsSiiUnitDocument> ReadSaveDocumentAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(save);
        ValidateSaveReference(save);

        var source = await _codec.ReadAsync(save.GameSiiPath, cancellationToken);
        return ScsSiiUnitDocument.Parse(source.Content);
    }

    private Task<ScsSaveEditResult> ApplySaveDocumentAsync(
        ScsSaveReference save,
        ScsSiiUnitDocument document,
        string operation,
        CancellationToken cancellationToken) =>
        _saveEditService.ApplyAsync(
            new ScsSaveEditRequest(
                save.GameSiiPath,
                operation,
                document.ToText()),
            cancellationToken);

    private static long ParseLongScalar(
        ScsSiiUnitDocument document,
        ScsSiiUnit unit,
        string key,
        string displayName)
    {
        var value = document.GetRequiredUnitScalar(unit, key);
        if (!long.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed < 0)
        {
            throw new ScsSaveEditException(
                $"{displayName} enthält keinen unterstützten nichtnegativen Ganzzahlwert.");
        }

        return parsed;
    }

    private static int ParseSkillScalar(
        ScsSiiUnitDocument document,
        ScsSiiUnit economy,
        string key,
        int min,
        int max)
    {
        var value = document.GetRequiredUnitScalar(economy, key);
        if (!int.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed < min ||
            parsed > max)
        {
            throw new ScsSaveEditException(
                $"Skill-Feld '{key}' in economy enthält einen ungültigen Wert.");
        }

        return parsed;
    }

    private static void ValidateSkills(ScsCareerSkills skills)
    {
        if (skills.AdrMask is < 0 or > 63)
        {
            throw new ArgumentOutOfRangeException(
                nameof(skills),
                "ADR muss als Bitmaske zwischen 0 und 63 angegeben werden.");
        }

        ValidateSkillLevel(skills.LongDistance, nameof(skills.LongDistance));
        ValidateSkillLevel(skills.HighValueCargo, nameof(skills.HighValueCargo));
        ValidateSkillLevel(skills.FragileCargo, nameof(skills.FragileCargo));
        ValidateSkillLevel(skills.UrgentDelivery, nameof(skills.UrgentDelivery));
        ValidateSkillLevel(skills.EcoDriving, nameof(skills.EcoDriving));
    }

    private static void ValidateSkillLevel(int value, string parameterName)
    {
        if (value is < 0 or > 6)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Skill-Level müssen zwischen 0 und 6 liegen.");
        }
    }

    private static void ValidateProfileReference(ScsProfileReference profile)
    {
        var profileDirectory = Path.GetFullPath(profile.ProfileDirectory);
        var expected = Path.Combine(profileDirectory, "profile.sii");

        if (!PathsEqual(expected, profile.ProfileSiiPath))
            throw new ScsSaveEditException("Ungültige profile.sii-Auswahl.");

        if (!Directory.Exists(profileDirectory) || !File.Exists(expected))
            throw new ScsSaveEditException("Der ausgewählte Profilordner ist nicht mehr vorhanden.");

        var attributes = File.GetAttributes(profileDirectory);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new ScsSaveEditException("Profilordner über Reparse-Points werden nicht bearbeitet.");

        var storageRootName = profile.StorageKind switch
        {
            ScsProfileStorageKind.Local => "profiles",
            ScsProfileStorageKind.SteamCloud => "steam_profiles",
            _ => throw new ScsSaveEditException("Unbekannter Profilspeichertyp.")
        };

        var expectedRoot = Path.GetFullPath(
            Path.Combine(profile.GameHomeDirectory, storageRootName));
        var actualParent = Directory.GetParent(profileDirectory)?.FullName;

        if (actualParent is null || !PathsEqual(actualParent, expectedRoot))
        {
            throw new ScsSaveEditException(
                "Der Profilordner liegt nicht direkt im erwarteten SCS-Profilverzeichnis.");
        }
    }

    private static string BuildRenamedProfileDirectory(
        ScsProfileReference profile,
        string normalizedName)
    {
        var parent = Directory.GetParent(Path.GetFullPath(profile.ProfileDirectory))?.FullName
            ?? throw new ScsSaveEditException("Der Profilordner besitzt kein gültiges Elternverzeichnis.");

        var encodedName = Convert.ToHexString(Encoding.UTF8.GetBytes(normalizedName));
        return Path.Combine(parent, encodedName);
    }

    private static string UnquoteSiiString(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '"' || trimmed[^1] != '"')
        {
            throw new ScsSaveEditException(
                "profile_name enthält keinen unterstützten SII-String.");
        }

        var content = trimmed[1..^1];
        var builder = new StringBuilder(content.Length);
        var escaped = false;

        foreach (var character in content)
        {
            if (escaped)
            {
                if (character is not ('\\' or '"'))
                {
                    throw new ScsSaveEditException(
                        "profile_name enthält eine nicht unterstützte Escape-Sequenz.");
                }

                builder.Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            builder.Append(character);
        }

        if (escaped)
            throw new ScsSaveEditException("profile_name endet mit einer unvollständigen Escape-Sequenz.");

        return builder.ToString();
    }

    private static void ValidateSaveReference(ScsSaveReference save)
    {
        var profileDirectory = Path.GetFullPath(save.ProfileDirectory);
        var saveDirectory = Path.GetFullPath(save.SaveDirectory);
        var expectedRoot = Path.Combine(profileDirectory, "save");

        if (!IsWithinRoot(saveDirectory, expectedRoot))
            throw new ScsSaveEditException("Das Save-Verzeichnis liegt außerhalb des ausgewählten Profils.");

        var expected = Path.Combine(saveDirectory, "game.sii");
        if (!PathsEqual(expected, save.GameSiiPath))
            throw new ScsSaveEditException("Ungültige game.sii-Auswahl.");
    }

    private static string ValidateProfileName(string value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 64)
            throw new ArgumentException("Der Profilname muss zwischen 1 und 64 Zeichen lang sein.", nameof(value));
        if (name.Any(char.IsControl))
            throw new ArgumentException("Der Profilname enthält ungültige Steuerzeichen.", nameof(value));

        return name;
    }

    private static string QuoteString(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsWithinRoot(string path, string root)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var prefix = fullRoot + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
