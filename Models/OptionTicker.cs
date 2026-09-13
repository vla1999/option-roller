namespace OptionRollerUI.Models;

public class OptionTicker
{
    public string InstrumentName { get; set; } = "";
    public OptionType OptionType { get; set; }
    public decimal Delta { get; set; }
    public decimal BestAsk { get; set; }
    public decimal BestBid { get; set; }
    public DateTime Expiration { get; set; }
    public decimal Strike { get; set; }
}
