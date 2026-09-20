using System.Net;
using System.Text;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class SteamWorkshopMetadataProviderTests
{
    [Fact]
    public async Task GetDetailsReturnsTitleAndUpdatedTimestamp()
    {
        var handler = new CapturingHandler("""
            {
              "response": {
                "result": 1,
                "resultcount": 2,
                "publishedfiledetails": [
                  {
                    "publishedfileid": "3733383303",
                    "result": 1,
                    "consumer_app_id": 227300,
                    "title": "Test Workshop Mod",
                    "time_updated": 1760000000
                  },
                  {
                    "publishedfileid": "645951060",
                    "result": 9
                  }
                ]
              }
            }
            """);
        var provider = new SteamWorkshopMetadataProvider(new HttpClient(handler));

        var details = await provider.GetDetailsAsync(
            GameType.Ets2,
            new[] { 3733383303UL, 645951060UL });

        var item = Assert.Single(details);
        Assert.Equal(3733383303UL, item.Key);
        Assert.Equal("Test Workshop Mod", item.Value.Title);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1760000000), item.Value.UpdatedAtUtc);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(
            "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
            handler.LastRequest.RequestUri!.AbsoluteUri);
        Assert.Contains("itemcount=2", handler.LastBody);
        Assert.Contains("publishedfileids%5B0%5D=3733383303", handler.LastBody);
        Assert.Contains("publishedfileids%5B1%5D=645951060", handler.LastBody);
    }

    [Fact]
    public async Task GetDetailsIgnoresItemsForAnotherGame()
    {
        var handler = new CapturingHandler("""
            {
              "response": {
                "publishedfiledetails": [
                  {
                    "publishedfileid": "1234",
                    "result": 1,
                    "consumer_appid": 270880,
                    "title": "ATS Item"
                  }
                ]
              }
            }
            """);
        var provider = new SteamWorkshopMetadataProvider(new HttpClient(handler));

        var details = await provider.GetDetailsAsync(GameType.Ets2, new[] { 1234UL });

        Assert.Empty(details);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _json;

        public CapturingHandler(string json)
        {
            _json = json;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            };
        }
    }
}
