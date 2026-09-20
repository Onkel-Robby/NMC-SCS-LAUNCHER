using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NmcScsLauncher.App.Services;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.ViewModels;

public partial class MainViewModel
{
    private IWorkshopContentLocator? _workshopContentLocator;
    private IWorkshopMetadataProvider? _workshopMetadataProvider;
    private IExternalUriService? _externalUriService;

    [ObservableProperty] private GameType _workshopSelectedGame = GameType.Ets2;
    [ObservableProperty] private WorkshopContentItem? _selectedWorkshopItem;
    [ObservableProperty] private string _workshopStatusText = "Workshop-Inhalte wurden noch nicht eingelesen.";
    [ObservableProperty] private string _workshopRootsText = "–";
    [ObservableProperty] private string _workshopSearchText = string.Empty;
    [ObservableProperty] private string _workshopSortMode = "Name (A–Z)";
    [ObservableProperty] private string _workshopTitleFilter = "Alle";

    public ObservableCollection<WorkshopContentItem> WorkshopItems { get; } = new();
    public ObservableCollection<WorkshopContentItem> WorkshopVisibleItems { get; } = new();

    public IReadOnlyList<GameType> WorkshopGames { get; } = Enum.GetValues<GameType>();
    public IReadOnlyList<string> WorkshopSortModes { get; } =
        new[] { "Name (A–Z)", "Zuletzt geändert (neu zuerst)", "PublishedFileId" };
    public IReadOnlyList<string> WorkshopTitleFilters { get; } =
        new[] { "Alle", "Mit Titel", "Nur ID" };

    public string WorkshopItemCountText
    {
        get
        {
            if (WorkshopVisibleItems.Count == WorkshopItems.Count)
            {
                return WorkshopItems.Count == 1
                    ? "1 lokaler Workshop-Eintrag"
                    : $"{WorkshopItems.Count} lokale Workshop-Einträge";
            }

            return $"{WorkshopVisibleItems.Count} von {WorkshopItems.Count} angezeigt";
        }
    }

    public void ConfigureWorkshopServices(
        IWorkshopContentLocator workshopContentLocator,
        IWorkshopMetadataProvider workshopMetadataProvider,
        IExternalUriService externalUriService)
    {
        _workshopContentLocator = workshopContentLocator ?? throw new ArgumentNullException(nameof(workshopContentLocator));
        _workshopMetadataProvider = workshopMetadataProvider ?? throw new ArgumentNullException(nameof(workshopMetadataProvider));
        _externalUriService = externalUriService ?? throw new ArgumentNullException(nameof(externalUriService));
    }

