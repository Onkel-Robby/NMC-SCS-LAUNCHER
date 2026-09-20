using System.Diagnostics;

namespace NmcScsLauncher.App.Services;

public interface IExternalUriService
{
    void Open(Uri uri);
}

public sealed class ExternalUriService : IExternalUriService
{
    public void Open(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Nur absolute HTTP/HTTPS-Adressen dürfen geöffnet werden.", nameof(uri));
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
    }
}
