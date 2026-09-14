namespace NmcScsLauncher.App.Services;

public static class LicenseEnforcementPolicy
{
#if DEBUG
    public const bool RequiredByBuild = false;
#else
    public const bool RequiredByBuild = true;
#endif
}
