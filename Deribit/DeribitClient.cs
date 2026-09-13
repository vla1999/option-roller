using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web;

using Newtonsoft.Json;

using OptionRollerUI.Models;

namespace OptionRollerUI.Deribit;

public class DeribitClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private string _accessToken = "";
    private DateTime _tokenExpiry = DateTime.MinValue;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    public DeribitClient(string clientId, string clientSecret, bool testnet = false)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _baseUrl = testnet
            ? "https://test.deribit.com/api/v2/"
            : "https://www.deribit.com/api/v2/";
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    private async Task EnsureAuthenticatedAsync()
    {
        if (_tokenExpiry > DateTime.UtcNow.AddSeconds(30))
            return;

        await _authLock.WaitAsync();
        try
        {
            if (_tokenExpiry > DateTime.UtcNow.AddSeconds(30))
                return;

            var url = _baseUrl + $"public/auth?client_id={Uri.EscapeDataString(_clientId)}" +
                      $"&client_secret={Uri.EscapeDataString(_clientSecret)}&grant_type=client_credentials";

            var httpResp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
            var response = await httpResp.Content.ReadAsStringAsync();

            if (!httpResp.IsSuccessStatusCode)
                throw new Exception($"Auth HTTP {(int)httpResp.StatusCode}: {response}");

            var parsed = JsonConvert.DeserializeObject<DeribitResponse<DeribitAuthResult>>(response)
                         ?? throw new Exception("Auth: null response");

            if (!parsed.IsSuccess)
                throw new Exception($"Auth failed: {parsed.Error}");

            _accessToken = parsed.Result!.AccessToken;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(parsed.Result.ExpiresIn);
        }
        finally
        {
            _authLock.Release();
        }
    }

    // ── Generic GET helpers ───────────────────────────────────────────────────

    private async Task<T> GetPublicAsync<T>(string method, Dictionary<string, string>? args = null)
    {
        var url = BuildUrl(_baseUrl + "public/" + method, args);
        var httpResp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
        var json = await httpResp.Content.ReadAsStringAsync();
        if (!httpResp.IsSuccessStatusCode)
            throw new Exception($"{method} HTTP {(int)httpResp.StatusCode}: {json}");
        var result = JsonConvert.DeserializeObject<DeribitResponse<T>>(json)
                     ?? throw new Exception($"{method}: null response");
        if (!result.IsSuccess)
            throw new Exception($"{method} error: {result.Error}");
        return result.Result!;
    }

    private async Task<T> GetPrivateAsync<T>(string method, Dictionary<string, string>? args = null)
    {
        await EnsureAuthenticatedAsync();
        var url = BuildUrl(_baseUrl + "private/" + method, args);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<DeribitResponse<T>>(json)
                     ?? throw new Exception($"{method}: null response");
        if (!result.IsSuccess)
            throw new Exception($"{method} error: {result.Error}");
        return result.Result!;
    }

    private async Task<T> PostPrivateAsync<T>(string method, Dictionary<string, string> args)
    {
        await EnsureAuthenticatedAsync();
        var url = _baseUrl + "private/" + method;
        var body = new FormUrlEncodedContent(args);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = body };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<DeribitResponse<T>>(json)
                     ?? throw new Exception($"{method}: null response");
        if (!result.IsSuccess)
            throw new Exception($"{method} error: {result.Error}");
        return result.Result!;
    }

    private static string BuildUrl(string baseUrl, Dictionary<string, string>? args)
    {
        if (args == null || args.Count == 0) return baseUrl;
        var qs = string.Join("&", args.Select(kv =>
            Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
        return baseUrl + "?" + qs;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Returns all options for the given currency + expiration, with Greeks.</summary>
    public async Task<List<OptionTicker>> GetOptionsAsync(string currency, DateTime expiration)
    {
        var summaries = await GetPublicAsync<List<DeribitBookSummary>>(
            "get_book_summary_by_currency",
            new Dictionary<string, string>
            {
                ["currency"] = currency,
                ["kind"] = "option"
            });

        var result = new List<OptionTicker>();
        var expirationDate = expiration.Date;

        foreach (var s in summaries)
        {
            if (s.Greeks == null) continue;

            var parsed = ParseInstrumentName(s.InstrumentName);
            if (parsed == null) continue;
            if (parsed.Value.Expiration.Date != expirationDate) continue;

            result.Add(new OptionTicker
            {
                InstrumentName = s.InstrumentName,
                OptionType = parsed.Value.OptionType,
                Strike = parsed.Value.Strike,
                Expiration = parsed.Value.Expiration,
                BestAsk = s.BestAskPrice ?? 0m,
                BestBid = s.BestBidPrice ?? 0m,
                Delta = s.Greeks.Delta
            });
        }

        return result;
    }

    // ── Private API ────────────────────────────────────────────────────────────

    /// <summary>Returns current option positions for the given currency.</summary>
    public async Task<List<Position>> GetPositionsAsync(string currency)
    {
        var raw = await GetPrivateAsync<List<DeribitPosition>>(
            "get_positions",
            new Dictionary<string, string>
            {
                ["currency"] = currency,
                ["kind"] = "option"
            });

        var result = new List<Position>();
        foreach (var p in raw)
        {
            if (p.Size == 0) continue;
            var parsed = ParseInstrumentName(p.InstrumentName);
            if (parsed == null) continue;

            result.Add(new Position
            {
                InstrumentName = p.InstrumentName,
                Size = p.Size,
                Delta = p.Delta,
                OptionType = parsed.Value.OptionType,
                Strike = parsed.Value.Strike
            });
        }
        return result;
    }

    /// <summary>Places a market order. Direction: Buy or Sell.</summary>
    public async Task<DeribitOrderResult> PlaceOrderAsync(
        string instrumentName,
        Direction direction,
        decimal amount,
        PositionOrderType positionType)
    {
        var method = direction == Direction.Buy ? "buy" : "sell";
        var args = new Dictionary<string, string>
        {
            ["instrument_name"] = instrumentName,
            ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
            ["type"] = "market",
        };

        if (positionType == PositionOrderType.Close)
            args["reduce_only"] = "true";

        return await PostPrivateAsync<DeribitOrderResult>(method, args);
    }

    // ── Instrument name parser ─────────────────────────────────────────────────

    // Format: BTC-30MAY25-100000-C  or  BTC-30MAY25-100000-P
    private static (OptionType OptionType, decimal Strike, DateTime Expiration)?
        ParseInstrumentName(string name)
    {
        var parts = name.Split('-');
        if (parts.Length < 4) return null;

        var optType = parts[^1].ToUpperInvariant() == "C" ? OptionType.Call : OptionType.Put;

        if (!decimal.TryParse(parts[^2], NumberStyles.Any, CultureInfo.InvariantCulture, out var strike))
            return null;

        if (!TryParseDeribitExpiry(parts[^3], out var expiry))
            return null;

        return (optType, strike, expiry);
    }

    // Parses "30MAY25" or "30MAY2025" → DateTime
    private static bool TryParseDeribitExpiry(string s, out DateTime result)
    {
        result = DateTime.MinValue;
        // Try short year first: 30MAY25
        if (DateTime.TryParseExact(s, "dMMMyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return true;
        // Then long year: 30MAY2025
        if (DateTime.TryParseExact(s, "dMMMyyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return true;
        return false;
    }

    public void Dispose()
    {
        _http.Dispose();
        _authLock.Dispose();
    }
}
