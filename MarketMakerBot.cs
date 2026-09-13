using MarketMakerBot.DataFeed;
using MarketMakerBot.Models;
using MarketMakerBot.PublicDataFeed;
using MarketMakerBot.Strategies;
using MarketMakerBot.Strategies.PerpRiskManagement;
using MarketMakerBot.Strategies.PerpSpotMakerTakerArbitrage;
using MarketMakerBot.Strategies.PingPong;
using MarketMakerBot.Strategies.Settings;
using MarketMakerBot.Strategies.Settings.SettingsBase;
using MarketMakerBot.Strategies.Settings.StockSettingsBase;
using MarketMakerBot.Strategies.Twins;

using Microsoft.Extensions.Logging;

using SharedResources.Models.Shared.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using TelegramBotLibrary;

using static MarketMakerBot.PublicDataFeed.PublicDataFeedConnectionsPool;

namespace MarketMakerBot
{
    public class MarketMakerBot
    {
        public static string BotVersion {  get; set; } = string.Empty;
        private List<IStrategy> strategies = new List<IStrategy>();
        private ILogger _logger;
        private bool _isStarted = false;

        /// <summary>
        /// Invoke when need swap accounts
        /// </summary>
        public Action<int> OnSettingsNeedChange;
        public event AsyncEventHandlerArgs<StrategyLogicEventArgs> StrategyEvent;
        public event AsyncEventHandlerArgs<StrategyLogicFinishEventArgs> StrategyFinishEvent;

        PublicDataFeedConnectionsPool _publicDataFeed;
        public PublicDataFeedConnectionsPool PublicDataFeed
        {
            get
            {
                if (_publicDataFeed == null)
                    _publicDataFeed = new PublicDataFeedConnectionsPool(_logger);
                return _publicDataFeed;
            }
            private set { }
        }

        private PrivateDataFeedConnectionsPool _privateDataFeed;
        public PrivateDataFeedConnectionsPool PrivateDataFeed
        {
            get
            {
                if (_privateDataFeed == null)
                    _privateDataFeed = new PrivateDataFeedConnectionsPool(_logger);
                return _privateDataFeed;
            }
            private set { }
        }

        private ConnectorsPool _connectorsPool;

        private readonly IDbService _dbService;
        private readonly ITelegramBot _watchdogBot;

        public MarketMakerBot(ILogger logger, ConnectorsPool connectorsPool, IDbService dbService, ITelegramBot watchdogBot)
        {
            _logger = logger;
            ConnectorLegacyCompatibilityExtensions.ConfigureLogger(logger);
            _connectorsPool = connectorsPool;
            _publicDataFeed = new PublicDataFeedConnectionsPool(_logger);
            _dbService = dbService;
            _watchdogBot = watchdogBot;
        }

