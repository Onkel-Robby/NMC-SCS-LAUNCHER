using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NmcScsLauncher.App.Services;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.ViewModels;

public partial class MainViewModel
{
    private ITruckPowertrainDiagnosticExportService? _truckPowertrainDiagnosticExport;
    private CancellationTokenSource? _truckPowertrainExportCancellation;
    [ObservableProperty] private bool _truckPowertrainConfigurationBlocked;
    [ObservableProperty] private string _truckPowertrainInspectionSummary = string.Empty;
    [ObservableProperty] private string _truckPowertrainExportStatus = string.Empty;
    [ObservableProperty] private string _truckPowertrainSourceDetails = string.Empty;
    public ObservableCollection<TruckPowertrainDiagnosticViewModel> TruckPowertrainDiagnosticIssues { get; } = new();

    public void ConfigureTruckPowertrainDiagnosticExport(ITruckPowertrainDiagnosticExportService service)
    {
        _truckPowertrainDiagnosticExport = service ?? throw new ArgumentNullException(nameof(service));
        ExportTruckPowertrainDiagnosticsCommand.NotifyCanExecuteChanged();
    }

    private void SetTruckPowertrainDiagnostics(ScsEffectiveContentRequest request, IScsTruckPowertrainPreview preview)
    {
        var report = preview.InspectionReport;
        TruckPowertrainDiagnosticIssues.Clear();
        var issues = report?.Issues ?? [];
        foreach (var issue in issues.OrderBy(TruckPowertrainDiagnosticReport.DiagnosticPriority).Take(128))
            TruckPowertrainDiagnosticIssues.Add(new(issue));
        TruckPowertrainConfigurationBlocked = report is { ConfigurationVerified: false } or { DefinitionsAttempted: false } or { ResultsInvalidated: true };
        var packageCount = issues.Where(issue => issue.BlocksConfiguration && issue.PackageIdentity is not null)
            .Select(issue => issue.PackageIdentity).Distinct(StringComparer.Ordinal).Count();
        var packageText = packageCount == 1 ? "1 Paket blockiert" : $"{packageCount} Pakete blockieren";
        TruckPowertrainInspectionSummary = report switch
        {
            { ResultsInvalidated: true } => "Prüfergebnis verworfen: Die Komponenten wurden bereits geprüft, aber die abschließende Save- oder Quellenprüfung ist fehlgeschlagen. " +
                "Der Einbau bleibt gesperrt. Bitte den Spielstand neu laden; die Gründe stehen unten.",
            { DefinitionsAttempted: false } => "Globale Prüfung gesperrt: Die Komponentenprüfung wurde nicht begonnen. " +
                (packageCount > 0 ? packageText + " die aktive Konfiguration. " : "Eine Voraussetzung für die Komponentenprüfung ist nicht bestätigt; die Gründe stehen unten. ") +
                "Angezeigt werden nur die im Save vermerkten Komponenten; zusätzliche Archivfunde sind noch keine Einbaufreigabe.",
            { ConfigurationVerified: false } => "Die aktive Konfiguration ist nicht vollständig bestätigt. Einbau gesperrt.",
            { ConfigurationVerified: true } => $"Aktive Konfiguration geprüft · {report.ActiveMods.Count} aktive Mods · " + SourceCounts(report) + ". " +
                "Komponenten und gewählte Kombination werden getrennt bewertet.",
            _ => "Für diese Vorschau liegt kein strukturierter Diagnosebericht vor."
        };
        var context = "Auswahl im Launcher; zuletzt im Spiel verwendeter Save nicht bestätigt." +
            Environment.NewLine + "Profil: " + request.Profile.DisplayName + " (" + request.Profile.StorageKind + ")" +
            Environment.NewLine + "Profildatei: " + request.Profile.ProfileSiiPath +
            Environment.NewLine + "Profil-Home: " + request.Profile.GameHomeDirectory +
            Environment.NewLine + "Save: " + request.Save.DisplayName + " (" + request.Save.Kind + ")" +
            Environment.NewLine + "Save-Datei: " + request.Save.GameSiiPath +
            Environment.NewLine + "Save-Zeitstempel beim Laden (UTC): " + request.Save.LastWriteTimeUtc.ToUniversalTime().ToString("O") +
            Environment.NewLine + "Spielversion: " + (string.IsNullOrEmpty(report?.GameVersion) ? "nicht ermittelt" : report.GameVersion) +
            Environment.NewLine + "Spielinstallation: " + (request.PreferredInstallPath ?? "automatisch ermittelt") +
            Environment.NewLine + string.Join(Environment.NewLine, request.ModDirectories?.Select(root => "Mod-Ordner: " + root.DirectoryPath) ?? []);
        var sourceLines = report?.Sources.Take(256).Select(source =>
            $"{source.Kind} · {source.PackageIdentity} · Version {source.PackageVersion} · Priorität {source.Priority}\n{source.PhysicalPath}\n" +
            $"Verfügbarkeit: {(source.AvailabilityVerified ? "bestätigt" : source.AvailabilityReason)}") ?? [];
        var modLines = report?.ActiveMods.Take(256).Select(mod => $"{mod.LoadOrder}: {mod.DisplayName} ({mod.Identity})") ?? [];
        TruckPowertrainSourceDetails = context + $"\n\nAktive Mods aus profile.sii ({report?.ActiveMods.Count ?? 0}) · Ladereihenfolge:\n" +
            string.Join("\n", modLines) + SaveDependencyDetails(report) +
            "\n\nQuellen · " + SourceCounts(report) + ":\n" + string.Join("\n\n", sourceLines);
        ExportTruckPowertrainDiagnosticsCommand.NotifyCanExecuteChanged();
    }

