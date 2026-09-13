using System.Globalization;

using OptionRollerUI.Deribit;
using OptionRollerUI.Models;

namespace OptionRollerUI.Strategy;

public class StrategyStatistic
{
    public string CurrentCallPositions { get; set; } = "";
    public string CurrentPutPositions { get; set; } = "";
    public decimal TotalCallDelta { get; set; }
    public decimal TotalPutDelta { get; set; }
    public string LastAction { get; set; } = "";

    public StrategyStatistic Clone() => (StrategyStatistic)MemberwiseClone();

    public bool Equals(StrategyStatistic other) =>
        CurrentCallPositions == other.CurrentCallPositions &&
        CurrentPutPositions == other.CurrentPutPositions &&
        TotalCallDelta == other.TotalCallDelta &&
        TotalPutDelta == other.TotalPutDelta &&
        LastAction == other.LastAction;
}

public class OptionRollStrategy
{
    private readonly OptionRollSettings _settings;
    private DeribitClient? _client;

    private List<Position> _callPositions = new();
    private List<Position> _putPositions = new();
    private List<OptionTicker> _options = new();
    private StrategyStatistic _statistic = new();
    private string _lastLoggedPositions = "";

    private Timer? _timer;
    private volatile bool _running;

    // ── Events ────────────────────────────────────────────────────────────────

    public event Action<string>? OnLog;
    public event Action<StrategyStatus>? OnStatusChanged;
    public event Action<StrategyStatistic>? OnStatisticUpdated;
    public event Action<List<Position>, List<Position>>? OnPositionsUpdated;

