using Xunit;

namespace NmcScsLauncher.Core.Tests;

public sealed class LauncherFeatureFlagsTests
{
    [Fact]
    public void ReleasePolicyIsDisabledAndHasNoPublicMutationOrConstructor()
    {
        Assert.False(LauncherFeatureFlags.Current.PowertrainEditingEnabled);
        Assert.Empty(typeof(LauncherFeatureFlags).GetConstructors());
        Assert.Null(typeof(LauncherFeatureFlags).GetProperty(nameof(LauncherFeatureFlags.Current))!.SetMethod);
        Assert.Null(typeof(LauncherFeatureFlags).GetProperty(nameof(LauncherFeatureFlags.PowertrainEditingEnabled))!.SetMethod);
        Assert.Throws<ScsSaveEditException>(() => LauncherFeatureFlags.Current.RequirePowertrainEditing());
    }

    [Fact]
    public void ExplicitTestPolicyDoesNotChangeTheReleasePolicy()
    {
        var enabled = new LauncherFeatureFlags(true);
        enabled.RequirePowertrainEditing();
        Assert.True(enabled.PowertrainEditingEnabled);
        Assert.False(LauncherFeatureFlags.Current.PowertrainEditingEnabled);
    }
}
