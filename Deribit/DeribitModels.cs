using Newtonsoft.Json;

namespace OptionRollerUI.Deribit;

// Generic JSON-RPC 2.0 wrapper
public class DeribitResponse<T>
{
    [JsonProperty("result")]
    public T? Result { get; set; }

    [JsonProperty("error")]
    public DeribitError? Error { get; set; }

    public bool IsSuccess => Error == null;
}

public class DeribitError
{
    [JsonProperty("code")]
    public int Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; } = "";

    public override string ToString() => $"[{Code}] {Message}";
}

// Auth
public class DeribitAuthResult
{
    [JsonProperty("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonProperty("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonProperty("refresh_token")]
    public string RefreshToken { get; set; } = "";
}

// Position
public class DeribitPosition
{
    [JsonProperty("instrument_name")]
    public string InstrumentName { get; set; } = "";

    [JsonProperty("size")]
    public decimal Size { get; set; }

    [JsonProperty("delta")]
    public decimal Delta { get; set; }

    [JsonProperty("direction")]
    public string Direction { get; set; } = "";

    [JsonProperty("kind")]
    public string Kind { get; set; } = "";
}

// Book summary (options list with greeks)
public class DeribitBookSummary
{
    [JsonProperty("instrument_name")]
    public string InstrumentName { get; set; } = "";

    [JsonProperty("best_ask_price")]
    public decimal? BestAskPrice { get; set; }

    [JsonProperty("best_bid_price")]
    public decimal? BestBidPrice { get; set; }

    [JsonProperty("greeks")]
    public DeribitGreeks? Greeks { get; set; }

    [JsonProperty("underlying_price")]
    public decimal? UnderlyingPrice { get; set; }
}

public class DeribitGreeks
{
    [JsonProperty("delta")]
    public decimal Delta { get; set; }

    [JsonProperty("gamma")]
    public decimal Gamma { get; set; }

    [JsonProperty("vega")]
    public decimal Vega { get; set; }

    [JsonProperty("theta")]
    public decimal Theta { get; set; }
}

// Order result
public class DeribitOrderResult
{
    [JsonProperty("order")]
    public DeribitOrder? Order { get; set; }
}

public class DeribitOrder
{
    [JsonProperty("order_id")]
    public string OrderId { get; set; } = "";

    [JsonProperty("instrument_name")]
    public string InstrumentName { get; set; } = "";

    [JsonProperty("direction")]
    public string Direction { get; set; } = "";

    [JsonProperty("amount")]
    public decimal Amount { get; set; }

    [JsonProperty("order_state")]
    public string OrderState { get; set; } = "";

    [JsonProperty("order_type")]
    public string OrderType { get; set; } = "";
}
