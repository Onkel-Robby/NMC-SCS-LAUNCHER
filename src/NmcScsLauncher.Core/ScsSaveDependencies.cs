namespace NmcScsLauncher.Core;

/// <summary>The literal kind in info.sii. None of these values proves current activation or entitlement.</summary>
public enum ScsSaveDependencyKind { Mod, Dlc, Rdlc }

/// <summary>A historical reference from the selected save's dependencies array, retaining its original order.</summary>
public sealed record ScsSaveDependency(ScsSaveDependencyKind Kind, string Identity, string DisplayName, int Index);
