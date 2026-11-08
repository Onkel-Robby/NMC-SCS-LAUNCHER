using System.Text;
using System.Windows;
using Microsoft.Win32;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.Services;

public interface ITruckPowertrainDiagnosticExportService
{
    string? SelectDestination(GameType game);
    Task WriteAsync(string destination, string report, CancellationToken cancellationToken);
}

public sealed class TruckPowertrainDiagnosticExportService : ITruckPowertrainDiagnosticExportService
{
    public string? SelectDestination(GameType game)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Truck-Diagnose lokal speichern", Filter = "JSON-Diagnose (*.json)|*.json",
            DefaultExt = ".json", AddExtension = true, OverwritePrompt = true,
            FileName = $"NMC-Truck-Diagnose-{(game == GameType.Ets2 ? "ETS2" : "ATS")}.json"
        };
        var accepted = Application.Current?.MainWindow is { IsVisible: true } owner
            ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return accepted == true ? dialog.FileName : null;
    }

    public async Task WriteAsync(string destination, string report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Encoding.UTF8.GetByteCount(report) > TruckPowertrainDiagnosticReport.MaximumBytes)
            throw new InvalidDataException("Der Diagnosebericht überschreitet das Größenlimit.");
        var fullPath = Path.GetFullPath(destination);
        if (new Uri(fullPath).IsUnc || !string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Bitte eine lokale JSON-Datei auswählen.");
        var temporaryPath = Path.Combine(Path.GetDirectoryName(fullPath)!, ".nmc-diagnose-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, report, new UTF8Encoding(false), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