    private static string SourceCounts(ScsTruckPowertrainInspectionReport? report)
    {
        var sources = report?.Sources ?? [];
        return $"Spielarchive: {sources.Count(source => source.Kind == ScsPowertrainSourceKind.Game)} · " +
            $"DLC-Archive: {sources.Count(source => source.Kind == ScsPowertrainSourceKind.Dlc)} · " +
            $"Lokale Mod-Quellen: {sources.Count(source => source.Kind == ScsPowertrainSourceKind.LocalMod)} · " +
            $"Workshop-Quellen: {sources.Count(source => source.Kind == ScsPowertrainSourceKind.Workshop)}";
    }

    private static string SaveDependencyDetails(ScsTruckPowertrainInspectionReport? report)
    {
        if (report?.SaveDependencies is null) return "\n\nSave-Abhängigkeiten aus info.sii: nicht ermittelt.";
        var sections = new List<string>();
        foreach (var kind in new[] { ScsSaveDependencyKind.Mod, ScsSaveDependencyKind.Dlc, ScsSaveDependencyKind.Rdlc })
        {
            var dependencies = report.SaveDependencies.Where(item => item.Kind == kind).ToArray();
            var title = kind switch
            {
                ScsSaveDependencyKind.Mod => "Mod-Historie",
                ScsSaveDependencyKind.Dlc => "DLC-Abhängigkeiten",
                _ => "RDLC-Abhängigkeiten"
            };
            var lines = dependencies.Take(256).Select(dependency =>
            {
                var match = TruckPowertrainDependencyDiagnostics.Describe(dependency, report);
                var status = match.Status switch
                {
                    "Active" => "im ausgewählten Profil als aktiv eingetragen",
                    "Historical" => "nur im Save vermerkt; nicht im aktiven Profil",
                    "Matched" => "1 Archiv mit exakt passender Manifest-ID",
                    "Ambiguous" => $"{match.Sources.Count} Archive mit exakt passender Manifest-ID; Zuordnung mehrdeutig",
                    _ => "kein Archiv mit exakt passender Manifest-ID"
                };
                return $"{dependency.Index}: {dependency.DisplayName} ({dependency.Identity}) · {status}" +
                    string.Concat(match.Sources.Take(16).Select(source => "\n  " + source.PhysicalPath));
            });
            sections.Add($"{title} aus info.sii ({dependencies.Length}):\n" + string.Join("\n", lines));
        }
        return "\n\n" + string.Join("\n\n", sections) +
            "\nDLC-Zuordnung: Datei vorhanden bestätigt keine aktuelle Berechtigung oder Aktivierung.";
    }

    private void ClearTruckPowertrainDiagnostics()
    {
        _truckPowertrainExportCancellation?.Cancel();
        TruckPowertrainDiagnosticIssues.Clear();
        TruckPowertrainConfigurationBlocked = false;
        TruckPowertrainInspectionSummary = string.Empty;
        TruckPowertrainSourceDetails = string.Empty;
        TruckPowertrainExportStatus = string.Empty;
        ExportTruckPowertrainDiagnosticsCommand.NotifyCanExecuteChanged();
    }

    private bool CanExportTruckPowertrainDiagnostics() => _truckPowertrainDiagnosticExport is not null &&
        _truckPowertrainPreview is not null && _truckPowertrainInstallContext is not null && !TruckPowertrainIsLoading &&
        !IsBusy && !SaveEditorIsInspecting && _truckPowertrainContextMatches?.Invoke() == true;

