using System.Globalization;
using System.Text.Json;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class SteamWorkshopMetadataProvider : IWorkshopMetadataProvider
{
    private const int BatchSize = 50;
    private static readonly Uri DetailsEndpoint =
        new("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/");

    private readonly HttpClient _httpClient;

    public SteamWorkshopMetadataProvider(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IReadOnlyDictionary<ulong, WorkshopPublishedFileDetails>> GetDetailsAsync(
        GameType game,
        IEnumerable<ulong> publishedFileIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publishedFileIds);

        var ids = publishedFileIds
            .Where(static id => id != 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<ulong, WorkshopPublishedFileDetails>();
        }

        var expectedAppId = GameDefinition.For(game).SteamAppId;
        var result = new Dictionary<ulong, WorkshopPublishedFileDetails>();

        for (var offset = 0; offset < ids.Length; offset += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = ids.Skip(offset).Take(BatchSize).ToArray();
            var formValues = new List<KeyValuePair<string, string>>(batch.Length + 1)
            {
                new("itemcount", batch.Length.ToString(CultureInfo.InvariantCulture))
            };

            for (var index = 0; index < batch.Length; index++)
            {
                formValues.Add(new(
                    $"publishedfileids[{index}]",
                    batch[index].ToString(CultureInfo.InvariantCulture)));
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, DetailsEndpoint)
            {
                Content = new FormUrlEncodedContent(formValues)
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);

            if (!document.RootElement.TryGetProperty("response", out var responseElement)
                || !responseElement.TryGetProperty("publishedfiledetails", out var detailsElement)
                || detailsElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var detail in detailsElement.EnumerateArray())
            {
                if (!TryGetInt32(detail, "result", out var detailResult) || detailResult != 1)
                {
                    continue;
                }

                if (!TryGetUInt64(detail, "publishedfileid", out var publishedFileId))
                {
                    continue;
                }

                if (TryGetInt32(detail, "consumer_app_id", out var consumerAppId)
                    || TryGetInt32(detail, "consumer_appid", out consumerAppId))
                {
                    if (consumerAppId != expectedAppId)
                    {
                        continue;
                    }
                }

                if (!detail.TryGetProperty("title", out var titleElement)
                    || titleElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var title = titleElement.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                DateTimeOffset? updatedAt = null;
                if (TryGetInt64(detail, "time_updated", out var unixSeconds)
                    || TryGetInt64(detail, "timeupdated", out unixSeconds))
                {
                    try
                    {
                        updatedAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        updatedAt = null;
                    }
                }

                result[publishedFileId] = new WorkshopPublishedFileDetails(
                    publishedFileId,
                    title,
                    updatedAt);
            }
        }

        return result;
    }

    private static bool TryGetUInt64(JsonElement element, string propertyName, out ulong value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => ulong.TryParse(
                property.GetString(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value),
            JsonValueKind.Number => property.TryGetUInt64(out value),
            _ => false
        };
    }

    private static bool TryGetInt64(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => long.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value),
            JsonValueKind.Number => property.TryGetInt64(out value),
            _ => false
        };
    }

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => int.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value),
            JsonValueKind.Number => property.TryGetInt32(out value),
            _ => false
        };
    }
}
