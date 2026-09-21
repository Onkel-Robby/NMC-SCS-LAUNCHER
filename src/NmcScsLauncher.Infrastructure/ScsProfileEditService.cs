using System.Globalization;
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
        var source = await _codec.ReadAsync(profile.ProfileSiiPath, cancellationToken);
        var document = ScsSiiTextDocument.Parse(source.Content);
        var updated = document
            .SetScalar("profile_name", QuoteString(normalizedName))
            .ToText();

        return await _saveEditService.ApplyAsync(
            new ScsSaveEditRequest(
                profile.ProfileSiiPath,
                $"Profilname ändern: {normalizedName}",
                updated),
            cancellationToken);
    }

    public Task<ScsSaveEditResult> SetMoneyAsync(
        ScsSaveReference save,
        long amount,
        CancellationToken cancellationToken = default)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Geld darf nicht negativ sein.");

        return SetSaveScalarAsync(
            save,
            "money_account",
            amount.ToString(CultureInfo.InvariantCulture),
            $"Geld ändern: {amount}",
            cancellationToken);
    }

    public Task<ScsSaveEditResult> SetExperienceAsync(
        ScsSaveReference save,
        long experiencePoints,
        CancellationToken cancellationToken = default)
    {
        if (experiencePoints < 0)
            throw new ArgumentOutOfRangeException(
                nameof(experiencePoints),
                "Erfahrungspunkte dürfen nicht negativ sein.");

        return SetSaveScalarAsync(
            save,
            "experience_points",
            experiencePoints.ToString(CultureInfo.InvariantCulture),
            $"Erfahrung ändern: {experiencePoints}",
            cancellationToken);
    }

    public async Task<ScsCareerSkills> GetCareerSkillsAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);
        ValidateSaveReference(save);

        var source = await _codec.ReadAsync(save.GameSiiPath, cancellationToken);
        var document = ScsSiiTextDocument.Parse(source.Content);

        return new ScsCareerSkills(
            ParseSkillScalar(document, "adr", 0, 63),
            ParseSkillScalar(document, "long_dist", 0, 6),
            ParseSkillScalar(document, "heavy", 0, 6),
            ParseSkillScalar(document, "fragile", 0, 6),
            ParseSkillScalar(document, "urgent", 0, 6),
            ParseSkillScalar(document, "mechanical", 0, 6));
    }

    public async Task<ScsSaveEditResult> SetCareerSkillsAsync(
        ScsSaveReference save,
        ScsCareerSkills skills,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(skills);
        ValidateSaveReference(save);
        ValidateSkills(skills);

        var source = await _codec.ReadAsync(save.GameSiiPath, cancellationToken);
        var document = ScsSiiTextDocument.Parse(source.Content);

        var updated = document
            .SetScalar("adr", skills.AdrMask.ToString(CultureInfo.InvariantCulture))
            .SetScalar("long_dist", skills.LongDistance.ToString(CultureInfo.InvariantCulture))
            .SetScalar("heavy", skills.HighValueCargo.ToString(CultureInfo.InvariantCulture))
            .SetScalar("fragile", skills.FragileCargo.ToString(CultureInfo.InvariantCulture))
            .SetScalar("urgent", skills.UrgentDelivery.ToString(CultureInfo.InvariantCulture))
            .SetScalar("mechanical", skills.EcoDriving.ToString(CultureInfo.InvariantCulture))
            .ToText();

        return await _saveEditService.ApplyAsync(
            new ScsSaveEditRequest(
                save.GameSiiPath,
                "Karriere-Skills ändern",
                updated),
            cancellationToken);
    }

    private async Task<ScsSaveEditResult> SetSaveScalarAsync(
        ScsSaveReference save,
        string key,
        string value,
        string operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(save);
        ValidateSaveReference(save);

        var source = await _codec.ReadAsync(save.GameSiiPath, cancellationToken);
        var document = ScsSiiTextDocument.Parse(source.Content);
        var updated = document.SetScalar(key, value).ToText();

        return await _saveEditService.ApplyAsync(
            new ScsSaveEditRequest(save.GameSiiPath, operation, updated),
            cancellationToken);
    }

    private static int ParseSkillScalar(
        ScsSiiTextDocument document,
        string key,
        int min,
        int max)
    {
        if (!document.TryGetScalar(key, out var value) ||
            !int.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed < min ||
            parsed > max)
        {
            throw new ScsSaveEditException(
                $"Skill-Feld '{key}' fehlt, ist mehrdeutig oder enthält einen ungültigen Wert.");
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
