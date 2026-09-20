using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NmcScsLauncher.App.Services;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.ViewModels;

public partial class MainViewModel
{
    private IWorkshopContentLocator? _workshopContentLocator;
    private IExternalUriService? _externalUriService;

    [ObservableProperty] private GameType _workshopSelectedGame = GameType.Ets2;
    [ObservableProperty] private WorkshopContentItem? _selectedWorkshopItem;
    [ObservableProperty] private string _workshopStatusText = "Workshop-Inhalte wurden noch nicht eingelesen.";
    [ObservableProperty] private string _workshopRootsText = "–";

    public ObservableCollection<WorkshopContentItem> WorkshopItems { get; } = new();
    public IReadOnlyList<GameType> WorkshopGames { get; } = Enum.GetValues<GameType>();

    public string WorkshopItemCountText => WorkshopItems.Count == 1
        ? "1 lokaler Workshop-Eintrag"
        : $"{WorkshopItems.Count} lokale Workshop-Einträge";

    public void ConfigureWorkshopServices(
        IWorkshopContentLocator workshopContentLocator,
        IExternalUriService externalUriService)
    {
        _workshopContentLocator = workshopContentLocator ?? throw new ArgumentNullException(nameof(workshopContentLocator));
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
            var snapshot = await _workshopContentLocator.ScanAsync(WorkshopSelectedGame);
            WorkshopItems.Clear();
            foreach (var item in snapshot.Items)
            {
                WorkshopItems.Add(item);
            }

            WorkshopRootsText = snapshot.ScannedContentRoots.Count == 0
                ? "Keine lokalen Steam-Workshop-Verzeichnisse für dieses Spiel gefunden."
                : string.Join(Environment.NewLine, snapshot.ScannedContentRoots);

            WorkshopStatusText = snapshot.Warnings.Count == 0
                ? $"{GameDefinition.For(WorkshopSelectedGame).DisplayName}: {WorkshopItemCountText} gefunden."
                : $"{GameDefinition.For(WorkshopSelectedGame).DisplayName}: {WorkshopItemCountText}. {string.Join(" ", snapshot.Warnings)}";

            OnPropertyChanged(nameof(WorkshopItemCountText));
            SelectedWorkshopItem = WorkshopItems.FirstOrDefault();
            await _logger.WriteAsync(
                "INFO",
                $"Workshop scan completed. Game={WorkshopSelectedGame}; Items={snapshot.Items.Count}; Roots={snapshot.ScannedContentRoots.Count}; Warnings={snapshot.Warnings.Count}");
        }
        catch (Exception ex)
        {
            WorkshopItems.Clear();
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
        SelectedWorkshopItem = null;
        WorkshopRootsText = "–";
        WorkshopStatusText = $"{GameDefinition.For(value).DisplayName}: zum Einlesen auf „Aktualisieren“ klicken.";
        OnPropertyChanged(nameof(WorkshopItemCountText));
    }

    partial void OnSelectedWorkshopItemChanged(WorkshopContentItem? value)
    {
        OpenSelectedWorkshopFolderCommand.NotifyCanExecuteChanged();
        OpenSelectedWorkshopPageCommand.NotifyCanExecuteChanged();
    }

    private bool HasSelectedWorkshopItem() => SelectedWorkshopItem is not null;
}