        /// <summary>
        /// Start bot
        /// </summary>
        /// <returns></returns>
        public async Task Start(List<ISettings<IStockSettings>> settings, Dictionary<string, object> state, List<SharedOrder> activeOrders, SharedReloadSettings reloadSettings)
        {
            //SpreadArbitrage
            foreach (var setting in settings)
            {
                if (setting is IcebergSettings icebergSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var iceberg = new IcebergStrategy(PublicDataFeed, PrivateDataFeed, _logger, icebergSettings, _connectorsPool, activeOrders, _dbService, _watchdogBot);
                        iceberg.StrategyEvent += OnStrategyEvent;
                        iceberg.StrategyFinishEvent += OnStrategyFinish;
                        await iceberg.LoadState(state);
                        await iceberg.StartStrategy(reloadSettings);
                        strategies.Add(iceberg);
                    }
                }
                else if (setting is AdaptiveSettings adaptiveSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var adaptive = new AdaptiveStrategy(PublicDataFeed, PrivateDataFeed, _logger, adaptiveSettings, _connectorsPool, activeOrders, _dbService, _watchdogBot);
                        adaptive.StrategyEvent += OnStrategyEvent;
                        adaptive.StrategyFinishEvent += OnStrategyFinish;
                        await adaptive.LoadState(state);
                        await adaptive.StartStrategy(reloadSettings);
                        strategies.Add(adaptive);
                    }
                }
                else if (setting is AdaptiveV2Settings adaptiveV2Settings)
                {
                    if (setting.IsEnabled)
                    {
                        var adaptive = new AdaptiveV2Strategy(PublicDataFeed, PrivateDataFeed, _logger, adaptiveV2Settings, _connectorsPool, activeOrders, _dbService, _watchdogBot);
                        adaptive.StrategyEvent += OnStrategyEvent;
                        adaptive.StrategyFinishEvent += OnStrategyFinish;
                        await adaptive.LoadState(state);
                        await adaptive.StartStrategy(reloadSettings);
                        strategies.Add(adaptive);
                    }
                }
                else if (setting is PingPongV2Settings pingPongv2Settings)
                {
                    if (setting.IsEnabled)
                    {
                        var pingPong = new PingPongV2Strategy(PublicDataFeed, PrivateDataFeed, _logger, pingPongv2Settings, _connectorsPool, activeOrders, _dbService, _watchdogBot);
                        pingPong.StrategyEvent += OnStrategyEvent;
                        pingPong.StrategyFinishEvent += OnStrategyFinish;
                        await pingPong.LoadState(state);
                        await pingPong.StartStrategy(reloadSettings);
                        strategies.Add(pingPong);
                    }
                }
                else if (setting is PingPongSettings pingPongSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var pingPong = new PingPongStrategy(PublicDataFeed, PrivateDataFeed, _logger, pingPongSettings, _connectorsPool, _dbService, _watchdogBot);
                        pingPong.StrategyEvent += OnStrategyEvent;
                        pingPong.StrategyFinishEvent += OnStrategyFinish;
                        await pingPong.StartStrategy(reloadSettings);
                        strategies.Add(pingPong);
                    }
                }
                else if (setting is SpreadArbitrageSetings spreadArbitrageSetings)
                {
                    if (setting.IsEnabled)
                    {
                        var spreadArbitrage = new SpreadArbitrage(PublicDataFeed, PrivateDataFeed, _logger, spreadArbitrageSetings, _connectorsPool, _dbService, _watchdogBot);
                        spreadArbitrage.StrategyEvent += OnStrategyEvent;
                        spreadArbitrage.StrategyFinishEvent += OnStrategyFinish;
                        await spreadArbitrage.StartStrategy(reloadSettings);
                        strategies.Add(spreadArbitrage);
                    }
                }
                else if (setting is SpreadArbitrageStepsSettigs spreadArbitrageSetingsStep)
                {
                    if (setting.IsEnabled)
                    {
                        var spreadArbitrageStep = new SpreadArbitrageSteps(PublicDataFeed, PrivateDataFeed, _logger, spreadArbitrageSetingsStep, _connectorsPool, _dbService, _watchdogBot);
                        spreadArbitrageStep.StrategyEvent += OnStrategyEvent;
                        spreadArbitrageStep.StrategyFinishEvent += OnStrategyFinish;
                        await spreadArbitrageStep.StartStrategy(reloadSettings);
                        strategies.Add(spreadArbitrageStep);
                    }
                }
                else if (setting is SpreadArbitrageSetingsTriple spreadArbitrageSetingsTriple)
                {
                    if (setting.IsEnabled)
                    {
                        var spreadArbitrageTriple = new SpreadArbitrageTriple(PublicDataFeed, PrivateDataFeed, _logger, spreadArbitrageSetingsTriple, _connectorsPool, _dbService, _watchdogBot);
                        spreadArbitrageTriple.StrategyEvent += OnStrategyEvent;
                        spreadArbitrageTriple.StrategyFinishEvent += OnStrategyFinish;
                        await spreadArbitrageTriple.StartStrategy(reloadSettings);
                        strategies.Add(spreadArbitrageTriple);
                    }
                }

                else if (setting is SpreadArbitrageSetingsTripleNew spreadArbitrageSetingsTriplenew)
                {
                    if (setting.IsEnabled)
                    {
                        var spreadArbitrageTriple = new SpreadArbitrageTripleNew(PublicDataFeed, PrivateDataFeed, _logger, spreadArbitrageSetingsTriplenew, _connectorsPool, _dbService, _watchdogBot);
                        spreadArbitrageTriple.StrategyEvent += OnStrategyEvent;
                        spreadArbitrageTriple.StrategyFinishEvent += OnStrategyFinish;
                        await spreadArbitrageTriple.StartStrategy(reloadSettings);
                        strategies.Add(spreadArbitrageTriple);
                    }
                }
                else if (setting is SpreadArbitrageSetingsTripleStep spreadArbitrageSetingsTripleStep)
                {
                    if (setting.IsEnabled)
                    {
                        var spreadArbitrageTripleStep = new SpreadArbitrageTripleSteps(PublicDataFeed, PrivateDataFeed, _logger, spreadArbitrageSetingsTripleStep, _connectorsPool, _dbService, _watchdogBot);
                        spreadArbitrageTripleStep.StrategyEvent += OnStrategyEvent;
                        spreadArbitrageTripleStep.StrategyFinishEvent += OnStrategyFinish;
                        await spreadArbitrageTripleStep.StartStrategy(reloadSettings);
                        strategies.Add(spreadArbitrageTripleStep);
                    }
                }