    public OptionRollStrategy(OptionRollSettings settings)
    {
        _settings = settings;
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public void Start()
    {
        if (_running) return;

        _client = new DeribitClient(_settings.ClientId, _settings.ClientSecret, _settings.UseTestnet);
        _running = true;
        OnStatusChanged?.Invoke(StrategyStatus.Running);
        Log("Strategy started.");

        _timer = new Timer(async _ =>
        {
            if (!_running) return;
            try { await RunMainCycleAsync(); }
            catch (Exception ex) { Log($"[ERROR] {ex.Message}"); }
            finally
            {
                if (_running)
                    _timer?.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
            }
        }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
    }

    public void Stop()
    {
        _running = false;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;
        _client?.Dispose();
        _client = null;
        Log("Strategy stopped.");
        OnStatusChanged?.Invoke(StrategyStatus.Stopped);
    }

    // ── Main cycle (mirrors original OptionRollStrategy.RunMainCycle) ──────────

    private async Task RunMainCycleAsync()
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

        var current = _statistic.Clone();
        current.CurrentCallPositions = string.Join(", ", _callPositions.Select(p => $"{p.InstrumentName} Sz={p.Size}"));
        current.CurrentPutPositions = string.Join(", ", _putPositions.Select(p => $"{p.InstrumentName} Sz={p.Size}"));
        current.TotalCallDelta = Truncate(callDelta, 0.0001m);
        current.TotalPutDelta = Truncate(putDelta, 0.0001m);

        if (!current.Equals(_statistic))
        {
            _statistic = current;
            OnStatisticUpdated?.Invoke(_statistic);
        }

        OnPositionsUpdated?.Invoke(_callPositions.ToList(), _putPositions.ToList());

        if (callDelta > 0.445m || putDelta > 0.445m)
        {
            Log("[WARN] Delta too high — closing all positions.");
            await CloseAllPositions();
            Finish();
            return;
        }

        var deltaDiff = Math.Abs(callDelta - putDelta);
        if (deltaDiff <= _settings.Diff)
            return;

        var typeToRoll = callDelta > putDelta ? OptionType.Put : OptionType.Call;
        var totalDelta = Math.Max(callDelta, putDelta);

        var targetDelta = SelectTargetDelta(totalDelta);
        if (targetDelta == null)
            return;

        Log($"Rolling {typeToRoll}, totalDelta={totalDelta:F4}, targetDelta={targetDelta:F2}");

        await RefreshOptionsAsync();

        var currentList = typeToRoll == OptionType.Call ? _callPositions : _putPositions;
        var oppositeList = typeToRoll == OptionType.Call ? _putPositions : _callPositions;

        if (!currentList.Any())
        {
            Log($"[WARN] No active positions to roll for {typeToRoll}");
            return;
        }

        var targetPosition = currentList.OrderByDescending(p => Math.Abs(p.Delta / p.Size)).First();
        var oppositeTop = oppositeList.OrderByDescending(e => Math.Abs(e.Delta / e.Size)).FirstOrDefault();
        if (oppositeTop == null)
        {
            Log($"[WARN] No opposite positions found for {typeToRoll} — skipping roll.");
            return;
        }
        var oppositeVolume = oppositeTop.Size;

        await RollOptionAsync(typeToRoll, targetDelta.Value, targetPosition, oppositeVolume);
    }

    // ── Position refresh ───────────────────────────────────────────────────────

    private async Task RefreshPositionsAsync()
    {
        _callPositions.Clear();
        _putPositions.Clear();

        var all = await _client!.GetPositionsAsync(_settings.Currency);

        var expKey = _settings.Expiration.ToString("dMMMyy", CultureInfo.InvariantCulture).ToUpper();

        foreach (var pos in all.Where(p => p.Size != 0 && p.InstrumentName.Contains(expKey, StringComparison.OrdinalIgnoreCase)))
        {
            if (pos.OptionType == OptionType.Call && _settings.CallStrike != 0 && pos.Strike != _settings.CallStrike)
                continue;
            if (pos.OptionType == OptionType.Put && _settings.PutStrike != 0 && pos.Strike != _settings.PutStrike)
                continue;

            var list = pos.OptionType == OptionType.Call ? _callPositions : _putPositions;
            list.Add(pos);
        }

        var posSnapshot = string.Join("|",
            _callPositions.Select(p => $"C:{p.InstrumentName} Sz={p.Size} Δ={p.Delta:F4}")
            .Concat(_putPositions.Select(p => $"P:{p.InstrumentName} Sz={p.Size} Δ={p.Delta:F4}")));

        if (posSnapshot != _lastLoggedPositions)
        {
            _lastLoggedPositions = posSnapshot;
            foreach (var p in _callPositions) Log($"[CallPos] {p.InstrumentName} Sz={p.Size} Δ={p.Delta:F4}");
            foreach (var p in _putPositions)  Log($"[PutPos]  {p.InstrumentName} Sz={p.Size} Δ={p.Delta:F4}");
        }
    }

    private async Task RefreshOptionsAsync()
    {
        _options = await _client!.GetOptionsAsync(_settings.Currency, _settings.Expiration);
        Log($"Loaded {_options.Count} options for {_settings.Expiration:dMMMyy}.");
    }

    // ── Consolidation: keep only top-delta leg ─────────────────────────────────

    private async Task CloseAllPositionsExceptTopByDelta(List<Position> list)
    {
        if (list.Count == 0) return;

        var top = list.OrderByDescending(p => Math.Abs(p.Delta / p.Size)).First();

        foreach (var pos in list.Where(p => p.InstrumentName != top.InstrumentName && p.Size != 0))
        {
            await PlaceOrderAsync(pos.InstrumentName, Direction.Buy, Math.Abs(pos.Size), PositionOrderType.Close);
            UpdateLastAction($"Closed excess position: {pos.InstrumentName} Sz={pos.Size}");
        }
    }

    // ── Roll logic ─────────────────────────────────────────────────────────────

    private async Task RollOptionAsync(OptionType typeToRoll, decimal targetDelta, Position targetPosition, decimal oppositeVolume)
    {
        var candidates = _options
            .Where(x => x.OptionType == typeToRoll && x.Expiration.Date == _settings.Expiration.Date)
            .ToList();

        var best = candidates
            .OrderBy(x => Math.Abs(Math.Abs(x.Delta) - targetDelta))
            .ThenBy(x => Math.Abs(x.Delta))
            .FirstOrDefault();

        if (best == null)
        {
            Log($"[WARN] No candidate for Δ={targetDelta:F2} in {typeToRoll}");
            return;
        }

        if (best.InstrumentName == targetPosition.InstrumentName)
        {
            var volumeDiff = Math.Abs(oppositeVolume) - Math.Abs(targetPosition.Size);
            if (volumeDiff != 0)
            {
                var dir = volumeDiff > 0 ? Direction.Sell : Direction.Buy;
                var posType = volumeDiff > 0 ? PositionOrderType.Open : PositionOrderType.Close;
                await PlaceOrderAsync(targetPosition.InstrumentName, dir, Math.Abs(volumeDiff), posType);
                UpdateLastAction($"Adjusted {targetPosition.InstrumentName} by {volumeDiff:+0.##;-0.##}");
            }
            else
            {
                UpdateLastAction($"Already at target: {best.InstrumentName}. No roll needed.");
            }
            return;
        }

        Log($"[ROLL] Closing {targetPosition.InstrumentName}, opening {best.InstrumentName} Δ={best.Delta:F2} Strike={best.Strike}");

        // Close old position
        await PlaceOrderAsync(targetPosition.InstrumentName, Direction.Buy, Math.Abs(targetPosition.Size), PositionOrderType.Close);

        if (_settings.StrategyType == OptionStrategyType.Close)
        {
            Finish();
            return;
        }

        // Open new position
        if (oppositeVolume != 0)
            await PlaceOrderAsync(best.InstrumentName, Direction.Sell, Math.Abs(oppositeVolume), PositionOrderType.Open);
        else
            Log("Skipped opening — opposite volume is zero.");

        UpdateLastAction($"Roll done. Closed={targetPosition.InstrumentName}, Opened={best.InstrumentName} Sz={Math.Abs(oppositeVolume)}");
    }

    // ── Order placement ────────────────────────────────────────────────────────

    private async Task PlaceOrderAsync(string instrument, Direction dir, decimal volume, PositionOrderType posType)
    {
        Log($"[ORDER] {dir} {volume} {instrument} ({posType})");
        try
        {
            var result = await _client!.PlaceOrderAsync(instrument, dir, volume, posType);
            var orderId = result?.Order?.OrderId ?? "?";
            Log($"[ORDER] Confirmed: {orderId} state={result?.Order?.OrderState}");
        }
        catch (Exception ex)
        {
            Log($"[ORDER ERROR] {instrument}: {ex.Message}");
            throw;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static decimal CalculateTotalDelta(List<Position> positions)
    {
        if (!positions.Any()) return 0;
        var totalSize = positions.Sum(p => Math.Abs(p.Size));
        if (totalSize == 0) return 0;
        return positions.Sum(p => Math.Abs(p.Delta)) / totalSize;
    }

    private static decimal? SelectTargetDelta(decimal delta)
    {
        if (delta > 0.395m) return 0.40m;
        if (delta > 0.295m) return 0.30m;
        if (delta > 0.195m) return 0.20m;
        return null;
    }

    private async Task CloseAllPositions()
    {
        foreach (var pos in _callPositions.Concat(_putPositions).Where(p => p.Size != 0))
            await PlaceOrderAsync(pos.InstrumentName, Direction.Buy, Math.Abs(pos.Size), PositionOrderType.Close);

        _callPositions.Clear();
        _putPositions.Clear();
    }

    private void Finish()
    {
        _running = false;
        Log("Strategy finished.");
        OnStatusChanged?.Invoke(StrategyStatus.Finished);
    }

    private void UpdateLastAction(string msg)
    {
        _statistic.LastAction = msg;
        OnStatisticUpdated?.Invoke(_statistic);
        Log(msg);
    }

    private void Log(string msg) =>
        OnLog?.Invoke($"[{DateTime.UtcNow:HH:mm:ss}Z] {msg}");

    private static decimal Truncate(decimal value, decimal step) =>
        Math.Truncate(value / step) * step;
}
