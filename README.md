# LoupixDeck

[![.NET Release](https://github.com/RadiatorTwo/LoupixDeck/actions/workflows/release.yml/badge.svg)](https://github.com/RadiatorTwo/LoupixDeck/actions/workflows/release.yml)
[![Platform](https://img.shields.io/badge/platform-linux-blue)](https://github.com/RadiatorTwo/LoupixDeck)
[![Platform](https://img.shields.io/badge/platform-windows-blue)](https://github.com/RadiatorTwo/LoupixDeck)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

**LoupixDeck** is an open-source, cross-platform control deck application for **Loupedeck** devices (**Live**, **Live S** and **CT**) and the **Razer Stream Controller**.

> **Note:** Loupedeck **CT** support is still a work in progress and not yet feature-complete.

It lets you build custom pages with touch buttons, rotary controls, physical buttons, macros, integrations and plugins — without depending on the official vendor software.

Built with **Avalonia** and **.NET 9**.

![LoupixDeck main window](docs/screenshots/main-window-loupedeck.png)

---

## Highlights

* **Linux and Windows support**
* **Loupedeck Live**, **Live S**, **CT** (partial) and **Razer Stream Controller** support
* **Multi-device support** with serial-scoped configuration
* **Layer-based touch button editor** with images, text, symbols and wallpapers
* **Rotary encoder pages** with separate actions for rotation, click and press
* **Visual macro editor** for keyboard, mouse, delay and command sequences
* **Native haptic feedback** on supported touch buttons
* **OBS Studio**, **Elgato Key Lights**, **Cooler Control**, **Argus Monitor** and **Windows Audio** integrations
* **App-focus page switching**
* **Local CLI / IPC channel** for scripts and automation
* **Plugin SDK** for custom commands, dynamic text providers and settings UI

---

## Supported Devices

| Device                      |          VID:PID | Layout                                                                         |
| --------------------------- | ---------------: | ------------------------------------------------------------------------------ |
| **Loupedeck Live**          |      `2ec2:0004` | 4×3 touch grid, 2 side touch strips, 6 rotary encoders, 8 round buttons         |
| **Loupedeck Live S**        |      `2ec2:0006` | 5×3 touch grid, 2 rotary encoders, 8 physical buttons                          |
| **Razer Stream Controller** |      `1532:0d06` | 4×3 touch grid, 2 side panels, 6 rotary encoders, 8 LED buttons                |
| **Loupedeck CT** *(partial)*| `2ec2:0003/0007` | 4×3 touch grid + round wheel touchscreen, 6 dials + wheel, 8 round + 12 square buttons |

> Loupedeck **CT** is supported but **not yet feature-complete** — some controls/behaviours are still being finished and need hardware verification.

Multiple devices can run in parallel in a single LoupixDeck instance.
Even two identical units are separated by USB serial and keep their own configuration.

---

## Download

Pre-built binaries are available on the GitHub Releases page:

**https://github.com/RadiatorTwo/LoupixDeck/releases/latest**

| Platform    | Asset                         |
| ----------- | ----------------------------- |
| **Windows** | `LoupixDeck-win-x64.zip`      |
| **Linux**   | `LoupixDeck-linux-x64.tar.gz` |

Release builds are self-contained.
The .NET runtime is bundled and does not need to be installed separately.

---

## Installation

### Windows

Download `LoupixDeck-win-x64.zip`, extract it and run:

```powershell
LoupixDeck.exe
```

### Linux

Use the installer script:

```bash
curl -fsSL https://raw.githubusercontent.com/RadiatorTwo/LoupixDeck/master/install-loupixdeck.sh | bash
```

Or with `wget`:

```bash
wget -qO- https://raw.githubusercontent.com/RadiatorTwo/LoupixDeck/master/install-loupixdeck.sh | bash
```

The installer downloads the latest release build, installs LoupixDeck system-wide, adds udev rules and creates a desktop entry.

After installation, launch it with:

```bash
loupixdeck
```

Or start it from your application menu.

To inspect the script first:

```bash
curl -fsSLO https://raw.githubusercontent.com/RadiatorTwo/LoupixDeck/master/install-loupixdeck.sh
less install-loupixdeck.sh
bash install-loupixdeck.sh
```

---

## Features

### Touch Button Editor

* Stack image, text and symbol layers per button
* Live preview with direct layer manipulation
* Per-page wallpaper with opacity control
* Optional visual touch feedback
* Content-addressed asset store for deduplicated images
* Material Design Icons symbol picker

### Rotary Encoders

* Independent rotary pages
* Separate commands for:

  * rotate left / right
  * click
  * press
* Multi-command sequences per action

### Macros

LoupixDeck includes a visual macro editor for reusable macro sequences.

Supported macro steps:

| Step                  | Description                             |
| --------------------- | --------------------------------------- |
| **Text**              | Type a text string                      |
| **Key Combination**   | Send combinations like `Ctrl+Shift+Esc` |
| **Key Down / Key Up** | Hold or release individual keys         |
| **Mouse**             | Click, press, release, move or scroll   |
| **Delay**             | Wait for a configured time              |
| **Command**           | Run another LoupixDeck command          |

Input injection backends:

| Platform    | Backend                      |
| ----------- | ---------------------------- |
| **Linux**   | `uinput`                     |
| **Windows** | `SendInput`                  |
| **Windows** | Optional Interception driver |

### Integrations

LoupixDeck includes built-in commands and dynamic values for:

* **OBS Studio** via obs-websocket
* **Elgato Key Lights** via Zeroconf discovery
* **Cooler Control**
* **Argus Monitor** on Windows
* **Windows Audio** via WASAPI
* Shell commands
* Page navigation
* Device power control
* Runtime button updates

### Native Haptic Feedback

Touch buttons can use native vibration effects on supported devices.

Native haptic support is based on reverse-engineered firmware commands.
Technical notes are available here:

[`docs/NATIVE_HAPTIC.md`](docs/NATIVE_HAPTIC.md)

Huge thanks to [@Athorus](https://github.com/Athorus) for the reverse-engineering work that made this possible.

### App-Focus Page Switching

LoupixDeck can automatically switch pages when the foreground application changes.

Rules can match:

* process name
* optional window title substring
* fallback page

Supported platforms:

| Platform                 | Status                                  |
| ------------------------ | --------------------------------------- |
| **Windows**              | Supported                               |
| **Linux X11 / XWayland** | Supported via `xprop`                   |
| **Pure Wayland**         | Not supported, no common focus protocol |

### Multi-Device Support

LoupixDeck can drive multiple connected devices at the same time.

* Each device gets its own profile
* Identical devices are separated by USB serial
* Devices can be connected or disconnected while LoupixDeck is running
* A device switcher appears when more than one device is connected
* CLI commands can target a specific device

---

## Plugins

LoupixDeck supports third-party plugins.

Plugins can provide:

* custom commands
* dynamic text providers
* settings UI
* integration-specific functionality

The Plugin SDK is maintained in a separate repository:

**https://github.com/RadiatorTwo/LoupixDeck.PluginSdk**

It is also available as the `LoupixDeck.PluginSdk` NuGet package.

---

## Screenshots

| Loupedeck Live S                                                              | Razer Stream Controller                                                          |
| ----------------------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| ![Main window — Loupedeck Live S](docs/screenshots/main-window-loupedeck.png) | ![Main window — Razer Stream Controller](docs/screenshots/main-window-razer.png) |

| Layer Editor                                                          | Symbol Picker                                                              |
| --------------------------------------------------------------------- | -------------------------------------------------------------------------- |
| ![Layer-based touch button editor](docs/screenshots/layer-editor.png) | ![Material Design Icons symbol picker](docs/screenshots/symbol-picker.png) |

| Settings                                                              | Macro Editor                                              |
| --------------------------------------------------------------------- | --------------------------------------------------------- |
| ![Settings sidebar navigation](docs/screenshots/settings-sidebar.png) | ![Visual macro editor](docs/screenshots/macro-editor.png) |

---

## Configuration

LoupixDeck auto-detects supported devices by USB VID/PID.

Configuration is stored as JSON in the user config directory.

Typical files:

| File                   | Purpose                               |
| ---------------------- | ------------------------------------- |
| `config.json`          | Global application settings           |
| `config_<device>.json` | Per-device layout and device settings |
| `obs.json`             | OBS integration settings              |
| `elgato.json`          | Elgato integration settings           |
| `macros.json`          | Shared macro definitions              |

Per-device configuration is scoped by USB serial whenever possible, so two identical devices do not overwrite each other's layouts.

If a configuration file becomes corrupted, LoupixDeck creates a backup before writing a fresh file.

---

## CLI / Automation

While LoupixDeck is running, external scripts can control it through a local IPC channel.

The easiest way is to call the LoupixDeck binary again.
If an instance is already running, the second process forwards the command and exits.

### Linux

```bash
./LoupixDeck nextpage
./LoupixDeck page 3
./LoupixDeck updatebutton 6 text=Build_OK backColor=LimeGreen
./LoupixDeck System.ObsStartRecord
```

### Windows

```powershell
.\LoupixDeck.exe nextpage
.\LoupixDeck.exe page 3
.\LoupixDeck.exe updatebutton 6 text=Build_OK backColor=LimeGreen
```

When multiple devices are connected, a specific device can be targeted:

```bash
./LoupixDeck --device A1B2C3 page 3
./LoupixDeck -d "Loupedeck Live S" nextpage
```

Available IPC endpoints:

| Platform    | Endpoint                                      |
| ----------- | --------------------------------------------- |
| **Linux**   | Unix domain socket `/tmp/loupixdeck_app.sock` |
| **Windows** | Named pipe `LoupixDeck_Pipe`                  |

---

## Build from Source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download).

### Linux

```bash
git clone https://github.com/RadiatorTwo/LoupixDeck.git
cd LoupixDeck

dotnet publish LoupixDeck.csproj -c Release -r linux-x64 --self-contained true \
  /p:PublishSingleFile=true \
  /p:PublishTrimmed=false \
  /p:EnableCompressionInSingleFile=true \
  /p:ReadyToRun=true \
  -o publish/linux-x64
```

On Linux, macro **execution** writes to `/dev/uinput` and macro **recording** reads
`/dev/input/event*`. Both are gated behind the `input` group, so the recommended setup
is a udev rule that grants `uinput` to the `input` group plus membership in that group:

```text
# Grant uinput to the 'input' group (macro execution)
KERNEL=="uinput", SUBSYSTEM=="misc", GROUP="input", MODE="0660", OPTIONS+="static_node=uinput"
```

```bash
sudo usermod -aG input "$USER"   # then log out and back in
```

> The bundled `install-loupixdeck.sh` already writes this uinput rule and adds the
> invoking user to the `input` group. Note: being able to run macros does **not**
> automatically mean recording works — recording additionally needs read access to
> `/dev/input/event*`, which the `input` group provides.

If the **device** itself is not accessible without `sudo`, add a udev rule for its VID/PID.

Example for the Loupedeck Live S:

```text
SUBSYSTEM=="usb", ATTRS{idVendor}=="2ec2", ATTRS{idProduct}=="0006", MODE="0666"
SUBSYSTEM=="tty", ATTRS{idVendor}=="2ec2", ATTRS{idProduct}=="0006", MODE="0666"
```

For the Razer Stream Controller, replace `2ec2:0006` with `1532:0d06`.

Reload rules and reconnect the device:

```bash
sudo udevadm control --reload-rules
sudo udevadm trigger
```

### Windows

```powershell
git clone https://github.com/RadiatorTwo/LoupixDeck.git
cd LoupixDeck

dotnet publish LoupixDeck.csproj -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true `
  /p:PublishTrimmed=false `
  /p:EnableCompressionInSingleFile=true `
  /p:ReadyToRun=true `
  -o publish/win-x64
```

---

## Diagnostics

Managed crash logging can be enabled with:

```bash
./LoupixDeck --crashlog
```

On Windows:

```powershell
.\LoupixDeck.exe --crashlog
```

For very noisy first-chance exception logging:

```bash
./LoupixDeck --firstchance
```

Crash logs are written to the LoupixDeck user config directory.

Native crashes are not captured by `--crashlog`.
For native crashes, use the .NET minidump environment variables instead.

---

## Third-Party Software

### Interception Driver on Windows

The optional Windows macro driver feature can use the [Interception](https://github.com/oblitum/Interception) kernel driver to inject keyboard and mouse input at driver level.

This can be useful for applications that read raw input.

Important notes:

* The Interception driver is **not bundled** with LoupixDeck.
* It is only downloaded when installing it from the settings.
* Interception is free for non-commercial use only.
* Commercial use requires a separate license from its author.
* Without Interception, macros use the standard Windows `SendInput` backend.

---

## Project Status

LoupixDeck is usable, but still actively developed.

Features may change between releases and some areas may still have rough edges.
Bug reports, testing feedback and pull requests are welcome.

---

## License

LoupixDeck is released under the [MIT License](LICENSE).

Third-party components are subject to their own licenses.
