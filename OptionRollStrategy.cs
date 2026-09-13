using BaseStockConnector.Models.Enums;
using BaseStockConnector.Models.Orders;

using BaseStockConnectorInterface.Models.Enums;
using BaseStockConnectorInterface.Models.Instruments;
using BaseStockConnectorInterface.Models.Orders;

using MarketMakerBot.DataFeed;
using MarketMakerBot.Models;
using MarketMakerBot.Models.StatisticStrategy;
using MarketMakerBot.PublicDataFeed;
using MarketMakerBot.Strategies.Settings;
using MarketMakerBot.Strategies.Settings.SettingsBase;
using MarketMakerBot.Strategies.Settings.StockSettingsBase;
using MarketMakerBot.Utils;

using Microsoft.Extensions.Logging;

using SharedResources.Models.Shared.Enums;
using SharedResources.Models.Shared.Models;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using TelegramBotLibrary;

namespace MarketMakerBot.Strategies
{

    public class OptionTicker
    {
        public string StockName { get; set; }
        public string InstrumentName { get; set; }
        public OptionType OptionType { get; set; }
        public decimal Delta { get; set; }
        public decimal BestAsk { get; set; }
        public decimal BestBid { get; set; }
        public DateTime Expiration { get; set; }
        public decimal Strike { get; set; }
    }
    public class OptionRollStrategy : StrategyBase
    {
        private OptionRollStatistic _statistic = new OptionRollStatistic();
        private readonly PrivateDataFeedConnectionsPool _privateDataFeed;
        private readonly OptionRollSettings _settings;
        private readonly ConnectorsPool _connectorsPool;
        private readonly int _createOrdersErrorsCountLimit = 50;
        private List<Position> _callPositions = new();
        private List<Position> _putPositions = new();

        private List<OptionTicker> _options = new();

        private Timer strategyTimer;
        private readonly ITelegramBot _watchdogBot;

        public OptionRollStrategy(PublicDataFeedConnectionsPool publicStockPool,
            PrivateDataFeedConnectionsPool privateDataFeed,
            ILogger logger,
            OptionRollSettings settings,
            ConnectorsPool connectorsPool,
            ITelegramBot watchdogBot) : base(logger, settings.FirstArbitrageOrderStepFromSlaveStock, settings.MasterStock, privateDataFeed)
        {
            _TagMessage = this.GetType().Name;

            _privateDataFeed = privateDataFeed;
            _settings = settings;
            _connectorsPool = connectorsPool;
            _publicStockPool = publicStockPool;
            _watchdogBot = watchdogBot;
            LogSettingsId = _settings.LogSettingsId;
            MasterStokName = _settings.MasterStock.StockName.ToString();
        }

        public override async Task StartStrategy(SharedReloadSettings reloadSettings)
        {
            _logger.LogInformation($"[{_TagMessage}] Start {_settings.MasterStock.StockName}");
            masterStock = await _connectorsPool.GetStockConnector(_settings.MasterStock);

            await _privateDataFeed.AddOrderUpdateSubscription(masterStock.SocketClient, _settings.MasterStock.InstrumentName, OrderUpdates, InstrumentType.Option);

            RunTimers();
        }

        private async Task RefreshPositionsAsync()
        {
            _callPositions.Clear();
            _putPositions.Clear();
            var positions = await masterStock.HttpClient.Trading.GetPositionsAsync(InstrumentType.Option, _settings.MasterStock.InstrumentName).LegacyListOrEmptyIfOrderNotFoundAsync();

            var positionsByExpiration = positions.Where(p => p.Size != 0 && p.InstrumentName.Contains(_settings.Expiration.ToString("dMMMyy", CultureInfo.InvariantCulture).ToUpper(), StringComparison.OrdinalIgnoreCase));

            foreach (var userPosition in positionsByExpiration)
            {
                var list = userPosition.OptionType == OptionType.Call ? _callPositions : _putPositions;

                list.Add(userPosition);
            }

            if (_callPositions.Any())
            {
                foreach (var pos in _callPositions)
                {
                    _logger.LogInformation($"[{_TagMessage}][CallPos] {pos.InstrumentName} Size={pos.Size} Δ={pos.Delta:F4}");
                }
            }

            if (_putPositions.Any())
            {
                foreach (var pos in _putPositions)
                {
                    _logger.LogInformation($"[{_TagMessage}][PutPos] {pos.InstrumentName} Size={pos.Size} Δ={pos.Delta:F4}");
                }
            }

        }