    [RelayCommand(CanExecute = nameof(CanExportTruckPowertrainDiagnostics))]
    private async Task ExportTruckPowertrainDiagnosticsAsync()
    {
        if (!CanExportTruckPowertrainDiagnostics()) return;
        var generation = _truckPowertrainGeneration;
        var preview = _truckPowertrainPreview!;
        var request = _truckPowertrainInstallContext!.Content;
        var service = _truckPowertrainDiagnosticExport!;
        using var cancellation = new CancellationTokenSource();
        _truckPowertrainExportCancellation = cancellation;
        try
        {
            ScsTruckPowertrainAssessment? selectedPair = null;
            var engine = TruckPowertrainSelectedEngine;
            var transmission = TruckPowertrainSelectedTransmission;
            if (engine is not null && transmission is not null &&
                _truckEngineOptions.Contains(engine) && _truckTransmissionOptions.Contains(transmission))
            {
                selectedPair = preview.AssessPair(engine.DataPath, transmission.DataPath);
                if (selectedPair.EngineDataPath != engine.DataPath || selectedPair.TransmissionDataPath != transmission.DataPath)
                    throw new InvalidDataException("Die Kombinationsprüfung gehört nicht zur Auswahl.");
            }
            var report = TruckPowertrainDiagnosticReport.Create(request, preview, selectedPair);
            var destination = service.SelectDestination(request.Profile.GameType);
            if (destination is null || generation != _truckPowertrainGeneration ||
                !ReferenceEquals(preview, _truckPowertrainPreview) || !CanExportTruckPowertrainDiagnostics()) return;
            await service.WriteAsync(destination, report, cancellation.Token);
            if (generation == _truckPowertrainGeneration)
                TruckPowertrainExportStatus = "Diagnose lokal gespeichert. Es wurden keine Dateien hochgeladen.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or ScsSaveEditException)
        {
            if (generation == _truckPowertrainGeneration)
                TruckPowertrainExportStatus = "Der lokale Diagnosebericht konnte nicht gespeichert werden. Bitte einen anderen Speicherort auswählen.";
        }
        finally
        {
            if (ReferenceEquals(_truckPowertrainExportCancellation, cancellation)) _truckPowertrainExportCancellation = null;
        }
    }
}

public sealed class TruckPowertrainDiagnosticViewModel
{
    public TruckPowertrainDiagnosticViewModel(ScsPowertrainDiagnostic issue)
    {
        var scope = issue.BlocksConfiguration ? "Globale Sperre" : issue.Scope switch
        {
            ScsPowertrainDiagnosticScope.Source => "Quelle", ScsPowertrainDiagnosticScope.Component => "Komponente",
            ScsPowertrainDiagnosticScope.Pair => "Kombination", _ => "Prüfung"
        };
        Heading = scope + " · " + StageName(issue.Stage) +
            (string.IsNullOrWhiteSpace(issue.DisplayName ?? issue.PackageIdentity) ? "" : " · " + (issue.DisplayName ?? issue.PackageIdentity));
        Message = Regex.Replace(Limit(issue.Message, 2000), @"(?:[a-zA-Z]:[\\/]|\\\\)[^\r\n\""<>|]+", "[lokaler Pfad – siehe Details]");
        TechnicalText = string.Join(Environment.NewLine, new[]
        {
            "Code: " + issue.Code, "Stufe: " + issue.Stage, "Bereich: " + issue.Scope,
            "Paket: " + issue.PackageIdentity, "Name: " + issue.DisplayName, "Version: " + issue.PackageVersion,
            "Quelle: " + issue.PhysicalPath, "Definition: " + issue.VirtualPath,
            "Meldung: " + issue.Message, issue.TechnicalDetails
        }.Where(value => !string.IsNullOrEmpty(value)).Select(value => Limit(value!, 4000)));
    }
    public string Heading { get; }
    public string Message { get; }
    public string TechnicalText { get; }
    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length] + " …";
    private static string StageName(ScsPowertrainDiagnosticStage stage) => stage switch
    {
        ScsPowertrainDiagnosticStage.ArchiveDiscovery => "Archiv lesen", ScsPowertrainDiagnosticStage.ActiveConfiguration => "Aktive Mods",
        ScsPowertrainDiagnosticStage.EffectiveDefinition => "Wirksame Definition", ScsPowertrainDiagnosticStage.TruckModel => "Truck-Modell",
        ScsPowertrainDiagnosticStage.AccessoryRules => "Zubehörregeln", ScsPowertrainDiagnosticStage.PairCompatibility => "Kombination",
        ScsPowertrainDiagnosticStage.Installation => "Einbau", _ => "Prüfung"
    };
}
