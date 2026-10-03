<p align="center">
  <img src="inFaux.ico" alt="inFaux Logo" width="128" height="128" />
</p>

<h1 align="center">inFaux</h1>

<p align="center">
  <em>The Modern, Native Hardware Monitor &amp; Open Telemetry Server for Windows</em><br>
  <em>Real telemetry. Faux drivers. Colloquially known as "info".</em><br>
  <em>Crafted with care by FoxDen Software</em>
</p>

<p align="center">
  <a href="https://github.com/TalviFox/inFaux"><img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue.svg" alt="Platform" /></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-10.0%20(Self--Contained)-purple.svg" alt="Runtime" /></a>
  <a href="https://github.com/TalviFox/inFaux"><img src="https://img.shields.io/badge/RAM-~115%20MB-brightgreen.svg" alt="RAM Footprint" /></a>
  <a href="https://github.com/TalviFox/inFaux"><img src="https://img.shields.io/badge/Background%20Processes-0-success.svg" alt="Background Processes" /></a>
  <a href="https://github.com/TalviFox/inFaux"><img src="https://img.shields.io/badge/Kernel%20Drivers-0%20(100%25%20Driverless)-success.svg" alt="Driverless" /></a>
  <a href="https://github.com/TalviFox/inFaux"><img src="https://img.shields.io/badge/Elevation-Standard%20User%20(asInvoker)-success.svg" alt="Privilege" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-PolyForm%20Noncommercial-blue.svg" alt="License" /></a>
</p>

> *"Microsoft basically went 'we can't agree what temp is correct, so you can have none without kernel drivers' unless you do this complicated-ass math."*

---

## 💡 Why inFaux?

For the last 15 years, the PC hardware monitoring world has been split into two frustrating extremes:

1. **The 1998 C++ Grognards (HWiNFO, CPU-Z, Core Temp):**
   * *The Problem:* Dense, intimidating walls of 600 raw register lines with Windows XP-era interfaces. They rely on ancient, vulnerable kernel drivers (`WinRing0.sys`) that trip Windows 11's *Vulnerable Driver Blocklist*, and their only advice is *"go disable Windows Defender Core Isolation."*
2. **The 800 MB Corporate Bloatware (Armoury Crate, NZXT CAM, Corsair iCUE):**
   * *The Problem:* Disguised web apps built on Electron or Chromium Embedded Framework. You download an app just to check your CPU temp and end up with **15 zombie background processes, 600+ MB of RAM consumed, mandatory account logins, telemetry trackers, and an RGB store**.

### The inFaux Golden Middle
**inFaux** ("info") bridges the void:
* **Single-Process Executable (`inFaux.exe`):** Zero installers required, zero zombie background helpers, zero telemetry phoning home. Runs strictly as a standard non-elevated user (`asInvoker`) with **zero UAC prompts**. (It even displays a gentle notice if you accidentally launch it elevated!)
* **100% Driverless Architecture:** Zero third-party hardware monitor libraries, zero kernel drivers, and zero vulnerability warnings. Fully compatible with Windows 11 Core Isolation, Memory Integrity (HVCI), and Defender.
* **Human-First Curated Dashboard:** Clean, responsive, dark-mode dashboard with instant component drilldowns, a live all-sensors table, and deep thermal calibration controls.
* **Embedded Open API:** A local REST and WebSocket server out-of-the-box on `http://127.0.0.1:8765` for Stream Deck, Rainmeter, Home Assistant, or custom status displays.

---

## 🚀 Key Features

### 1. Curated Multi-View Dashboard & Component Drilldowns
* **Primary Dashboard:** Glanceable metric cards with live 60-second sparklines for:
  * **Processor (CPU):** Package temperature, total load %, dynamic wattage, and boost clock speed.
  * **Graphics (GPU):** Multi-GPU selector pills, core clock, temperature, hotspot, VRAM usage, power, and fan speed.
  * **System Memory (RAM):** Memory load %, used/total GB, available GB, and memory speed (MT/s).
  * **Storage Volumes:** Live aggregate read/write throughput (MB/s), total capacity, and volume utilization.
  * **Network & Power:** Real-time download/upload throughput (Mbps), active adapter interface, and battery state (automatically adapts column layout on battery-equipped systems).