        private async Task RefreshOptionsAsync()
        {
            _logger.LogDebug($"[{_TagMessage}][RunMainCycle] Start RefreshOptionsAsync");
            _options.Clear();

            _options = await GetOptionsAsync(_settings.MasterStock.InstrumentName, _settings.Expiration);
            _logger.LogDebug($"[{_TagMessage}][RunMainCycle] End RefreshOptionsAsync");
        }

        private async Task<List<OptionTicker>> GetOptionsAsync(string symbol, DateTime expiration)
        {
            var optionsResponse = await masterStock.HttpClient.MarketData.GetOptionListAsync(symbol, expiration).ToLegacyExchangeResponseAsync();

            if (!optionsResponse.IsSuccess)
                throw new Exception($"[{_TagMessage}] Error fetching options for {symbol} on {expiration.ToString("dMMMyy", CultureInfo.InvariantCulture)}: {optionsResponse.OriginalResponse}");

            var options = optionsResponse.Model;

            List<OptionTicker> result = new List<OptionTicker>();

            foreach (var option in options)
            {
                var ticker = new OptionTicker
                {
                    OptionType = option.OptionType,
                    BestAsk = option.Asks.FirstOrDefault()?.Price ?? 0,
                    BestBid = option.Bids.FirstOrDefault()?.Price ?? 0,
                    Delta = option.OptionData?.Greeks.Delta ?? 0,
                    Expiration = option.ExpirationTime,
                    InstrumentName = option.InstrumentName,
                    StockName = MasterStokName,
                    Strike = option.Strike,
                };
                result.Add(ticker);
            }
            return result;

        }

