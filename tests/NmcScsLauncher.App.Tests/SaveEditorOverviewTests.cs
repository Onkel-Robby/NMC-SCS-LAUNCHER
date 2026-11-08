using System.Xml.Linq;
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
}
