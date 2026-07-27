using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using OutageNotifier.Configuration;
using OutageNotifier.Models;

namespace OutageNotifier.Services;

public sealed class OutageApiClient : IOutageApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;

    public OutageApiClient(HttpClient httpClient, IOptions<OutageApiOptions> options)
    {
        _httpClient = httpClient;
        _endpoint = options.Value.Endpoint;
    }

    public async Task<IReadOnlyList<Outage>> GetOutagesAsync(CancellationToken cancellationToken)
    {
        var outages = await _httpClient.GetFromJsonAsync<List<Outage>>(_endpoint, cancellationToken);
        return outages ?? new List<Outage>();
    }
}