* **Deep Component Drilldowns:** Click any card on the dashboard to access dedicated telemetry views:
  * **CPU View:** Per-core breakdown across all physical cores and threads with live per-core frequencies (MHz), load percentages, and dynamic thermal flux modeling.
  * **GPU View:** Multi-adapter switcher, vendor-native hardware telemetry (NVML / ADL / DXGI), core/memory clocks, VRAM allocation, dynamic power wattage, and fan RPM.
  * **Storage View:** Volume capacities, real-time read/write throughput rates (MB/s via PDH), physical drive geometry, and volume utilization (bus type NVMe/SATA and media type SSD/HDD).
  * **Network & Power View:** Real-time throughput rates (Mbps), adapter switcher (`Auto` highest traffic or specific adapter), link speed, battery charge/discharge wattage, AC line state, and estimated runtime minutes.
* **All Sensors View:** Complete live DataGrid table of all raw hardware sensors and channels with min/max tracking, category, type, and current values (rendered via non-destructive in-place sync to eliminate visual flickering).
* **Settings & Tuning View:** Interactive controls for polling intervals, tray icon sensor targets, network adapter binding, chassis form factor profiles, cooler profiles, live thermal calibration offset slider (-15°C to +15°C), CPU TDP override targets, and 1-click API testing.

---

### 2. The Thermodynamic Observer & Safe Physics Modeling
* **Why this exists:** Microsoft Windows does not allow regular user-mode applications to read raw CPU thermal diodes without installing Ring-0 kernel drivers (`.sys`). Legacy tools rely on vulnerable drivers (like `WinRing0.sys`) that trip Windows Defender and trigger Memory Integrity (HVCI) warnings.
* **The inFaux Solution:**
  Instead of risking your system stability with kernel drivers, inFaux uses real Newtonian physics:
  * **Real Hardware Baselines:** Continuously reads safe, user-mode diode temperatures from your GPU and NVMe SSD controllers to establish the true thermal floor of your PC chassis.
  * **Copper Heat Spreader Simulation:** Treats your CPU's integrated heat spreader (IHS) and cooler cold plate like a real thermal mass. Solid copper heats up quickly when a core spikes under heavy load, and dissipates heat gradually into the cooler fins.
  * **Per-Core Temperature Gradients:** Models localized heat blooms across individual cores and clusters based on live clock frequencies and workloads from native Windows performance counters.
  * **Zero Elevation, Zero Lag:** Runs 100% in user space (`asInvoker`) with zero UAC prompts, zero background services, and zero fan-hunting jitter.
  * **Honest Telemetry:** Real physical diode readings (GPU, NVMe, and Bluetooth beacons) are clearly marked with `[📡 Measured]`, while thermodynamic CPU numbers are transparently tagged `[Estimated]`.

---

### 3. Wireless Bluetooth Environmental Telemetry (BTHome v2)
* **Passive Windows BLE Listener:** inFaux passively listens for Bluetooth Low Energy (BLE) environmental beacons without needing pairing, dongles, USB bridges, or third-party background software (yes, Windows can do this).
* **Plug & Play Hardware Support:** Supports all open **BTHome v2** format thermometers, including:
  * **Shelly BLU H&T** (native, official out-of-the-box BTHome v2)
  * ThermoBeacon / Qingping beacons
  * Custom ESP32 / ESPHome / Arduino temperature beacons
  * Xiaomi / Tuya thermometers (flashed with open BTHome v2 firmware)
* **Multi-Zone Monitoring:**
  * **Room Ambient:** Monitor your actual office or room temperature and humidity.
  * **In-Case Air:** Place a thermometer inside your case to measure real internal ambient air.
  * **Radiator Exhaust:** Measure liquid cooler exhaust air to compute radiator delta-T ($\Delta T$) efficiency.
* **Upgrades Estimations to Measured Data:** When a sensor is detected, inFaux automatically upgrades its estimated room/case calculations to 100% real measured physical readings (`[📡 Measured]`).