                else if (setting is SpreadRetentionSettigs spreadRetentionSetings)
                {
                    if (setting.IsEnabled)
                    {
                        var spreadRetention = new SpreadRetention(PublicDataFeed, PrivateDataFeed, _logger, spreadRetentionSetings, _connectorsPool, _dbService, _watchdogBot);
                        spreadRetention.StrategyEvent += OnStrategyEvent;
                        spreadRetention.StrategyFinishEvent += OnStrategyFinish;
                        await spreadRetention.StartStrategy(reloadSettings);
                        strategies.Add(spreadRetention);
                    }
                }
                else if (setting is MaintainingVolumeGraphSettings maintainingVolumeGraphSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var maintainingVolumeGrah = new MaintainingVolumeGraph(PublicDataFeed, PrivateDataFeed, _logger, maintainingVolumeGraphSettings, _connectorsPool, _watchdogBot);
                        maintainingVolumeGrah.StrategyEvent += OnStrategyEvent;
                        maintainingVolumeGrah.StrategyFinishEvent += OnStrategyFinish;
                        await maintainingVolumeGrah.LoadState(state);
                        await maintainingVolumeGrah.StartStrategy(reloadSettings);
                        strategies.Add(maintainingVolumeGrah);
                    }
                }
                else if (setting is HFTSettings hftSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var hftStrategy = new Strategies.HFT.HFTStrategy(PublicDataFeed, PrivateDataFeed, _logger, hftSettings, _connectorsPool, activeOrders, _watchdogBot);
                        hftStrategy.StrategyEvent += OnStrategyEvent;
                        hftStrategy.StrategyFinishEvent += OnStrategyFinish;
                        await hftStrategy.LoadState(state);
                        await hftStrategy.StartStrategy(reloadSettings);
                        strategies.Add(hftStrategy);
                    }
                }
                else if (setting is MarketVolumeSettings marketVolumeSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var maintainingVolumeGrah = new MarketVolumeStrategy(PublicDataFeed, PrivateDataFeed, _logger, marketVolumeSettings, _connectorsPool, _watchdogBot);
                        maintainingVolumeGrah.StrategyEvent += OnStrategyEvent;
                        maintainingVolumeGrah.StrategyFinishEvent += OnStrategyFinish;
                        await maintainingVolumeGrah.StartStrategy(reloadSettings);
                        strategies.Add(maintainingVolumeGrah);
                    }
                }
                else if (setting is TieredSettings tieredSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var tieredStrategy = new TieredStrategy(PublicDataFeed, PrivateDataFeed, _logger, tieredSettings, _connectorsPool, activeOrders, _watchdogBot);
                        tieredStrategy.StrategyEvent += OnStrategyEvent;
                        tieredStrategy.StrategyFinishEvent += OnStrategyFinish;
                        await tieredStrategy.StartStrategy(reloadSettings);
                        strategies.Add(tieredStrategy);
                    }
                }
                else if (setting is ArbitrageV2Settings arbitrageV2Settings)
                {
                    if (setting.IsEnabled)
                    {
                        var arbitrageV2 = new ArbitrageV2Strategy(PublicDataFeed, PrivateDataFeed, _logger, arbitrageV2Settings, _connectorsPool, activeOrders, _dbService, _watchdogBot);
                        arbitrageV2.StrategyEvent += OnStrategyEvent;
                        arbitrageV2.StrategyFinishEvent += OnStrategyFinish;
                        await arbitrageV2.StartStrategy(reloadSettings);
                        strategies.Add(arbitrageV2);
                    }
                }
                else if (setting is RepeatChartSettings chartFollowingSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var repeatChartStrategy = new RepeatChartStrategy(PublicDataFeed, PrivateDataFeed, _logger, chartFollowingSettings, _connectorsPool, _dbService, _watchdogBot);
                        repeatChartStrategy.StrategyEvent += OnStrategyEvent;
                        repeatChartStrategy.StrategyFinishEvent += OnStrategyFinish;
                        await repeatChartStrategy.StartStrategy(reloadSettings);
                        strategies.Add(repeatChartStrategy);
                    }
                }
                else if (setting is RepeatDepthSettings repeatDepthSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var repeatDepthStrategy = new RepeatDepthStrategy(PublicDataFeed, PrivateDataFeed, _logger, repeatDepthSettings, _connectorsPool, _dbService, _watchdogBot);
                        repeatDepthStrategy.StrategyEvent += OnStrategyEvent;
                        repeatDepthStrategy.StrategyFinishEvent += OnStrategyFinish;
                        await repeatDepthStrategy.StartStrategy(reloadSettings);
                        strategies.Add(repeatDepthStrategy);
                    }
                }
                else if (setting is RepeatChartV2Settings repeatChartV2Settings)
                {
                    if (setting.IsEnabled)
                    {
                        var repeatChartV2Strategy = new RepeatChartV2Strategy(PublicDataFeed, PrivateDataFeed, _logger, repeatChartV2Settings, _connectorsPool, _dbService, _watchdogBot);
                        repeatChartV2Strategy.StrategyEvent += OnStrategyEvent;
                        repeatChartV2Strategy.StrategyFinishEvent += OnStrategyFinish;
                        await repeatChartV2Strategy.StartStrategy(reloadSettings);
                        strategies.Add(repeatChartV2Strategy);
                    }
                }
                else if (setting is RepeatDexSettings repeatDexSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var repeatDexStrategy = new RepeatDexStrategy(PublicDataFeed, PrivateDataFeed, _logger, repeatDexSettings, _connectorsPool, _dbService, _watchdogBot);
                        repeatDexStrategy.StrategyEvent += OnStrategyEvent;
                        repeatDexStrategy.StrategyFinishEvent += OnStrategyFinish;
                        await repeatDexStrategy.StartStrategy(reloadSettings);
                        strategies.Add(repeatDexStrategy);
                    }
                }
                else if (setting is TriangleArbitrageV2Settings triangleV2Settings)
                {
                    if (setting.IsEnabled)
                    {
                        var triangleV2Strategy = new TriangleArbitrageV2Strategy(PublicDataFeed, PrivateDataFeed, _logger, triangleV2Settings, _connectorsPool, _dbService, _watchdogBot);
                        triangleV2Strategy.StrategyEvent += OnStrategyEvent;
                        triangleV2Strategy.StrategyFinishEvent += OnStrategyFinish;
                        await triangleV2Strategy.StartStrategy(reloadSettings);
                        strategies.Add(triangleV2Strategy);
                    }
                }
                else if (setting is MarketArbitrageSettings marketArbitrageSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var marketArbitrageStrategy = new MarketArbitrageStrategy(PublicDataFeed, PrivateDataFeed, _logger, marketArbitrageSettings, _connectorsPool, _dbService, _watchdogBot);
                        marketArbitrageStrategy.StrategyEvent += OnStrategyEvent;
                        marketArbitrageStrategy.StrategyFinishEvent += OnStrategyFinish;
                        await marketArbitrageStrategy.StartStrategy(reloadSettings);
                        strategies.Add(marketArbitrageStrategy);
                    }
                }
                else if (setting is TransferSettings transferSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var transferStrategy = new TransferStrategy(PublicDataFeed, PrivateDataFeed, _logger, transferSettings, _connectorsPool, activeOrders, _watchdogBot);
                        transferStrategy.StrategyEvent += OnStrategyEvent;
                        transferStrategy.StrategyFinishEvent += OnStrategyFinish;
                        await transferStrategy.LoadState(state);
                        await transferStrategy.StartStrategy(reloadSettings);
                        strategies.Add(transferStrategy);
                    }
                }
                else if (setting is CandleCloserSettings candleCloseSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var candleCloserStrategy = new CandleCloserStrategy(PublicDataFeed, PrivateDataFeed, _logger, candleCloseSettings, _connectorsPool, activeOrders, _dbService, _watchdogBot);
                        candleCloserStrategy.StrategyEvent += OnStrategyEvent;
                        candleCloserStrategy.StrategyFinishEvent += OnStrategyFinish;
                        await candleCloserStrategy.LoadState(state);
                        await candleCloserStrategy.StartStrategy(reloadSettings);
                        strategies.Add(candleCloserStrategy);
                    }
                }
                else if (setting is FundingArbitrageSettings fundingSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var fundingStrategy = new FundingArbitrageStrategy(PublicDataFeed, PrivateDataFeed, _logger, fundingSettings, _connectorsPool, _watchdogBot);
                        fundingStrategy.StrategyEvent += OnStrategyEvent;
                        fundingStrategy.StrategyFinishEvent += OnStrategyFinish;
                        await fundingStrategy.LoadState(state);
                        await fundingStrategy.StartStrategy(reloadSettings);
                        strategies.Add(fundingStrategy);
                    }
                }
                else if (setting is OptionRollSettings optionSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var optionRollStrategy = new OptionRollStrategy(PublicDataFeed, PrivateDataFeed, _logger, optionSettings, _connectorsPool, _watchdogBot);
                        optionRollStrategy.StrategyEvent += OnStrategyEvent;
                        optionRollStrategy.StrategyFinishEvent += OnStrategyFinish;
                        await optionRollStrategy.LoadState(state);
                        await optionRollStrategy.StartStrategy(reloadSettings);
                        strategies.Add(optionRollStrategy);
                    }
                }
                else if (setting is CancelSettings cancelSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var adaptive = new CancelStrategy(PublicDataFeed, PrivateDataFeed, _logger, cancelSettings, _connectorsPool, _dbService, _watchdogBot);
                        adaptive.StrategyEvent += OnStrategyEvent;
                        adaptive.StrategyFinishEvent += OnStrategyFinish;
                        await adaptive.LoadState(state);
                        await adaptive.StartStrategy(reloadSettings);
                        strategies.Add(adaptive);
                    }
                }
                else if (setting is PerpSpotMakerTakerArbitrageSettings perpSpotArbitrageSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var perpSpotArbitrage = new PerpSpotMakerTakerArbitrageStrategy(PublicDataFeed, PrivateDataFeed, _logger, perpSpotArbitrageSettings, _connectorsPool, activeOrders, _dbService, _watchdogBot);
                        perpSpotArbitrage.StrategyEvent += OnStrategyEvent;
                        perpSpotArbitrage.StrategyFinishEvent += OnStrategyFinish;
                        await perpSpotArbitrage.LoadState(state);
                        await perpSpotArbitrage.StartStrategy(reloadSettings);
                        strategies.Add(perpSpotArbitrage);
                    }
                }
                else if (setting is FeeCalculationSettings feeCalculationSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var feeCalculationStrategy = new FeeCalculationStrategy(PublicDataFeed, PrivateDataFeed, _logger, feeCalculationSettings, _connectorsPool, activeOrders, _watchdogBot);
                        feeCalculationStrategy.StrategyEvent += OnStrategyEvent;
                        feeCalculationStrategy.StrategyFinishEvent += OnStrategyFinish;
                        await feeCalculationStrategy.LoadState(state);
                        await feeCalculationStrategy.StartStrategy(reloadSettings);
                        strategies.Add(feeCalculationStrategy);
                    }
                }
                else if (setting is TwinsMakerSettings twinsMakerSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var twinsMakerStrategy = new TwinsMakerStrategy(PublicDataFeed, PrivateDataFeed, _logger, twinsMakerSettings, _connectorsPool, activeOrders, _watchdogBot);
                        twinsMakerStrategy.StrategyEvent += OnStrategyEvent;
                        twinsMakerStrategy.StrategyFinishEvent += OnStrategyFinish;
                        await twinsMakerStrategy.LoadState(state);
                        await twinsMakerStrategy.StartStrategy(reloadSettings);
                        strategies.Add(twinsMakerStrategy);
                    }
                }
                else if (setting is TwinsTakerSettings twinsTakerSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var twinsTakerStrategy = new TwinsTakerStrategy(PublicDataFeed, PrivateDataFeed, _logger, twinsTakerSettings, _connectorsPool, activeOrders, _watchdogBot);
                        twinsTakerStrategy.StrategyEvent += OnStrategyEvent;
                        twinsTakerStrategy.StrategyFinishEvent += OnStrategyFinish;
                        await twinsTakerStrategy.LoadState(state);
                        await twinsTakerStrategy.StartStrategy(reloadSettings);
                        strategies.Add(twinsTakerStrategy);
                    }
                }
                else if (setting is PerpRiskManagementSettings perpRiskSettings)
                {
                    if (setting.IsEnabled)
                    {
                        var perpRisk = new PerpRiskManagementStrategy(PublicDataFeed, PrivateDataFeed, _logger, perpRiskSettings, _connectorsPool, _dbService, _watchdogBot);
                        perpRisk.StrategyEvent += OnStrategyEvent;
                        perpRisk.StrategyFinishEvent += OnStrategyFinish;
                        await perpRisk.LoadState(state);
                        await perpRisk.StartStrategy(reloadSettings);
                        strategies.Add(perpRisk);
                    }
                }
            }
        }

        

        public async Task StopStrategy(bool cancelOrders)
        {
            _isStarted = false;
            List<Task> stoppedStrategies = new List<Task>();
            foreach (var strategy in strategies)
            {
                _logger.LogInformation($"Start stop strategy {strategy.GetSettings().LogSettingsId} {strategy.GetSettings().MasterStock.StockName} {cancelOrders}");
                try
                {
                    stoppedStrategies.Add(strategy.StopStrategy(cancelOrders));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"{strategy.ToString()} stop error");

                    await OnStrategyEvent(strategy, new StrategyLogicEventArgs
                    {
                        Message = "Stop strategy error",
                        EventType = StrategyEventType.Critical,
                        StrategyId = strategy.GetSettings().LogSettingsId,
                        Exception = ex
                    });
                }
                _logger.LogInformation($"Start stop strategy finish {strategy.GetSettings().LogSettingsId} {strategy.GetSettings().MasterStock.StockName}");
            }
            await Task.WhenAll(stoppedStrategies);
        }

        public async Task OngoingReloadStrategy(BaseSettings<IStockSettings> settings)
        {
            var currentStrategy = strategies.First();
            await currentStrategy.OngoingReload(settings);
        }

        public async Task<Dictionary<string, object>> ExtractState()
        {
            return await strategies.First().ExtractState();
        }

        public async Task DisposeStrategy()
        {
            foreach (var strategy in strategies)
            {
                _logger.LogInformation($"Start stop strategy dispose {strategy.GetSettings().LogSettingsId} {strategy.GetSettings().MasterStock.StockName}");
                try
                {
                    strategy.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"{strategy.ToString()} stop dispose error");
                }
            }
            strategies.Clear();


            try
            {
                await PublicDataFeed.UnsubscribeAll();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"PublicDataFeed unsubscribe stop error");
            }
            PublicDataFeed.Dispose();
            PublicDataFeed = null;
            try
            {
                await PrivateDataFeed.UnsubscribeAll();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"PrivateDataFeed unsubscribe stop error");
            }
            PrivateDataFeed.Dispose();
            PrivateDataFeed = null;

            try
            {
                await _connectorsPool.UnsubscribeAll();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"_connectorsPool unsubscribe stop error");
            }
            _connectorsPool.Dispose();
        }

        public async Task StartAdditionalStocks(List<ISettings<IStockSettings>> settings)
        {
            //await Start(settings);//same
        }

        public async Task ResumeSomeStrategy(int settingsId)
        {
            try
            {
                var needResumeStrategy = strategies.Where(x => x.GetSettings().LogSettingsId == settingsId).FirstOrDefault();
                try
                {
                    await needResumeStrategy.ResumeStrategy();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"ResumeSomeStrategy {needResumeStrategy.ToString()} {settingsId} Resume error");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ResumeSomeStrategy {settingsId} error");
            }
        }
        public bool isRunningStrategy(int settingsId)
        {
            try
            {
                var needStrategy = strategies.Where(x => x.GetSettings().LogSettingsId == settingsId).FirstOrDefault();
                try
                {
                    return needStrategy.IsRunning();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"isRunningStrategy {needStrategy.ToString()} {settingsId} Resume error");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"isRunningStrategy {settingsId} error");
            }
            return false;
        }

        private async Task OnStrategyEvent(object? sender, StrategyLogicEventArgs e)
        {
            if (StrategyEvent != null)
            {
                await StrategyEvent.Invoke(sender, e);
            }
        }
        private async Task OnStrategyFinish(object? sender, StrategyLogicFinishEventArgs e)
        {
            if (StrategyFinishEvent != null)
            {
                await StrategyFinishEvent.Invoke(sender, e);
            }
        }
    }
}
