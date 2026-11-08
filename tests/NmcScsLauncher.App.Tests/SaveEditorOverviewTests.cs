using System.Xml.Linq;
using NmcScsLauncher.App;
using NmcScsLauncher.App.ViewModels;
using Xunit;

namespace NmcScsLauncher.App.Tests;

public sealed class SaveEditorOverviewTests
{
    [Theory]
    [InlineData("profile", 1)]
    [InlineData("truck", 2)]
    [InlineData("trailer", 4)]
    [InlineData("plates", 5)]
    [InlineData("backup", 6)]
    [InlineData("unknown", 0)]
    public void QuickAccessCardsNavigateToExistingEditorSections(string section, int expectedIndex)
    {
        var vm = new SaveEditorTestHarness().ViewModel;
        vm.OpenSaveEditorSectionCommand.Execute(section);
        Assert.Equal(expectedIndex, vm.SaveEditorSectionIndex);
    }

    [Fact]
    public void OverviewUsesThreeEqualColumnsWithoutNarrowFixedCardWidths()
    {
        using var stream = typeof(SaveEditorOverviewTests).Assembly
            .GetManifestResourceStream("NmcScsLauncher.Tests.MainWindow.xaml")!;
        var document = XDocument.Load(stream);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var cards = document.Descendants(presentation + "ItemsControl").Single(element =>
            ((string?)element.Attribute("ItemsSource"))?.Contains("SaveEditorOverviewActions", StringComparison.Ordinal) == true);
        var panel = cards.Descendants(presentation + "UniformGrid").Single();
        Assert.Equal("3", (string?)panel.Attribute("Columns"));
        Assert.Empty(cards.Descendants(presentation + "WrapPanel"));
        var card = cards.Descendants(presentation + "DataTemplate").Single()
            .Elements(presentation + "Border").Single();
        Assert.Null(card.Attribute("Width"));
        Assert.Equal("288", (string?)card.Attribute("Height"));
        Assert.Equal("1160", (string?)cards.Attribute("MaxWidth"));
    }

    [Fact]
    public void AllOverviewCardsUseKnownNonPowertrainDestinations()
    {
        var vm = new SaveEditorTestHarness().ViewModel;
        Assert.Equal(0, vm.SaveEditorSectionIndex);
        Assert.Equal(11, vm.SaveEditorOverviewActions.Count);
        Assert.DoesNotContain(vm.SaveEditorOverviewActions, action =>
            action.Section is "powertrain" or "catalog");
        foreach (var action in vm.SaveEditorOverviewActions)
        {
            Assert.False(string.IsNullOrWhiteSpace(action.Title));
            Assert.False(string.IsNullOrWhiteSpace(action.Description));
            vm.OpenSaveEditorSectionCommand.Execute(action.Section);
            Assert.Contains(vm.SaveEditorSectionIndex, new[] { 1, 2, 4, 5, 6 });
        }
        Assert.False(vm.PowertrainFeaturesEnabled);
    }
    [Fact]
    public void AllCardsOpenKnownModalActionsRatherThanOnlyNavigatingToTabs()
    {
        var vm = new SaveEditorTestHarness().ViewModel;
        var actionIds = vm.SaveEditorOverviewActions.Select(action => action.ActionId).ToArray();
        Assert.Equal(11, actionIds.Length);
        Assert.Equal(actionIds.Length, actionIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(actionIds, actionId => Assert.Contains(actionId, SaveEditorActionDialog.SupportedActionIds));
        Assert.DoesNotContain(actionIds, id => id is "powertrain" or "engine" or "transmission");

        using var stream = typeof(SaveEditorOverviewTests).Assembly
            .GetManifestResourceStream("NmcScsLauncher.Tests.MainWindow.xaml")!;
        var document = XDocument.Load(stream);
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var cards = document.Descendants(p + "ItemsControl").Single(e =>
            ((string?)e.Attribute("ItemsSource"))?.Contains("SaveEditorOverviewActions", StringComparison.Ordinal) == true);
        var button = cards.Descendants(p + "Button").Single();
        Assert.Equal("OpenSaveEditorActionClick", (string?)button.Attribute("Click"));
        Assert.Null(button.Attribute("Command"));
    }

    [Fact]
    public void ModalDialogsUseOriginalSaveEditCommandsWithoutDirectFileWrites()
    {
        using var stream = typeof(SaveEditorOverviewTests).Assembly
            .GetManifestResourceStream("NmcScsLauncher.Tests.SaveEditorActionDialog.xaml")!;
        var document = XDocument.Load(stream);
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var panelNames = document.Descendants(p + "StackPanel")
            .Select(e => (string?)e.Attribute(x + "Name")).Where(s => s is not null).ToArray();
        foreach (var panel in new[] { "MoneyPanel", "ExperiencePanel", "SkillsPanel",
            "TruckSwitchPanel", "TruckRepairPanel", "FuelPanel", "MileagePanel", "TrailerPanel",
            "CargoPanel", "PlatesPanel", "BackupPanel" })
            Assert.Contains(panel, panelNames);

        var commandBindings = document.Descendants(p + "Button")
            .Select(e => (string?)e.Attribute("Command")).Where(e => e is not null).ToArray();
        foreach (var command in new[] {
            "SetSaveEditorMoneyCommand", "SetSaveEditorExperienceCommand", "SetSaveEditorCareerSkillsCommand",
            "SwitchSaveEditorTruckCommand", "RepairSaveEditorTruckCommand", "SetSaveEditorFuelCommand",
            "SetSaveEditorMileageCommand", "SwitchSaveEditorTrailerCommand", "RepairSaveEditorTrailerCommand",
            "SetSaveEditorCargoMassCommand", "SetSaveEditorTruckPlateCommand",
            "SetSaveEditorTrailerPlateCommand", "RestoreSaveEditorLastBackupCommand" })
            Assert.Contains(commandBindings, binding => binding!.Contains(command, StringComparison.Ordinal));
    }

    [Fact]
    public void CardPreviewAtlasUsesElevenDistinctCropsAndRealImageBrush()
    {
        var vm = new SaveEditorTestHarness().ViewModel;
        Assert.Equal(11, vm.SaveEditorOverviewActions.Select(action => action.PreviewSlot)
            .Distinct().Count());
        foreach (var action in vm.SaveEditorOverviewActions)
        {
            var rect = action.PreviewViewbox;
            Assert.InRange(rect.X, 0, 1);
            Assert.InRange(rect.Y, 0, 1);
            Assert.True(rect.Width > 0);
            Assert.True(rect.Height > 0);
            Assert.True(rect.X + rect.Width <= 1.000001);
            Assert.True(rect.Y + rect.Height <= 1.000001);
        }

        using var stream = typeof(SaveEditorOverviewTests).Assembly
            .GetManifestResourceStream("NmcScsLauncher.Tests.MainWindow.xaml")!;
        var document = XDocument.Load(stream);
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var brush = document.Descendants(p + "ImageBrush").Single(e =>
            ((string?)e.Attribute("ImageSource"))?.Contains("SaveEditorPreviews.png", StringComparison.Ordinal) == true);
        Assert.Equal("{Binding PreviewViewbox}", (string?)brush.Attribute("Viewbox"));
        Assert.Equal("RelativeToBoundingBox", (string?)brush.Attribute("ViewboxUnits"));
    }

}