---

### 4. Intelligent Multi-GPU & APU Support
* **Multi-GPU Architecture:** Automatically enumerates and tracks all graphics adapters simultaneously (discrete GPUs and integrated APUs).
* **Vendor-Native User-Mode Probes:**
  * **NVIDIA NVML (`nvml.dll`):** Direct interop with official NVIDIA Management Library in `System32` for core/memory clocks, power wattage, fan speed %, VRAM, and diode / hotspot temps.
  * **AMD ADL (`atiadlxx.dll`):** Direct interop with AMD Display Library in `System32` for Overdrive temperatures and power.
  * **DirectX DXGI COM Interop (`IDXGIFactory1`, `IDXGIAdapter3`):** Zero-driver adapter enumeration, dedicated video memory, and shared system memory tracking.
* **Discrete Prioritization:** Automatically selects high-performance discrete GPUs when present for primary telemetry and tray badge.
* **APU & iGPU Intelligence:** Dynamically attributes SoC shared die temperatures and allocates load-weighted power slices (e.g., AMD Radeon 780M / 680M / Intel Iris Xe) without false sensor warnings or phantom fan RPMs.

---

### 5. Embedded Read-Only Open API (Port 8765)
Built-in local HTTP + WebSocket server powered by ASP.NET Core Kestrel on `http://127.0.0.1:8765`:
* `GET /` — Root overview with API manifest and version.
* `GET /api/v1/summary` — Clean JSON snapshot of curated system telemetry.
* `GET /api/v1/sensors` — Complete flat list of all hardware probes with min/max tracking.
* `GET /api/v1/sensors/{category}` — Category-filtered sensors (`cpu`, `gpu`, `memory`, `storage`, `network`).
* `GET /api/v1/history/{metricId}` — 60-second rolling ring buffer for sparklines (`cpu_temp`, `gpu_temp`, `ram_load`, etc.).
* `GET /api/v1/health` — Service health check and uptime status.
* `WS  /api/v1/stream` — Low-latency WebSocket push stream for live dashboards, Stream Decks, and Discord bots.

---

### 6. Stream Deck Companion Suite v1.1.0 (Built-In LCM)
* **Real-Time Physical Hardware Gauges:** Dynamic canvas-rendered gauges for **CPU**, **GPU**, **RAM**, **Network**, and **BT Sensor** with live wattage, thermal color grading, usage percentages, and progress bars.
* **Universal Key Renaming:** Rename the top header on ANY key directly from the Property Inspector (e.g. `GAMING`, `3080TI`, `OFFICE`, `ROOM`) without random text stomping in the middle of your button.
* **Interactive Quick Tap:**
  * **GPU / Net:** Tap the key to instantly cycle through multiple GPUs or network adapters.
  * **BT Sensor:** Tap the key to toggle between live Temperature and Humidity on the fly!
* **Coat Color Modes:** Choose between clean pure **Monochrome** (white on dark), **Theme Accent** (syncs with your Fox Coat theme), or **Reactive** (dynamic green/amber/red thermal alerts).
* **Zero-Config Lifecycle Management (LCM):** 
  * Built straight into the inFaux **Settings** tab.
  * **1-Click Install:** Drops the plugin directly into `%APPDATA%\Elgato\StreamDeck\Plugins\` with zero web searches, zero manual file copying, and zero marketplace accounts.
  * **1-Click Repair / Update & Clean Uninstall:** One-click management with version auditing.
* Also distributed as a standalone, double-click installable package: [`com.foxden.infaux.streamDeckPlugin`](integrations/streamdeck/).

---

### 7. Dynamic Numeric System Tray Icon
* Draws live, readable temperature numbers (e.g. `42°` or `68°`) directly onto your Windows system tray icon using GDI+ pixel fonts.
* **Customizable Target:** Choose between CPU Package Temp, Primary GPU Temp, or Highest (CPU/GPU) auto-tracking.
* **Intelligent Thermal Color-Coding:**
  * 🟢 **Cyan (< 65°C):** Cool & nominal.
  * 🟡 **Amber (65–79°C):** Moderate gaming / compiling workload.
  * 🔴 **Red (≥ 80°C):** Thermal throttling / heavy stress.
* Automatically recovers if `explorer.exe` restarts.

---

### 8. The Fox Coat Theme Engine
Customize the appearance of inFaux and your Stream Deck to match your setup:
* **Silver Fox (Stealth Platinum):** Sleek modern slate dark mode.
* **Silver Fox OLED (Midnight):** True deep-black `#000000` background optimized for OLED displays.
* **Red Fox (Classic Ember):** Vibrant fox-orange accents inspired by classic hardware tuning suites.
* **Arctic Fox (Glacial Cyan - Dark):** Cool ice-blue cyber aesthetic.
* **Arctic Fox Snow (High-Contrast Light):** Clean, crisp light mode for high-ambient lighting conditions.
* **Fennec Fox (Desert Sand & Amber):** Warm golden-sand aesthetic.

