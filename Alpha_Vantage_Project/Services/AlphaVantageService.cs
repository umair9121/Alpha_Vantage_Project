using System.Globalization;
using System.Net.Http;
using System.Linq;
using System.Text.Json;
using Alpha_Vantage_Project.Models;
using Microsoft.Extensions.Options;

namespace Alpha_Vantage_Project.Services;

public sealed class AlphaVantageService : IDisposable
{
    private const string Endpoint = "https://www.alphavantage.co/query";
    private readonly HttpClient _httpClient;
    private readonly AlphaVantageOptions _options;

    public AlphaVantageService(IOptions<AlphaVantageOptions> options)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Alpha Vantage API key is not configured.");
        }

        _httpClient = new HttpClient();
    }

    public async Task<IntradaySnapshot> GetIntradaySnapshotAsync(
        string symbol,
        string interval = "5min",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        }

        var query = $"function=TIME_SERIES_INTRADAY&symbol={symbol.ToUpperInvariant()}&interval={interval}&outputsize=compact&apikey={_options.ApiKey}";
        var response = await _httpClient.GetAsync($"{Endpoint}?{query}", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.TryGetProperty("Error Message", out var errorElement))
        {
            throw new InvalidOperationException(errorElement.GetString() ?? "Alpha Vantage returned an error.");
        }

        if (document.RootElement.TryGetProperty("Note", out var noteElement))
        {
            throw new InvalidOperationException(noteElement.GetString() ?? "Alpha Vantage rate limit reached. Please try again in a minute.");
        }

        if (!document.RootElement.TryGetProperty("Meta Data", out var metaElement))
        {
            throw new InvalidOperationException("Response did not include metadata.");
        }

        var metaData = new IntradayMetaData(
            Information: metaElement.GetProperty("1. Information").GetString() ?? string.Empty,
            Symbol: metaElement.GetProperty("2. Symbol").GetString() ?? string.Empty,
            LastRefreshed: metaElement.GetProperty("3. Last Refreshed").GetString() ?? string.Empty,
            Interval: metaElement.GetProperty("4. Interval").GetString() ?? interval,
            TimeZone: metaElement.GetProperty("6. Time Zone").GetString() ?? "Unknown");

        var seriesElement = GetSeriesElement(document.RootElement);
        var points = ParsePoints(seriesElement);

        return new IntradaySnapshot(metaData, points);
    }

    private static JsonElement GetSeriesElement(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Contains("Time Series", StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new InvalidOperationException("Response did not include a time series payload.");
    }

    private static IReadOnlyList<IntradayPoint> ParsePoints(JsonElement seriesElement)
    {
        var results = new List<IntradayPoint>();

        foreach (var entry in seriesElement.EnumerateObject())
        {
            if (!DateTime.TryParse(entry.Name, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
            {
                continue;
            }

            var payload = entry.Value;
            results.Add(new IntradayPoint(
                Timestamp: timestamp,
                Open: ParseDecimal(payload, "1. open"),
                High: ParseDecimal(payload, "2. high"),
                Low: ParseDecimal(payload, "3. low"),
                Close: ParseDecimal(payload, "4. close"),
                Volume: ParseLong(payload, "5. volume")));
        }

        return results
            .OrderByDescending(point => point.Timestamp)
            .ToList();
    }

    private static decimal ParseDecimal(JsonElement element, string propertyName)
        => decimal.TryParse(element.GetProperty(propertyName).GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;

    private static long ParseLong(JsonElement element, string propertyName)
        => long.TryParse(element.GetProperty(propertyName).GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0L;

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

