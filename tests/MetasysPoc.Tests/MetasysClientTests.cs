using System.Net;
using System.Text;
using System.Text.Json;
using BMS.Ingestion.DataAccess.Services;
using BMS.Ingestion.Domain.Models;

namespace MetasysPoc.Tests;

public sealed class MetasysClientTests
{
    [Fact]
    public async Task Catalog_reads_all_three_resources_in_order()
    {
        var paths = new List<string>();
        var client = Create(request => {
            paths.Add(request.RequestUri!.AbsolutePath);
            return Json(request.RequestUri.AbsolutePath.EndsWith("objects") ? "[{\"objectId\":\"P-1\"}]" : "[]");
        });
        var result = await client.ReadCatalogAsync(default);
        Assert.Equal(new[] { "/api/metasys/buildings", "/api/metasys/equipment", "/api/metasys/objects" }, paths);
        Assert.Equal("P-1", Assert.Single(result.Points).ObjectId);
    }

    [Fact]
    public async Task Subscribe_posts_object_ids_and_returns_subscription()
    {
        string? body = null;
        using var handler = new Handler(async (request, ct) => {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/metasys/subscriptions", request.RequestUri!.AbsolutePath);
            body = await request.Content!.ReadAsStringAsync(ct);
            return Json("{\"subscriptionId\":\"sub-1\"}");
        });
        var result = await new MetasysClient(new Factory(handler)).SubscribeAsync(["P-1", "P-2"], default);
        Assert.Equal("sub-1", result.SubscriptionId);
        using var parsed = JsonDocument.Parse(body!);
        Assert.Equal(new[] { "P-1", "P-2" }, parsed.RootElement.GetProperty("objectIds")
            .EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public async Task Sse_ignores_heartbeat_and_preserves_event_order_and_precision()
    {
        var client = Create(request => {
            Assert.Equal("/api/metasys/subscriptions/sub-1/stream", request.RequestUri!.AbsolutePath);
            return new(HttpStatusCode.OK) { Content = new StringContent(
                ": heartbeat\nevent: cov\ndata: {\"objectId\":\"P-1\",\"currentValue\":12.3456}\n\n" +
                "retry: 1000\ndata: {\"objectId\":\"P-2\",\"currentValue\":0}\n\n", Encoding.UTF8, "text/event-stream") };
        });
        var values = new List<CovEvent>();
        await foreach (var value in client.ReadEventsAsync("sub-1", default)) values.Add(value);
        Assert.Equal(new[] { "P-1", "P-2" }, values.Select(v => v.ObjectId));
        Assert.Equal(12.3456m, values[0].CurrentValue);
    }

    [Fact]
    public async Task Stopping_enumeration_disposes_the_response_stream()
    {
        var stream = new TrackedStream(Encoding.UTF8.GetBytes("data: {\"objectId\":\"P-1\"}\n"));
        var client = Create(_ => new(HttpStatusCode.OK) { Content = new StreamContent(stream) });
        await foreach (var value in client.ReadEventsAsync("sub-1", default))
        {
            Assert.Equal("P-1", value.ObjectId);
            break;
        }
        Assert.True(stream.WasDisposed);
    }

    [Fact]
    public async Task Cancellation_stops_sse_and_releases_the_stream()
    {
        using var cancellation = new CancellationTokenSource();
        var stream = new TrackedStream(Encoding.UTF8.GetBytes(
            "data: {\"objectId\":\"P-1\"}\ndata: {\"objectId\":\"P-2\"}\n"));
        var client = Create(_ => new(HttpStatusCode.OK) { Content = new StreamContent(stream) });
        var seen = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => {
            await foreach (var _ in client.ReadEventsAsync("sub-1", cancellation.Token))
            {
                seen++;
                cancellation.Cancel();
            }
        });
        Assert.Equal(1, seen);
        Assert.True(stream.WasDisposed);
    }

    [Fact]
    public async Task Http_failure_is_visible_to_the_worker()
    {
        var client = Create(_ => new(HttpStatusCode.Forbidden));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.ReadCatalogAsync(default));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
    }

    [Fact]
    public async Task Null_catalog_is_rejected_before_subscription()
    {
        var client = Create(_ => Json("null"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ReadCatalogAsync(default));
    }

    [Fact]
    public async Task Malformed_sse_data_is_not_silently_discarded()
    {
        var client = Create(_ => new(HttpStatusCode.OK) { Content = new StringContent("data: invalid\n") });
        await Assert.ThrowsAsync<JsonException>(async () => {
            await foreach (var _ in client.ReadEventsAsync("sub-1", default)) { }
        });
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    private static MetasysClient Create(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new Factory(new Handler((request, _) => Task.FromResult(respond(request)))));
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(MetasysClient.ClientName, name);
            return new(handler, disposeHandler: false) { BaseAddress = new("http://localhost/") };
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct);
    }
    private sealed class TrackedStream(byte[] content) : MemoryStream(content)
    {
        public bool WasDisposed;
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
