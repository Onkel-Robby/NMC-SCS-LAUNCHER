using System.Xml.Linq;
using System.Windows.Media.Imaging;
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
    public void CategoryPagesShareThreeColumnCardTemplateWithoutNarrowFixedWidths()
    {
        using var stream = typeof(SaveEditorOverviewTests).Assembly
            .GetManifestResourceStream("NmcScsLauncher.Tests.MainWindow.xaml")!;
        var document = XDocument.Load(stream);
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var template = document.Descendants(p + "DataTemplate").Single(element =>
            (string?)element.Attribute(x + "Key") == "SaveEditorActionCardTemplate");
        var card = template.Elements(p + "Border").Single();
        Assert.Null(card.Attribute("Width"));
        Assert.Equal("288", (string?)card.Attribute("Height"));

        var cardPanel = document.Descendants(p + "ItemsPanelTemplate").Single(element =>
            (string?)element.Attribute(x + "Key") == "SaveEditorActionCardsPanel");
        Assert.Equal("3", (string?)cardPanel.Element(p + "UniformGrid")?.Attribute("Columns"));

        var sources = document.Descendants(p + "ItemsControl")
            .Select(element => (string?)element.Attribute("ItemsSource"))
            .Where(value => value is not null && value.Contains("SaveEditor", StringComparison.Ordinal))
            .ToArray();
        Assert.Contains("{Binding SaveEditorCategories}", sources);
        Assert.DoesNotContain("{Binding SaveEditorOverviewActions}", sources);
        foreach (var name in new[] { "SaveEditorProfileActions", "SaveEditorTruckActions",
            "SaveEditorTrailerActions", "SaveEditorPlateActions", "SaveEditorBackupActions" })
            Assert.Contains("{Binding " + name + "}", sources);
    }

    [Fact]
    public void CategoriesExactlyPartitionExistingActions()
    {
        var vm = new SaveEditorTestHarness().ViewModel;
        var collections = new[]
        {
            (Section: "profile", Actions: vm.SaveEditorProfileActions, Count: 3),
            (Section: "truck", Actions: vm.SaveEditorTruckActions, Count: 4),
            (Section: "trailer", Actions: vm.SaveEditorTrailerActions, Count: 2),
            (Section: "plates", Actions: vm.SaveEditorPlateActions, Count: 1),
            (Section: "backup", Actions: vm.SaveEditorBackupActions, Count: 1)
        };
        Assert.Equal(11, collections.Sum(category => category.Actions.Count));
        Assert.Equal(5, vm.SaveEditorCategories.Count);
        var allAssignedIds = collections.SelectMany(c => c.Actions.Select(a => a.ActionId)).ToArray();
        Assert.Equal(allAssignedIds.Length, allAssignedIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(vm.SaveEditorOverviewActions.Select(a => a.ActionId).OrderBy(x => x),
            allAssignedIds.OrderBy(x => x));
        foreach (var c in collections)
        {
            Assert.Equal(c.Count, c.Actions.Count);
            Assert.All(c.Actions, action => Assert.Equal(c.Section, action.Section));
            var category = Assert.Single(vm.SaveEditorCategories.Where(category => category.Section == c.Section));
            Assert.Equal(c.Count, category.Count);
            vm.OpenSaveEditorSectionCommand.Execute(category.Section);
            Assert.Contains(vm.SaveEditorSectionIndex, new[] { 1, 2, 4, 5, 6 });
        }
        Assert.False(vm.PowertrainFeaturesEnabled);
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
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var cardTemplate = document.Descendants(p + "DataTemplate").Single(e =>
            (string?)e.Attribute(x + "Key") == "SaveEditorActionCardTemplate");
        var button = cardTemplate.Descendants(p + "Button").Single();
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

    [Fact]
    public void OriginalPreviewAtlasIsValidDecodablePng()
    {
        using var stream = typeof(SaveEditorOverviewTests).Assembly
            .GetManifestResourceStream("NmcScsLauncher.Tests.SaveEditorPreviews.png")!;
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = Assert.Single(decoder.Frames);
        Assert.Equal(1080, frame.PixelWidth);
        Assert.Equal(640, frame.PixelHeight);
    }

}
