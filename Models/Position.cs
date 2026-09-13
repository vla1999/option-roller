namespace OptionRollerUI.Models;

public class Position
{
    public string InstrumentName { get; set; } = "";
    public decimal Size { get; set; }
    public decimal Delta { get; set; }
    public OptionType OptionType { get; set; }
    public decimal Strike { get; set; }
}
