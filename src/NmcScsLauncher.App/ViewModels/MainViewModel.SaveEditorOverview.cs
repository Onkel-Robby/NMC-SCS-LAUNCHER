using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;

namespace NmcScsLauncher.App.ViewModels;

/// <summary>Display-only quick access metadata. All edits still run through the existing Save Editor commands.</summary>
public sealed record SaveEditorOverviewAction(
    string Category, string Title, string Description, string Symbol, string Accent, string Section, string ActionId, int PreviewSlot)
{
    // Original 3x4 atlas; one independent illustrative scene per action.
    public Rect PreviewViewbox => new((PreviewSlot % 3) / 3d, (PreviewSlot / 3) / 4d, 1d / 3, 1d / 4);
}

/// <summary>Navigation tile for a Save-Editor category, without any save write operation.</summary>
public sealed record SaveEditorCategory(string Title, string Description, string Section, string Accent, int Count);

public partial class MainViewModel
{
    // The existing component catalog is deliberately retained at index 3 but hidden by its release feature gate.
    [ObservableProperty] private int _saveEditorSectionIndex;

    public IReadOnlyList<SaveEditorOverviewAction> SaveEditorOverviewActions { get; } =
    [
        new("PROFIL & KARRIERE", "Geld verwalten", "Kontostand im ausgewählten Profil bearbeiten.", "€", "#4FA9FF", "profile", "money", 0),
        new("PROFIL & KARRIERE", "Erfahrungspunkte", "XP sicher lesen und ändern.", "XP", "#80B4FF", "profile", "experience", 1),
        new("PROFIL & KARRIERE", "Fähigkeiten", "ADR, Fernfahrten und weitere Skills verwalten.", "★", "#FFCF74", "profile", "skills", 2),
        new("LKW", "Truck wechseln", "Einen vorhandenen eigenen Truck auswählen.", "LKW", "#4FA9FF", "truck", "truck-switch", 3),
        new("LKW", "Truck reparieren", "Beschädigungen am aktiven Truck beheben.", "+", "#5AD3B4", "truck", "truck-repair", 4),
        new("LKW", "Kraftstoff", "Tankfüllstand des aktiven Trucks setzen.", "⛽", "#5AD3B4", "truck", "fuel", 5),
        new("LKW", "Kilometerstand", "Tacho und Truck-Manager synchron halten.", "KM", "#80B4FF", "truck", "mileage", 6),
        new("ANHÄNGER", "Trailer verwalten", "Trailer wechseln und reparieren.", "TR", "#C59BFF", "trailer", "trailer", 7),
        new("ANHÄNGER", "Frachtgewicht", "Gewicht der aktiven Trailer-Kette anpassen.", "KG", "#C59BFF", "trailer", "cargo", 8),
        new("KENNZEICHEN", "Kennzeichen", "Truck- und Trailer-Kennzeichen gestalten.", "ID", "#FFCF74", "plates", "plates", 9),
        new("SICHERUNGEN", "Backup wiederherstellen", "Letzte geprüfte Sicherung bei Bedarf laden.", "↺", "#5AD3B4", "backup", "backup", 10)
    ];

    // Keep the exact established tab indices (the disabled catalog still occupies slot 3).
    // Only the visible card location changes; dialogs, commands and preview slots do not.
    public IReadOnlyList<SaveEditorOverviewAction> SaveEditorProfileActions =>
        SaveEditorOverviewActions.Where(action => action.Section == "profile").ToArray();
    public IReadOnlyList<SaveEditorOverviewAction> SaveEditorTruckActions =>
        SaveEditorOverviewActions.Where(action => action.Section == "truck").ToArray();
    public IReadOnlyList<SaveEditorOverviewAction> SaveEditorTrailerActions =>
        SaveEditorOverviewActions.Where(action => action.Section == "trailer").ToArray();
    public IReadOnlyList<SaveEditorOverviewAction> SaveEditorPlateActions =>
        SaveEditorOverviewActions.Where(action => action.Section == "plates").ToArray();
    public IReadOnlyList<SaveEditorOverviewAction> SaveEditorBackupActions =>
        SaveEditorOverviewActions.Where(action => action.Section == "backup").ToArray();

    public IReadOnlyList<SaveEditorCategory> SaveEditorCategories { get; } =
    [
        new("Profil & Karriere", "Geld, Erfahrung und Fähigkeiten", "profile", "#4FA9FF", 3),
        new("Truck", "Truck-Wechsel, Reparatur, Kraftstoff und Kilometerstand", "truck", "#5AD3B4", 4),
        new("Trailer", "Trailer-Wechsel, Reparatur und Frachtgewicht", "trailer", "#C59BFF", 2),
        new("Kennzeichen", "Kennzeichen für Truck und Trailer", "plates", "#FFCF74", 1),
        new("Sicherungen", "Sicherungsstand und Wiederherstellung", "backup", "#5AD3B4", 1)
    ];

    [RelayCommand]
    private void OpenSaveEditorSection(string? section)
    {
        SaveEditorSectionIndex = section switch
        {
            "profile" => 1,
            "truck" => 2,
            "trailer" => 4,
            "plates" => 5,
            "backup" => 6,
            _ => 0
        };
    }
}
