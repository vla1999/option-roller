# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This folder contains a subset of **MarketMakerBot**, a C# (.NET) algorithmic cryptocurrency trading bot. The three files here implement the **options rolling strategy** — a component of a larger multi-strategy trading system.

## Build & Run

This directory has no standalone build config — it is part of a parent .NET solution. Standard commands from the solution root:

```bash
dotnet build        # Build the project
dotnet run          # Run the application
dotnet test         # Run tests
dotnet format       # Format code
```

## Architecture

### Orchestrator: `MarketMakerBot.cs`

The central class that manages the full lifecycle of all trading strategies:

- Holds a list of `IStrategy` implementations and starts/stops them
- Routes market data from `PublicDataFeedConnectionsPool` and account events from `PrivateDataFeedConnectionsPool` to active strategies
- Manages exchange connections via `ConnectorsPool` (multi-exchange: Binance, OKX, Deribit)
- Sends alerts via `ITelegramBot` watchdog for strategy errors and status events
- Supports hot-reload and resume of individual strategies without restarting the bot

### Strategies

The full system has 25+ strategies (referenced in `MarketMakerBot.cs`) implementing `IStrategy`:

- **Market making**: `IcebergStrategy`, `AdaptiveStrategy`, `PingPongStrategy`
- **Arbitrage**: `SpreadArbitrageStrategy`, `TriangleArbitrageV2Strategy`, `FundingArbitrageStrategy`, `PerpSpotMakerTakerArbitrageStrategy`
- **Options**: `OptionRollStrategy` (implemented in this folder)
- **HFT**: `HFTStrategy`

### Options Strategy: `OptionRollStrategy.cs` + `OptionRollSettings.cs`

Manages delta-neutral options positions on a 1-second cycle:

1. Refresh current call/put positions
2. Calculate total delta per side
3. If delta imbalance exceeds `Diff` threshold → roll: close old position, open new one targeting `OptionStrategyType` (Roll or Close)
4. Consolidate multi-position legs — keeps only the highest-delta position
5. Watchdog notifications and error-limit enforcement throughout

**Settings** (`OptionRollSettings`): `MasterStock`, `SlaveStock`, `Expiration` (DateTime), `OptionStrategyType`, `Diff` (decimal threshold).

### Data Flow

```
PublicDataFeedConnectionsPool  →  Strategies (market data: tickers, order book, candles)
PrivateDataFeedConnectionsPool →  Strategies (account data: positions, orders, balance)
ConnectorsPool                 →  Strategies (order placement / cancellation)
IDbService                     →  Persistence
ITelegramBot                   →  Monitoring alerts
```

All strategy logic is async/await; multiple strategies run concurrently.