---

### 9. Zero-UAC Lifecycle & Companion Scripts
* **Standard User Autostart:** Registers with Windows Task Scheduler using standard user permissions—starts on boot with **0 UAC prompts**.
* **Zero-Elevation Installer (`install.ps1`):** Installs cleanly into `%LOCALAPPDATA%\Programs\inFaux`, sets up shortcuts, registers in Windows *Installed Apps* (Add or Remove Programs), and validates SHA-256 hashes.
* **Cryptographic Auditor (`verify.ps1`):** Automatically downloads and verifies your local `inFaux.exe` against official GitHub Release SHA-256 checksums.
* **Clean Uninstaller (`uninstall.ps1`):** Complete 1-click system removal of scheduled tasks, shortcuts, registry entries, and program files.

---

## 🔌 API Quick Reference

### Curated Snapshot (`GET /api/v1/summary`)
```json
{
  "timestamp": "2026-09-26T20:20:00Z",
  "cpu": {
    "name": "AMD Ryzen 7 PRO 7840U w/ Radeon 780M Graphics",
    "tempC": 39.5,
    "loadPercent": 14.8,
    "powerWatts": 18.2,
    "clockGhz": 3.30,
    "coreCount": 8,
    "cores": [
      { "name": "Core 0", "tempC": 39.2, "clockMhz": 3294, "loadPercent": 12.0 },
      { "name": "Core 1", "tempC": 40.1, "clockMhz": 3410, "loadPercent": 18.5 }
    ]
  },
  "gpu": {
    "name": "AMD Radeon 780M Graphics",
    "isDiscrete": false,
    "vendor": "AMD",
    "tempC": 39.5,
    "hotspotTempC": null,
    "loadPercent": 0.0,
    "powerWatts": 1.8,
    "vramUsedGb": 0.35,
    "vramTotalGb": 3.91,
    "fanRpm": null
  },
  "memory": {
    "usedGb": 14.3,
    "totalGb": 28.0,
    "percent": 51.0,
    "availableGb": 13.7,
    "speedMts": 6400
  },
  "storage": [
    {
      "name": "Micron 2400 MTFDKBA1T0QFM",
      "driveLetter": "C:",
      "tempC": 36.0,
      "healthPercent": 100.0,
      "healthStatus": "Healthy (OK)",
      "busType": "NVMe",
      "mediaType": "Solid State Drive (SSD)",
      "usedGb": 412.5,
      "totalGb": 953.8,
      "readSpeedMBps": 1.2,
      "writeSpeedMBps": 0.4
    }
  ],
  "network": {
    "adapterName": "Wi-Fi",
    "adapterDescription": "Intel(R) Wi-Fi 6E AX210 160MHz",
    "downloadMbps": 4.82,
    "uploadMbps": 0.65
  },
  "chassis": {
    "chassisAirTempC": 36.8,
    "chassisAirTempF": 98.2,
    "estimatedAmbientTempC": 22.4,
    "estimatedAmbientTempF": 72.3,
    "thermalResistanceCPerW": 0.35,
    "confidencePercent": 85
  }
}
```

---

## ⚡ Quick Install