        private void RunTimers()
        {
            RunTimersBuySell();

            strategyTimer = new Timer(async (e) =>
            {
                try
                {
                    await RunMainCycle();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[{_TagMessage}] Timer {_settings.MasterStock.StockName} {_settings.MasterStock.InstrumentName} {LogSettingsId}");
                }

                if (strategyTimer != null && _isOrdersEnable)
                    strategyTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0));

            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0));
        }


        private async Task RunMainCycle()
        {
            try
            {
                await RefreshPositionsAsync();

                if (_callPositions.Count > 1 || _putPositions.Count > 1)
                {
                    await CloseAllPositionsExceptTopByDelta(_callPositions);
                    await CloseAllPositionsExceptTopByDelta(_putPositions);
                    return;
                }

                var callDelta = CalculateTotalDelta(_callPositions);
                var putDelta = CalculateTotalDelta(_putPositions);

                // save statistic
                var currentStatistic = _statistic.Clone();
                currentStatistic.CurrentCallPositions = string.Join(",", _callPositions.Select(e => $"{e.InstrumentName} Size={e.Size}").ToArray());
                currentStatistic.CurrentPutPositions = string.Join(",", _putPositions.Select(e => $"{e.InstrumentName} Size={e.Size}").ToArray());
                currentStatistic.TotalCallDelta = callDelta.TruncateToPrecision(0.0001m);
                currentStatistic.TotalPutDelta = putDelta.TruncateToPrecision(0.0001m);
                if (!currentStatistic.Equals(_statistic))
                {
                    _logger.LogInformation($"[{_TagMessage}][RunMainCycle] Statistic changed");
                    _statistic = currentStatistic;
                    NotifyStrategyStatisticEvent(string.Empty, StrategyEventType.Statistic, _statistic);
                }
                else
                {
                    _logger.LogDebug($"[{_TagMessage}][RunMainCycle] Statistic not changed");
                }


                _logger.LogInformation($"[{_TagMessage}][RunMainCycle] Call Δ: {callDelta:F4}, Put Δ: {putDelta:F4}");

                if (callDelta > 0.445m || putDelta > 0.445m)
                {
                    _logger.LogWarning($"[{_TagMessage}][RunMainCycle] Delta too high. Closing all positions.");
                    await CloseAllPositions();
                    StopStrategyIfCompleted();
                    return;
                }

                var deltaDiff = Math.Abs(callDelta - putDelta);
                if (deltaDiff <= _settings.Diff)
                {
                    _logger.LogInformation($"[{_TagMessage}][RunMainCycle] Delta diff {deltaDiff:F4} within acceptable range.");
                    return;
                }

                var typeToRoll = callDelta > putDelta ? OptionType.Put : OptionType.Call;
                var totalDelta = Math.Max(callDelta, putDelta);

                var targetDelta = SelectTargetDelta(totalDelta);
                if (targetDelta == null)
                {
                    _logger.LogDebug($"[{_TagMessage}][RunMainCycle] No roll — delta not in rollable zone.");
                    return;
                }

                _logger.LogInformation($"[{_TagMessage}][RunMainCycle] type={typeToRoll}, totalDelta={totalDelta:F4}, targetDelta={targetDelta:F2}");


                await RefreshOptionsAsync();


                var currentList = typeToRoll == OptionType.Call ? _callPositions : _putPositions;
                var oppositeList = typeToRoll == OptionType.Call ? _putPositions : _callPositions;
                _logger.LogDebug($"[{_TagMessage}][RunMainCycle] TypeToRoll {typeToRoll}");

                if (!currentList.Any())
                {
                    _logger.LogWarning($"[{_TagMessage}][RunMainCycle] No active positions to roll for {typeToRoll}");
                    return;
                }

                var targetPosition = currentList.OrderByDescending(p => Math.Abs(p.Delta / p.Size)).First();
                var oppositeVolume = oppositeList.OrderByDescending(e => Math.Abs(e.Delta / e.Size)).First().Size;

                await RollOption(typeToRoll, targetDelta.Value, targetPosition, oppositeVolume);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RunMainCycle");
            }
        }

        private async Task CloseAllPositionsExceptTopByDelta(List<Position> currentList)
        {
            _logger.LogDebug($"[{_TagMessage}][RunMainCycle] Start CloseAllPositionsExceptTopByDelta");

            var topByDelta = currentList
                       .OrderByDescending(p => Math.Abs(p.Delta / p.Size))
                       .First();

            var positionsToDelete = new List<Position>();

            foreach (var pos in currentList.Where(p => p.InstrumentName != topByDelta.InstrumentName))
            {
                if (pos.Size == 0) continue;

                await PlaceOrderAsync(pos.InstrumentName, Direction.BUY, Math.Abs(pos.Size), PositionOrderType.Close);

                _statistic.LastAction = $"Closed rolled-out position: {pos.InstrumentName}, size {pos.Size}";
                _logger.LogInformation($"[{_TagMessage}][CloseAllPositionsExceptTopByDelta] Closed rolled-out position: {pos.InstrumentName}, size {pos.Size}");
            }

            _logger.LogDebug($"[{_TagMessage}][RunMainCycle] End CloseAllPositionsExceptTopByDelta");
        }

        private async Task RollOption(OptionType typeToRoll, decimal targetDelta, Position targetPosition, decimal oppositeVolume)
        {


            // Найдём ближайший страйк по дельте
            var candidates = _options
                .Where(x => x.OptionType == typeToRoll && x.Expiration.Date == _settings.Expiration.Date)
                .ToList();

            var best = candidates
                .OrderBy(x => Math.Abs(Math.Abs(x.Delta) - targetDelta))
                .ThenBy(x => Math.Abs(x.Delta)) // если дельта одинаково близка — берём меньшую
                .FirstOrDefault();

            if (best == null)
            {
                _logger.LogWarning($"[{_TagMessage}][RollOption] No suitable candidate found for target Δ={targetDelta:F2} in {typeToRoll}");
                return;
            }

            var currentList = typeToRoll == OptionType.Call ? _callPositions : _putPositions;

            if (best.InstrumentName == targetPosition.InstrumentName)
            {
                var volumeDiff = Math.Abs(oppositeVolume) - Math.Abs(targetPosition.Size);

                // Пробуем выравнять, если есть разница по объему
                if (volumeDiff != 0)
                {
                    var adjustDirection = volumeDiff > 0 ? Direction.SELL : Direction.BUY;

                    _logger.LogInformation($"[{_TagMessage}][RollOption] Adjusting position {targetPosition.InstrumentName} by {Math.Abs(volumeDiff)} in direction {adjustDirection}");

                    await PlaceOrderAsync(
                        targetPosition.InstrumentName,
                        adjustDirection,
                        Math.Abs(volumeDiff),
                        volumeDiff > 0 ? PositionOrderType.Open : PositionOrderType.Close
                    );

                    _statistic.LastAction = $"Position {targetPosition.InstrumentName} adjusted to {oppositeVolume}";
                    _logger.LogInformation($"[{_TagMessage}][RollOption] Position {targetPosition.InstrumentName} adjusted to {oppositeVolume}");
                }
                else
                {
                    _statistic.LastAction = $"Target option {best.InstrumentName} already held. No roll needed";
                    _logger.LogInformation($"[{_TagMessage}][RollOption] Target option {best.InstrumentName} already held. No roll needed.");
                }
                return;
            }

            _logger.LogInformation($"[{_TagMessage}][RollOption][SELECTED] {best.InstrumentName} → Δ={best.Delta:F2}, Strike={best.Strike}, BestAsk={best.BestAsk}, BestBid={best.BestBid}");

            // Зкарываем старый опцион - покупка
            await PlaceOrderAsync(targetPosition.InstrumentName, Direction.BUY, Math.Abs(targetPosition.Size), PositionOrderType.Close);

            // Если параметр strategyType = Close, закрытие позиции и будет конец выполнения функции ролла. 
            // На этом этапе бот должен остановиться со статусом Finished
            if (_settings.OptionStrategyType == OptionStrategyType.Close)
            {
                StopStrategyIfCompleted();
            }

            // Открываем новый опцион — продажа
            if (oppositeVolume != 0)
            {
                await PlaceOrderAsync(best.InstrumentName, Direction.SELL, Math.Abs(oppositeVolume), PositionOrderType.Open);
            }
            else
            {
                _logger.LogInformation($"[{_TagMessage}][RollOption] Skipped opening new position for {best.InstrumentName} — volume is zero.");
            }

            _statistic.LastAction = $"Roll done. Closed: {targetPosition.InstrumentName}, Size={Math.Abs(targetPosition.Size)}, Opened: {best.InstrumentName}, Size={Math.Abs(oppositeVolume)}";
            _logger.LogInformation($"[{_TagMessage}][RollOption][ROLL DONE] Closed: {targetPosition.InstrumentName}, Size={Math.Abs(targetPosition.Size)}, Opened: {best.InstrumentName}, Size={Math.Abs(oppositeVolume)}");
        }


        private decimal CalculateTotalDelta(List<Position> positions)
        {
            if (!positions.Any())
                return 0;

            var totalSize = positions.Sum(p => Math.Abs(p.Size));
            if (totalSize == 0)
                return 0;

            //return positions.Sum(p => Math.Abs(p.Size) * Math.Abs(p.Delta)) / totalSize;

            return positions.Sum(p => Math.Abs(p.Delta)) / totalSize;
        }


        private decimal? SelectTargetDelta(decimal delta)
        {
            if (delta > 0.395m) return 0.40m;
            if (delta > 0.295m) return 0.30m;
            if (delta > 0.195m) return 0.20m;
            return null;
        }

        private async Task CloseAllPositions()
        {
            try
            {
                foreach (var position in _callPositions.Concat(_putPositions))
                {
                    if (position.Size == 0) continue;

                    var volume = Math.Abs(position.Size);

                    _logger.LogInformation($"[{_TagMessage}] Closing position {position.InstrumentName}, size: {volume}, direction: {Direction.BUY}");

                    await PlaceOrderAsync(position.InstrumentName, Direction.BUY, volume, PositionOrderType.Close);
                }

                _callPositions.Clear();
                _putPositions.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while closing all positions");
            }
        }


        public override async Task StopStrategy(bool cancelOrders = true)
        {
            _logger.LogInformation($"[{_TagMessage}][StopStrategy] start");
            _isOrdersEnable = false;
            UnsubscribeFromEvents();

            strategyTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            timerSyncState?.Change(Timeout.Infinite, Timeout.Infinite);
            _logger.LogInformation($"[{_TagMessage}][StopStrategy] finished");
        }

        private async Task PlaceOrderAsync(string instrumentName, Direction direction, decimal volume, PositionOrderType position)
        {
            var order = new SharedOrder
            {
                InstrumentName = instrumentName,
                OrderDirection = (SharedDirection)direction,
                Volume = volume,
                SystemOrderId = Guid.NewGuid(),
                OrderType = SharedRecources.Models.Shared.Enums.SharedOrderType.Market,
                InstrumentType = SharedInstrumentType.Option,
                StockName = _settings.MasterStock.StockName,
                SettingId = _settings.LogSettingsId
            };

            arbitrageOrders.Add(order);

            var makeOrderResultExecution = await masterStock.HttpClient.Trading.MakeOrderAsync(
                InstrumentType.Option,
                (OrderType)order.OrderType,
                order.OrderDirection.ToDirection(),
                position,
                order.InstrumentName,
                order.Volume,
                order.Price,
                order.SystemOrderId.ToString("N"));
            var makeOrderResultErrorType = makeOrderResultExecution.Error?.Type ?? ErrorType.Unknown;
            var makeOrderResultErrorMessage = makeOrderResultExecution.Error?.ToString() ?? "Unknown error";

            if (makeOrderResultExecution.IsSuccess && makeOrderResultExecution.Data != null)
            {
                order.ConfirmDateUtc = DateTime.UtcNow;
                order.StockOrderId = makeOrderResultExecution.Data.StockOrderId;

                _publicStockPool.OrderCreateOrUpdate(masterStock, order);
            }
            else
            {
                arbitrageOrders.Remove(order);

                if (makeOrderResultErrorType != ErrorType.ServiceUnavailable)
                    _createOrdersErrorsCount++;

                if (_createOrdersErrorsCount > _createOrdersErrorsCountLimit || makeOrderResultErrorType == ErrorType.BalanceNotEnough)
                {
                    _isMainStockOrdersEnable = false;
                    _isOrdersEnable = false;

                    LogErrorForStatistics($"[{_TagMessage}] {_settings.MasterStock.StockName} {_settings.MasterStock.InstrumentName} {_settings.LogSettingsId} disable create new orders. {makeOrderResultErrorType.ToString()}");
                    await _watchdogBot.SendMessageAsync($"[{_TagMessage}] Disable create new orders for {_settings.MasterStock.StockName} {_settings.MasterStock.InstrumentName} {_settings.LogSettingsId}");

                    NotifyStrategyError($"[{_TagMessage}] Not enough balance for creating new orders or reached errors limit {masterStock.StockName}", StrategyEventType.Critical);
                }

                _logger.LogCritical($"[{_TagMessage}] Create order error Spread arbitrage {_settings.MasterStock.StockName} {_settings.MasterStock.InstrumentName} {order.Price} {order.Volume} {order.StrategyOrderType} {makeOrderResultErrorMessage} {_settings.LogSettingsId}");
                NotifyStrategyError($"[{_TagMessage}] Api order create error {makeOrderResultErrorType} {_settings.MasterStock.StockName} {_settings.MasterStock.InstrumentName} {makeOrderResultErrorMessage}", StrategyEventType.Warning);
            }
        }

        public void StopStrategyIfCompleted()
        {
            _isOrdersEnable = false;
            _logger.LogInformation($"[{_TagMessage}] stop {_settings.MasterStock.StockName} Task finished");
            _watchdogBot.SendMessage($"[{_TagMessage}] task finished {_settings.MasterStock.StockName} {_settings.MasterStock.InstrumentName} {_settings.LogSettingsId}");
            NotifyStrategyFinished();
        }

        public override ISettings<IStockSettings> GetSettings()
        {
            return _settings;
        }

        #region Not Need
        public override Task RessetStrategy()
        {
            throw new NotImplementedException();
        }
        public override decimal SlaveStockVolumeSinkArbitrage(Guid? mainOrderLinkOrderId, SharedDirection direction, decimal volumeArbitrage, decimal mainOrderPrice, string firstOrderIdFromStock, SharedOrder mainOrder, SharedStrategyOrderType orderType = SharedStrategyOrderType.SpreadOrder)
        {
            return 0;
        }
        public override void Dispose()
        {
        }
        #endregion
    }
}
