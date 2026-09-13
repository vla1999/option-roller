namespace OptionRollerUI.Models;

public enum OptionType { Call, Put }

public enum Direction { Buy, Sell }

public enum PositionOrderType { Open, Close }

public enum OptionStrategyType { Roll, Close }

public enum StrategyStatus { Stopped, Running, Finished, Error }
