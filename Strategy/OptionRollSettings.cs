using OptionRollerUI.Models;

namespace OptionRollerUI.Strategy;

public class OptionRollSettings
{
    /// <summary>Deribit client ID</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Deribit client secret</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>Currency: BTC, ETH, SOL, etc.</summary>
    public string Currency { get; set; } = "BTC";

    /// <summary>Target expiration date for options</summary>
    public DateTime Expiration { get; set; }

    /// <summary>Strike of the call leg (0 = any)</summary>
    public decimal CallStrike { get; set; } = 0;

    /// <summary>Strike of the put leg (0 = any)</summary>
    public decimal PutStrike { get; set; } = 0;

    /// <summary>Delta imbalance threshold that triggers a roll</summary>
    public decimal Diff { get; set; } = 0.05m;

    /// <summary>Roll: close old + open new. Close: only close old.</summary>
    public OptionStrategyType StrategyType { get; set; } = OptionStrategyType.Roll;

    /// <summary>Use Deribit testnet instead of mainnet</summary>
    public bool UseTestnet { get; set; } = false;
}
