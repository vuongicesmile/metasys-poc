using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BMS.Ingestion.Business.Abstractions;
using BMS.Ingestion.Common.Configuration;
using BMS.Ingestion.Domain.Models;

namespace BMS.Ingestion.DataAccess.Services;

/// <summary>Infrastructure adapter that owns HTTP, JSON and SSE transport details.</summary>
public sealed class MetasysClient(IHttpClientFactory clients) : IMetasysClient
{
    public const string ClientName = "Metasys";

    public async Task<MetasysCatalog> ReadCatalogAsync(CancellationToken ct)
    {
        using var client = clients.CreateClient(ClientName);
        var buildings = await client.GetFromJsonAsync<BmsBuilding[]>(
                "api/metasys/buildings", AppSettings.JsonOptions, ct)
            ?? throw new InvalidOperationException("Fake Metasys returned an empty building catalog response.");
        var equipment = await client.GetFromJsonAsync<BmsEquipment[]>(
                "api/metasys/equipment", AppSettings.JsonOptions, ct)
            ?? throw new InvalidOperationException("Fake Metasys returned an empty equipment catalog response.");
        var points = await client.GetFromJsonAsync<MetasysPoint[]>(
                "api/metasys/objects", AppSettings.JsonOptions, ct)
            ?? throw new InvalidOperationException("Fake Metasys returned an empty point catalog response.");
        return new(buildings, equipment, points);
    }

    public async Task<SubscriptionResponse> SubscribeAsync(
        IEnumerable<string> objectIds,
        CancellationToken ct)
    {
        using var client = clients.CreateClient(ClientName);
        using var response = await client.PostAsJsonAsync(
            "api/metasys/subscriptions",
            new SubscriptionRequest { ObjectIds = [.. objectIds] },
            ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SubscriptionResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Fake Metasys returned an empty subscription response.");
    }

    public async IAsyncEnumerable<CovEvent> ReadEventsAsync(
        string subscriptionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var client = clients.CreateClient(ClientName);
        using var response = await client.GetAsync(
            $"api/metasys/subscriptions/{Uri.EscapeDataString(subscriptionId)}/stream",
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            // A line can already be buffered when cancellation is requested.
            ct.ThrowIfCancellationRequested();
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var value = JsonSerializer.Deserialize<CovEvent>(line[5..].TrimStart(), AppSettings.JsonOptions);
            if (value is not null) yield return value;
        }
    }
}