    [RelayCommand]
    private async Task RefreshWorkshopAsync()
    {
        if (_workshopContentLocator is null)
        {
            WorkshopStatusText = "Workshop-Dienst ist nicht konfiguriert.";
            return;
        }

        IsBusy = true;
        try
        {
            var detectedInstallation = await _gameDetector.DetectAsync(
                WorkshopSelectedGame,
                GetSavedPath(WorkshopSelectedGame));
            var preferredInstallPath = detectedInstallation?.InstallPath ?? GetSavedPath(WorkshopSelectedGame);

            var snapshot = await _workshopContentLocator.ScanAsync(
                WorkshopSelectedGame,
                preferredInstallPath);

            var enrichedItems = snapshot.Items.ToArray();
            string? metadataWarning = null;
            var loadedTitleCount = 0;

            if (_workshopMetadataProvider is not null && enrichedItems.Length > 0)
            {
                try
                {
                    var details = await _workshopMetadataProvider.GetDetailsAsync(
                        WorkshopSelectedGame,
                        enrichedItems.Select(static item => item.PublishedFileId));

                    enrichedItems = enrichedItems
                        .Select(item => details.TryGetValue(item.PublishedFileId, out var detail)
                            ? item with
                            {
                                Title = detail.Title,
                                WorkshopUpdatedAtUtc = detail.UpdatedAtUtc
                            }
                            : item)
                        .ToArray();

                    loadedTitleCount = enrichedItems.Count(static item => item.HasTitle);
                }
                catch (Exception ex)
                {
                    metadataWarning = "Steam-Titel konnten nicht geladen werden; lokale Workshop-Einträge bleiben verfügbar.";
                    await _logger.WriteAsync(
                        "WARN",
                        $"Steam Workshop metadata lookup failed for {WorkshopSelectedGame}.",
                        ex);
                }
            }

            WorkshopItems.Clear();
            foreach (var item in enrichedItems)
            {
                WorkshopItems.Add(item);
            }

            ApplyWorkshopView();

            WorkshopRootsText = snapshot.ScannedContentRoots.Count == 0
                ? "Keine lokalen Steam-Workshop-Verzeichnisse für dieses Spiel gefunden."
                : string.Join(Environment.NewLine, snapshot.ScannedContentRoots);

            var statusParts = new List<string>
            {
                $"{GameDefinition.For(WorkshopSelectedGame).DisplayName}: {WorkshopItems.Count} lokale Workshop-Einträge gefunden."
            };

            if (WorkshopItems.Count > 0)
            {
                statusParts.Add($"{loadedTitleCount} Titel über Steam geladen.");
            }

            if (snapshot.Warnings.Count > 0)
            {
                statusParts.Add(string.Join(" ", snapshot.Warnings));
            }

            if (metadataWarning is not null)
            {
                statusParts.Add(metadataWarning);
            }

            WorkshopStatusText = string.Join(" ", statusParts);

            await _logger.WriteAsync(
                "INFO",
                $"Workshop scan completed. Game={WorkshopSelectedGame}; InstallPath={preferredInstallPath ?? "<none>"}; Items={snapshot.Items.Count}; Titles={loadedTitleCount}; Roots={snapshot.ScannedContentRoots.Count}; Warnings={snapshot.Warnings.Count}");
        }
        catch (Exception ex)
        {
            WorkshopItems.Clear();
            WorkshopVisibleItems.Clear();
            SelectedWorkshopItem = null;
            WorkshopRootsText = "–";
            OnPropertyChanged(nameof(WorkshopItemCountText));
            WorkshopStatusText = "Workshop-Inhalte konnten nicht eingelesen werden. Details wurden protokolliert.";
            await _logger.WriteAsync("ERROR", $"Workshop scan failed for {WorkshopSelectedGame}.", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWorkshopItem))]
    private async Task OpenSelectedWorkshopFolderAsync()
    {
        var item = SelectedWorkshopItem;
        if (item is null) return;

        try
        {
            _explorerService.OpenFolder(item.ContentPath);
            StatusText = $"Workshop-Ordner {item.PublishedFileId} geöffnet.";
        }
        catch (Exception ex)
        {
            await HandleModsetErrorAsync("Der Workshop-Ordner konnte nicht geöffnet werden.", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWorkshopItem))]
    private async Task OpenSelectedWorkshopPageAsync()
    {
        var item = SelectedWorkshopItem;
        if (item is null || _externalUriService is null) return;

        try
        {
            _externalUriService.Open(SteamWorkshopLinks.GetItemUri(item));
            StatusText = $"Steam-Workshop-Eintrag {item.PublishedFileId} im Browser geöffnet.";
        }
        catch (Exception ex)
        {
            await HandleModsetErrorAsync("Der Steam-Workshop-Eintrag konnte nicht geöffnet werden.", ex);
        }
    }

    partial void OnWorkshopSelectedGameChanged(GameType value)
    {
        WorkshopItems.Clear();
        WorkshopVisibleItems.Clear();
        SelectedWorkshopItem = null;
        WorkshopRootsText = "–";
        WorkshopStatusText = $"{GameDefinition.For(value).DisplayName}: zum Einlesen auf „Aktualisieren“ klicken.";
        OnPropertyChanged(nameof(WorkshopItemCountText));
    }

    partial void OnWorkshopSearchTextChanged(string value) => ApplyWorkshopView();
    partial void OnWorkshopSortModeChanged(string value) => ApplyWorkshopView();
    partial void OnWorkshopTitleFilterChanged(string value) => ApplyWorkshopView();

    partial void OnSelectedWorkshopItemChanged(WorkshopContentItem? value)
    {
        OpenSelectedWorkshopFolderCommand.NotifyCanExecuteChanged();
        OpenSelectedWorkshopPageCommand.NotifyCanExecuteChanged();
    }

    private void ApplyWorkshopView()
    {
        var selectedId = SelectedWorkshopItem?.PublishedFileId;
        var search = WorkshopSearchText.Trim();

        IEnumerable<WorkshopContentItem> query = WorkshopItems;

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(item =>
                item.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || item.PublishedFileId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                || item.ContentPath.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        query = WorkshopTitleFilter switch
        {
            "Mit Titel" => query.Where(static item => item.HasTitle),
            "Nur ID" => query.Where(static item => !item.HasTitle),
            _ => query
        };

        query = WorkshopSortMode switch
        {
            "Zuletzt geändert (neu zuerst)" => query
                .OrderByDescending(static item => item.DisplayLastUpdatedUtc)
                .ThenBy(static item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            "PublishedFileId" => query.OrderBy(static item => item.PublishedFileId),
            _ => query
                .OrderBy(static item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(static item => item.PublishedFileId)
        };

        WorkshopVisibleItems.Clear();
        foreach (var item in query)
        {
            WorkshopVisibleItems.Add(item);
        }

        SelectedWorkshopItem = selectedId is ulong id
            ? WorkshopVisibleItems.FirstOrDefault(item => item.PublishedFileId == id)
            : WorkshopVisibleItems.FirstOrDefault();

        OnPropertyChanged(nameof(WorkshopItemCountText));
    }

    private bool HasSelectedWorkshopItem() => SelectedWorkshopItem is not null;
}
