using System;

using MarketMakerBot.Strategies.Settings.SettingsBase;
using MarketMakerBot.Strategies.Settings.StockSettingsBase;

namespace MarketMakerBot.Strategies.Settings
{
    public class OptionRollSettings : BaseSettings<IStockSettings>
    {
        /// <summary>
        /// Main account
        /// </summary>
        public override IStockSettings MasterStock { get; set; }
        /// <summary>
        /// Hedge account
        /// </summary>
        public override IStockSettings SlaveStock { get; set; }
        public DateTime Expiration { get; set; }
        public OptionStrategyType OptionStrategyType { get; set; }
        public decimal Diff { get; set; }
    }

    public enum OptionStrategyType
    {
        Roll,
        Close
    }
}
