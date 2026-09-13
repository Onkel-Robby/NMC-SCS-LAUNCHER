using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class FileAppLogger : IAppLogger, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task WriteAsync(
        string level,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        var logFile = Path.Combine(AppPaths.LogsDirectory, $"nmc-scs-launcher-{DateTime.UtcNow:yyyy-MM-dd}.log");
        var line = $"{DateTimeOffset.Now:O} [{level}] {message}";

        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(logFile, line + Environment.NewLine, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
