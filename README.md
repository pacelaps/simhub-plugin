# PaceLaps SimHub plugin

A SimHub plugin that brings PaceLaps race control calls into SimHub: on your overlay and dashboards, on your wheel's
and button box's LEDs, and as sounds. It only shows what race control sends to your car and holds no race logic.

## Status

Proof of concept. Built so far: the core library (connection, clock sync, show-at timing, reconnect and resync), with
tests. The SimHub plugin itself, the overlay and the LED profile come next.

## How it connects

1. Join your league on the driver page as usual.
2. Copy one of your **overlay URLs** from the driver page and paste it into the plugin's settings.

The plugin then gets exactly what that overlay gets: the calls for your car, your class and everyone, at the same
moment as every other driver's screen. **New overlay URLs** or **Turn off** on the driver page disconnect it.

## Build and test

Needs the .NET SDK 10 (`global.json`).

```
dotnet test
```

builds the core library (`src/PaceLaps.SimHub.Core`, netstandard2.0 so SimHub's .NET Framework 4.8 loads it) and runs
its tests (`tests/PaceLaps.SimHub.Core.Tests`).

The message formats are in `contracts/`, a copy of the web service's. `scripts/sync-contracts.ps1` refreshes it from a
web service checkout next to this one; a test fails while the copy is out of date.

## Layout

```
src/PaceLaps.SimHub.Core/          connection, clock offset, show-at timing, state (no SimHub reference)
src/PaceLaps.SimHub/               the SimHub plugin (next)
tests/PaceLaps.SimHub.Core.Tests/  unit tests with a fake clock, server and socket
contracts/                         the shared message schemas and examples (copied)
docs/simhub-notes.md               what we found out about SimHub
```

## Licence

GPL-3.0 (`LICENSE`).
