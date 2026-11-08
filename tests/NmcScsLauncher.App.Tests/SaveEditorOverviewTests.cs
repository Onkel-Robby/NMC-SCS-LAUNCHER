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
