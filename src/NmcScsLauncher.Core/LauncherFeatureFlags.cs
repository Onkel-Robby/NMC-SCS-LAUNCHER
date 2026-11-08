using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("NmcScsLauncher.Core.Tests")]
[assembly: InternalsVisibleTo("NmcScsLauncher.Infrastructure.Tests")]
[assembly: InternalsVisibleTo("NmcScsLauncher.App.Tests")]

namespace NmcScsLauncher.Core;

/// <summary>Immutable release policy. Deliberate code changes, never settings or environment variables, enable a feature.</summary>
public sealed class LauncherFeatureFlags
{
    public static LauncherFeatureFlags Current { get; } = new(powertrainEditingEnabled: false);

    internal LauncherFeatureFlags(bool powertrainEditingEnabled) => PowertrainEditingEnabled = powertrainEditingEnabled;

    public bool PowertrainEditingEnabled { get; }

    public void RequirePowertrainEditing()
    {
        if (!PowertrainEditingEnabled)
            throw new ScsSaveEditException("Die Motor-/Getriebefunktion ist vorübergehend deaktiviert.");
    }
}
