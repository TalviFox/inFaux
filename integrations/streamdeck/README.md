# inFaux — Official Stream Deck Companion Plugin

> Zero-config, real-time hardware telemetry gauges on your physical Elgato Stream Deck, powered natively by [inFaux](https://github.com/TalviFox/inFaux).

---

## Features

- **CPU Monitor:** Live temperature with dynamic color grading (`<55°C` green, `<72°C` blue, `<82°C` yellow, `>82°C` red), core wattage, usage percentage, and dynamic load bar.
- **GPU Monitor:** Live board wattage, GPU temperature, load percentage, and dynamic load bar.
- **RAM Monitor:** Live GB used out of total installed capacity, memory load percentage, and color-coded memory bar.
- **Network Monitor:** Live real-time bidirectional download and upload throughput.
- **Zero Configuration:** Automatically connects to inFaux's local REST API on `http://127.0.0.1:8765/api/v1/summary`. No URLs or JSON paths to configure—just drag and drop.
- **Offline Resilient:** Shows clean `OFFLINE` status if inFaux is not running, and resumes the instant inFaux starts up.

---

## Installation

### Option 1: Double-Click Install (`.streamDeckPlugin`)
Download `com.foxden.infaux.streamDeckPlugin` from the [Latest Release](https://github.com/TalviFox/inFaux/releases) and double-click it. Elgato Stream Deck will automatically install and register it.

### Option 2: Manual Developer Install
Copy the `com.foxden.infaux.sdPlugin` directory directly into your Elgato plugins folder:

```powershell
Copy-Item -Path "com.foxden.infaux.sdPlugin" -Destination "$env:APPDATA\Elgato\StreamDeck\Plugins\" -Recurse -Force
```

Restart the Elgato Stream Deck application, and you will see **inFaux Telemetry** in the action sidebar.

---

## How to Package the Plugin

A `.streamDeckPlugin` distribution file is a standard ZIP archive of the `com.foxden.infaux.sdPlugin` directory renamed to `.streamDeckPlugin`.

To build it via PowerShell:

```powershell
Compress-Archive -Path "com.foxden.infaux.sdPlugin" -DestinationPath "com.foxden.infaux.streamDeckPlugin" -Force
```
