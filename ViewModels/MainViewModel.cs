using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using OptionRollerUI.Models;
using OptionRollerUI.Strategy;

namespace OptionRollerUI.ViewModels;

public class PositionRow
{
    public string Side { get; init; } = "";
    public string Instrument { get; init; } = "";
    public string Size { get; init; } = "";
    public string Delta { get; init; } = "";
}

public class MainViewModel : INotifyPropertyChanged
{
    private OptionRollStrategy? _strategy;

    // ── Settings (bindable) ───────────────────────────────────────────────────

    private string _clientId = "";
    public string ClientId { get => _clientId; set { _clientId = value; OnPropertyChanged(); } }

    private string _clientSecret = "";
    public string ClientSecret { get => _clientSecret; set { _clientSecret = value; OnPropertyChanged(); } }

    private string _currency = "BTC";
    public string Currency { get => _currency; set { _currency = value; OnPropertyChanged(); } }

    private string _expiration = "";
    public string Expiration { get => _expiration; set { _expiration = value; OnPropertyChanged(); } }

    private string _callStrike = "0";
    public string CallStrike { get => _callStrike; set { _callStrike = value; OnPropertyChanged(); } }

    private string _putStrike = "0";
    public string PutStrike { get => _putStrike; set { _putStrike = value; OnPropertyChanged(); } }

    private string _diff = "0.05";
    public string Diff { get => _diff; set { _diff = value; OnPropertyChanged(); } }

    private OptionStrategyType _strategyType = OptionStrategyType.Roll;
    public OptionStrategyType StrategyType { get => _strategyType; set { _strategyType = value; OnPropertyChanged(); } }

    public IEnumerable<OptionStrategyType> StrategyTypes =>
        Enum.GetValues<OptionStrategyType>();

    private bool _useTestnet;
    public bool UseTestnet { get => _useTestnet; set { _useTestnet = value; OnPropertyChanged(); } }

    // ── Status ─────────────────────────────────────────────────────────────────

    private string _statusText = "Stopped";
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

    private string _statusColor = "#FF5555";
    public string StatusColor { get => _statusColor; set { _statusColor = value; OnPropertyChanged(); } }

    private string _callDelta = "—";
    public string CallDelta { get => _callDelta; set { _callDelta = value; OnPropertyChanged(); } }

    private string _putDelta = "—";
    public string PutDelta { get => _putDelta; set { _putDelta = value; OnPropertyChanged(); } }

    private string _lastAction = "—";
    public string LastAction { get => _lastAction; set { _lastAction = value; OnPropertyChanged(); } }

    // ── Collections ────────────────────────────────────────────────────────────

    public ObservableCollection<PositionRow> Positions { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    // ── Commands ───────────────────────────────────────────────────────────────

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ClearLogCommand { get; }

    public MainViewModel()
    {
        StartCommand = new RelayCommand(Start, () => _strategy == null || !IsRunning);
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        ClearLogCommand = new RelayCommand(() => Logs.Clear());
        LoadSettingsFromFile();
    }

    private void LoadSettingsFromFile()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        if (!File.Exists(path))
            path = Path.Combine(Directory.GetCurrentDirectory(), "settings.json");
        if (!File.Exists(path)) return;

        try
        {
            var json = JObject.Parse(File.ReadAllText(path));
            _clientId     = json["ClientId"]?.Value<string>()     ?? _clientId;
            _clientSecret = json["ClientSecret"]?.Value<string>()  ?? _clientSecret;
            _currency     = json["Currency"]?.Value<string>()      ?? _currency;
            _expiration   = json["Expiration"]?.Value<string>()    ?? _expiration;
            _callStrike   = json["CallStrike"]?.Value<string>()    ?? _callStrike;
            _putStrike    = json["PutStrike"]?.Value<string>()     ?? _putStrike;
            _diff         = json["Diff"]?.Value<string>()          ?? _diff;
            _useTestnet   = json["UseTestnet"]?.Value<bool>()      ?? _useTestnet;
            if (Enum.TryParse<OptionStrategyType>(json["StrategyType"]?.Value<string>(), out var st))
                _strategyType = st;
        }
        catch { /* ignore malformed settings */ }
    }

    private bool IsRunning => _statusText == "Running";

    // ── Start / Stop ───────────────────────────────────────────────────────────

    private void Start()
    {
        if (!TryBuildSettings(out var settings)) return;

        _strategy = new OptionRollStrategy(settings!);
        _strategy.OnLog += msg => Dispatch(() =>
        {
            Logs.Insert(0, msg);
            if (Logs.Count > 500) Logs.RemoveAt(Logs.Count - 1);
        });
        _strategy.OnStatusChanged += status => Dispatch(() => ApplyStatus(status));
        _strategy.OnStatisticUpdated += stat => Dispatch(() =>
        {
            CallDelta = stat.TotalCallDelta.ToString("F4");
            PutDelta = stat.TotalPutDelta.ToString("F4");
            LastAction = stat.LastAction;
        });
        _strategy.OnPositionsUpdated += (calls, puts) => Dispatch(() => RefreshPositions(calls, puts));

        _strategy.Start();
    }

    private void Stop()
    {
        _strategy?.Stop();
        _strategy = null;
    }

    private bool TryBuildSettings(out OptionRollSettings? settings)
    {
        settings = null;

        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            MessageBox.Show("Client ID and Secret are required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!DateTime.TryParse(Expiration, out var expDate))
        {
            MessageBox.Show("Invalid expiration date. Use format: yyyy-MM-dd", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!decimal.TryParse(Diff, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var diff))
        {
            MessageBox.Show("Invalid Diff value.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        decimal.TryParse(CallStrike, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var callStrike);
        decimal.TryParse(PutStrike, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var putStrike);

        settings = new OptionRollSettings
        {
            ClientId = ClientId,
            ClientSecret = ClientSecret,
            Currency = Currency.Trim().ToUpper(),
            Expiration = expDate,
            CallStrike = callStrike,
            PutStrike = putStrike,
            Diff = diff,
            StrategyType = StrategyType,
            UseTestnet = UseTestnet
        };
        return true;
    }

    // ── UI helpers ─────────────────────────────────────────────────────────────

    private void ApplyStatus(StrategyStatus status)
    {
        StatusText = status switch
        {
            StrategyStatus.Running => "Running",
            StrategyStatus.Finished => "Finished",
            StrategyStatus.Error => "Error",
            _ => "Stopped"
        };
        StatusColor = status switch
        {
            StrategyStatus.Running => "#50FA7B",
            StrategyStatus.Finished => "#F1FA8C",
            StrategyStatus.Error => "#FF5555",
            _ => "#6272A4"
        };
        if (status != StrategyStatus.Running) _strategy = null;
        CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshPositions(List<Position> calls, List<Position> puts)
    {
        Positions.Clear();
        foreach (var p in calls)
            Positions.Add(new PositionRow { Side = "CALL", Instrument = p.InstrumentName, Size = p.Size.ToString("+0.##;-0.##"), Delta = p.Delta.ToString("F4") });
        foreach (var p in puts)
            Positions.Add(new PositionRow { Side = "PUT", Instrument = p.InstrumentName, Size = p.Size.ToString("+0.##;-0.##"), Delta = p.Delta.ToString("F4") });
    }

    private static void Dispatch(Action action)
    {
        if (Application.Current?.Dispatcher?.CheckAccess() == true)
            action();
        else
            Application.Current?.Dispatcher?.Invoke(action);
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
