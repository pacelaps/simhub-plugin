# PaceLaps SimHub plugin

A SimHub plugin that brings PaceLaps race control calls into SimHub: on your overlay and dashboards, on your wheel's
and button box's LEDs, and as sounds. It only shows what race control sends to your car and holds no race logic.

## Status

Proof of concept. Built: the core library (connection, clock sync, show-at timing, reconnect and resync) with tests,
and the SimHub plugin (properties, events, tones, the settings page). The overlay and the LED profile come next.
What the plugin offers in SimHub: `docs/properties.md`.

## How it connects

1. Join your league on the driver page as usual.
2. Copy one of your **overlay URLs** from the driver page and paste it into the plugin's settings.

The plugin then gets exactly what that overlay gets: the calls for your car, your class and everyone, at the same
moment as every other driver's screen. **New overlay URLs** or **Turn off** on the driver page disconnect it.

## Build and test

Needs the .NET SDK 10 (`global.json`).

```
dotnet test tests/PaceLaps.SimHub.Core.Tests
```

builds the core library (`src/PaceLaps.SimHub.Core`, netstandard2.0 so SimHub's .NET Framework 4.8 loads it) and runs
its tests (`tests/PaceLaps.SimHub.Core.Tests`).

The plugin itself (`src/PaceLaps.SimHub`, .NET Framework 4.8) needs SimHub installed: it compiles against SimHub's
DLLs (`SIMHUB_INSTALL_PATH`, else `C:\Program Files (x86)\SimHub\`). To build it and copy it into SimHub (close
SimHub first, it locks the DLLs):

```
dotnet build src/PaceLaps.SimHub -p:DeployToSimHub=true
```

Then start SimHub, enable the PaceLaps plugin when SimHub asks, and open **PaceLaps** in the left menu. The plugin is
two files: `PaceLaps.SimHub.dll` and `PaceLaps.SimHub.Core.dll` (SimHub already has Newtonsoft.Json and NAudio).

### Overlays

Three Dash Studio overlays in PaceLaps' look, all reading the same properties; drivers pick one:

- **PaceLaps Card** (the default): the call on dark glass with a colour bar and a small race control header.
- **PaceLaps Solid**: the call as a banner filled in its colour, like the driver page.
- **PaceLaps Strip**: one slim line for the top of the screen.

They are built from `tools/PaceLaps.Overlays` (the designs in `Designs.cs`) into `overlay/dist/*.simhubdash`:

```
dotnet run --project tools/PaceLaps.Overlays
```

Import a `.simhubdash` in SimHub's Dash Studio. They bundle Inter (`overlay/fonts`, SIL Open Font License). The
build refuses a formula that reads a property the plugin doesn't have.

The message formats are in `contracts/`, a copy of the web service's. `scripts/sync-contracts.ps1` refreshes it from a
web service checkout next to this one; a test fails while the copy is out of date.

## Tested with

SimHub 9.13.3 (2026-11-12).

## Layout

```
src/PaceLaps.SimHub.Core/          connection, clock offset, show-at timing, state (no SimHub reference)
src/PaceLaps.SimHub/               the SimHub plugin: properties, events, tones, settings page
tests/PaceLaps.SimHub.Core.Tests/  unit tests with a fake clock, server and socket, and the overlays' checks
tools/PaceLaps.Overlays/           builds the three Dash Studio overlays
overlay/fonts/                     Inter, bundled with the overlays
contracts/                         the shared message schemas and examples (copied)
docs/simhub-notes.md               what we found out about SimHub
```

## Licence

GPL-3.0 (`LICENSE`).