Run in a standard **(non-elevated)** PowerShell window:
```powershell
irm https://raw.githubusercontent.com/TalviFox/inFaux/main/install.ps1 | iex
```
> [!NOTE]
> **No Administrator Privileges Required:** inFaux is 100% zero-driver and designed to run safely with standard user permissions (`asInvoker`). Do not run the installer as Administrator!

*Or grab `inFaux.exe` directly from the [Releases](https://github.com/TalviFox/inFaux/releases) page for zero-install portable use.*

---

## 🛠️ Building & Releasing

### Prerequisites
* Windows 10 (Build 19041+) or Windows 11
* [.NET 10.0 SDK](https://dotnet.microsoft.com/)

### Build from Source
```powershell
# Debug build
dotnet build -c Debug

# Release single-file build
dotnet publish InFox.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

### Automated Release Builder
Run the automated FoxDen release builder:
```powershell
.\release.ps1 -Version "1.0.0" -SkipGit
```

The script will:
1. Compile a self-contained, single-file executable for `win-x64`.
2. Generate `publish\inFaux.exe`.
3. Compute the cryptographic SHA-256 checksum and output `publish\SHA256SUMS.txt` and `publish\inFaux.exe.sha256`.
4. Generate release notes ready for GitHub Releases.

### Verify Cryptographic Integrity
Run the built-in auditor to verify your installed binary against official GitHub release hashes:
```powershell
.\verify.ps1
```

---

## ⚙️ Configuration Reference

Settings are stored in `%LOCALAPPDATA%\inFaux\config.json`:

| Setting | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `apiPort` | `int` | `8765` | Port for the embedded Kestrel REST and WebSocket API |
| `enableApi` | `bool` | `true` | Enables or disables the embedded HTTP/WebSocket server |
| `apiBindLocalhostOnly` | `bool` | `true` | When true, binds only to `127.0.0.1` (safe user-mode loopback) |
| `pollingIntervalMs` | `int` | `1000` | Telemetry harvest interval in milliseconds (250ms–2000ms) |
| `traySensorTarget` | `string` | `"cpu_temp"` | Tray badge metric: `"cpu_temp"`, `"gpu_temp"`, or `"auto_max_temp"` |
| `preferredNetworkAdapter` | `string` | `"Auto"` | Interface to monitor: `"Auto"` (highest traffic) or specific adapter |
| `chassisProfile` | `string` | `"Auto"` | Enclosure profile for air cavity modeling: `"Auto"`, `"Laptop"`, `"Desktop"` |
| `coolerProfile` | `string` | `"Auto"` | Dissipation scaling: `"Auto"`, `"AIO"`, `"TowerAir"`, `"Compact"` |
| `cpuThermalOffset` | `double` | `0.0` | Live calibration offset in °C (-15.0 to +15.0) |
| `cpuTdpOverrideWatts` | `int` | `0` | Package TDP target for Joule heating (0 = auto-detect) |
| `trayDisplayBadge` | `bool` | `true` | Toggle drawing the live numeric temperature badge on the tray icon |
| `startWithWindows` | `bool` | `true` | Start on user logon via Windows Task Scheduler (0 UAC prompts) |
| `checkForUpdates` | `bool` | `true` | Automatic GitHub release auditor check |

---

## 🤖 Transparency & AI Disclosure

inFaux is developed with the assistance of AI coding tools. In the spirit of open development and personal accountability: **I don't post what I don't run.**

Every feature, script, and build is actively dogfooded, tested, and run on my own daily-driver machines before it is published here.

---

## 🦊 FoxDen Software Suite

* **[WireFox](https://wirefox.foxdensoftware.dev/)** — Intelligent WireGuard Roaming Companion for Windows
* **[inFaux](https://github.com/TalviFox/inFaux)** — Modern Native Hardware Monitor & Telemetry Server

---

*Copyright © FoxDen Software. Distributed under the PolyForm Noncommercial License 1.0.0.*  
*Elgato and Stream Deck are registered trademarks of Corsair Memory, Inc. inFaux is an independent open-source project and is not affiliated with, endorsed by, or sponsored by Corsair or Elgato.*
