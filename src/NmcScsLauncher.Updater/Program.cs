using System.Diagnostics;
using NmcScsLauncher.Infrastructure;

namespace NmcScsLauncher.Updater;

internal static class Program
{
    private static readonly TimeSpan ExitWaitTimeout = TimeSpan.FromMinutes(2);

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = UpdaterOptions.Parse(args);
            await LogAsync($"Updater started. Target={options.TargetDirectory}; WaitPid={options.WaitProcessId}");

            if (options.WaitProcessId == Environment.ProcessId)
                throw new InvalidOperationException("Updater cannot wait for its own process.");

            if (options.WaitProcessId is > 0)
            {
                var exited = await WaitForProcessExitAsync(options.WaitProcessId.Value, ExitWaitTimeout);
                if (!exited)
                {
                    await LogAsync("Launcher did not exit within the update timeout.");
                    return 3;
                }
            }

            var applier = new UpdatePackageApplier();
            UpdateApplyResult? result = null;
            try
            {
                result = await applier.ApplyAsync(
                    options.PackagePath,
                    options.TargetDirectory,
                    options.RestartExecutableRelativePath);

                await LogAsync($"Update files applied. Backup={result.BackupDirectory}");
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = result.RestartExecutablePath,
                    WorkingDirectory = result.TargetDirectory,
                    UseShellExecute = true
                });

                if (process is null)
                    throw new InvalidOperationException("Updated launcher process could not be started.");

                await LogAsync($"Updated launcher started. Pid={process.Id}; Backup retained at {result.BackupDirectory}");
                TryDeleteFile(options.PackagePath);
                return 0;
            }
            catch (Exception ex)
            {
                await LogAsync($"Update apply/restart failed: {ex.GetType().Name}: {ex.Message}");
                if (result is not null)
                {
                    try
                    {
                        await applier.RollbackAsync(result);
                        await LogAsync("Rollback completed successfully.");
                    }
                    catch (Exception rollbackException)
                    {
                        await LogAsync($"Rollback failed: {rollbackException.GetType().Name}: {rollbackException.Message}");
                    }
                }
                return 4;
            }
        }
        catch (ArgumentException ex)
        {
            await LogAsync($"Updater arguments are invalid: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            await LogAsync($"Updater failed: {ex.GetType().Name}: {ex.Message}");
            return 5;
        }
    }

    private static async Task<bool> WaitForProcessExitAsync(int processId, TimeSpan timeout)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return true;
        }

        using (process)
        using (var timeoutSource = new CancellationTokenSource(timeout))
        {
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }

    private static async Task LogAsync(string message)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NMC Network",
                "NMC SCS LAUNCHER",
                "Logs");
            Directory.CreateDirectory(root);
            var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
            await File.AppendAllTextAsync(Path.Combine(root, "updater.log"), line);
        }
        catch
        {
            // The updater must not fail only because logging is unavailable.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Download cleanup can be retried by a later launcher cleanup pass.
        }
    }

    private sealed record UpdaterOptions(
        string PackagePath,
        string TargetDirectory,
        string RestartExecutableRelativePath,
        int? WaitProcessId)
    {
        public static UpdaterOptions Parse(IReadOnlyList<string> args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < args.Count; index += 2)
            {
                if (index + 1 >= args.Count || !args[index].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("Updater arguments must be supplied as --name value pairs.");
                values[args[index]] = args[index + 1];
            }

            var package = Require(values, "--package");
            var target = Require(values, "--target");
            var restart = Require(values, "--restart");
            int? waitPid = null;
            if (values.TryGetValue("--wait-pid", out var pidValue))
            {
                if (!int.TryParse(pidValue, out var parsedPid) || parsedPid <= 0)
                    throw new ArgumentException("--wait-pid must be a positive process ID.");
                waitPid = parsedPid;
            }

            return new UpdaterOptions(package, target, restart, waitPid);
        }

        private static string Require(IReadOnlyDictionary<string, string> values, string name)
        {
            if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Required updater argument '{name}' is missing.");
            return value.Trim();
        }
    }
}
