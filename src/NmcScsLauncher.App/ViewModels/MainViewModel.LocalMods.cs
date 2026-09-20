using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.ViewModels;

public sealed record LocalModListItem(LocalModInfo Source)
{
    public string Name => Source.Name;
    public string Path => Source.Path;
    public LocalModKind Kind => Source.Kind;
    public string TypeText => Source.Kind == LocalModKind.ScsPackage ? ".scs" : "Ordner";
    public long? SizeBytes => Source.SizeBytes;
    public string SizeText => FormatSize(Source.SizeBytes);
    public DateTimeOffset LastWriteTimeUtc => Source.LastWriteTimeUtc;

    private static string FormatSize(long? bytes)
    {
        if (bytes is null)
        {
            return "–";
        }

        var value = (double)bytes.Value;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unitIndex = 0;

        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes.Value} {units[unitIndex]}"
            : $"{value:0.##} {units[unitIndex]}";
    }
}

public partial class MainViewModel
{
    [ObservableProperty] private LocalModListItem? _selectedLocalMod;
    [ObservableProperty] private string _localModSearchText = string.Empty;
    [ObservableProperty] private string _localModSortMode = "Name (A–Z)";
    [ObservableProperty] private string _localModTypeFilter = "Alle";

    public ObservableCollection<LocalModListItem> LocalModItems { get; } = new();
    public ObservableCollection<LocalModListItem> LocalModVisibleItems { get; } = new();

    public IReadOnlyList<string> LocalModSortModes { get; } =
        new[] { "Name (A–Z)", "Zuletzt geändert (neu zuerst)", "Größe (groß zuerst)", "Typ" };

    public IReadOnlyList<string> LocalModTypeFilters { get; } =
        new[] { "Alle", ".scs", "Ordner" };

    public string LocalModItemCountText
    {
        get
        {
            if (LocalModVisibleItems.Count == LocalModItems.Count)
            {
                return LocalModItems.Count == 1
                    ? "1 lokaler Mod"
                    : $"{LocalModItems.Count} lokale Mods";
            }

            return $"{LocalModVisibleItems.Count} von {LocalModItems.Count} angezeigt";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedLocalMod))]
    private async Task OpenSelectedLocalModAsync()
    {
        var item = SelectedLocalMod;
        if (item is null)
        {
            return;
        }

        try
        {
            _explorerService.RevealPath(item.Path);
            StatusText = item.Kind == LocalModKind.ScsPackage
                ? $"Mod-Datei '{item.Name}' im Explorer markiert."
                : $"Mod-Ordner '{item.Name}' geöffnet.";
        }
        catch (Exception ex)
        {
            await HandleModsetErrorAsync("Der ausgewählte lokale Mod konnte nicht im Explorer geöffnet werden.", ex);
        }
    }

    partial void OnLocalModSearchTextChanged(string value) => ApplyLocalModView();
    partial void OnLocalModSortModeChanged(string value) => ApplyLocalModView();
    partial void OnLocalModTypeFilterChanged(string value) => ApplyLocalModView();

    partial void OnSelectedLocalModChanged(LocalModListItem? value)
    {
        OpenSelectedLocalModCommand.NotifyCanExecuteChanged();
    }

    private void SetLocalMods(IEnumerable<LocalModInfo> mods)
    {
        var selectedPath = SelectedLocalMod?.Path;

        LocalModItems.Clear();
        foreach (var mod in mods)
        {
            LocalModItems.Add(new LocalModListItem(mod));
        }

        ApplyLocalModView(selectedPath);
    }

    private void ClearLocalMods()
    {
        LocalModItems.Clear();
        LocalModVisibleItems.Clear();
        SelectedLocalMod = null;
        OnPropertyChanged(nameof(LocalModItemCountText));
    }

    private void ApplyLocalModView(string? preferredSelectedPath = null)
    {
        var selectedPath = preferredSelectedPath ?? SelectedLocalMod?.Path;
        var search = LocalModSearchText.Trim();

        IEnumerable<LocalModListItem> query = LocalModItems;

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(item =>
                item.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || item.Path.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        query = LocalModTypeFilter switch
        {
            ".scs" => query.Where(static item => item.Kind == LocalModKind.ScsPackage),
            "Ordner" => query.Where(static item => item.Kind == LocalModKind.ExtractedDirectory),
            _ => query
        };

        query = LocalModSortMode switch
        {
            "Zuletzt geändert (neu zuerst)" => query
                .OrderByDescending(static item => item.LastWriteTimeUtc)
                .ThenBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "Größe (groß zuerst)" => query
                .OrderByDescending(static item => item.SizeBytes ?? -1L)
                .ThenBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "Typ" => query
                .OrderBy(static item => item.Kind)
                .ThenBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => query
                .OrderBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(static item => item.Kind)
        };

        LocalModVisibleItems.Clear();
        foreach (var item in query)
        {
            LocalModVisibleItems.Add(item);
        }

        SelectedLocalMod = !string.IsNullOrWhiteSpace(selectedPath)
            ? LocalModVisibleItems.FirstOrDefault(item =>
                string.Equals(item.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
            : LocalModVisibleItems.FirstOrDefault();

        OnPropertyChanged(nameof(LocalModItemCountText));
    }

    private bool HasSelectedLocalMod() => SelectedLocalMod is not null;
}
