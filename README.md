# LoupixDeck

[![.NET Release](https://github.com/RadiatorTwo/LoupixDeck/actions/workflows/release.yml/badge.svg)](https://github.com/RadiatorTwo/LoupixDeck/actions/workflows/release.yml)
[![Platform](https://img.shields.io/badge/platform-linux-blue)](https://github.com/RadiatorTwo/LoupixDeck)
[![Platform](https://img.shields.io/badge/platform-windows-blue)](https://github.com/RadiatorTwo/LoupixDeck)

**LoupixDeck** is a Linux application for controlling the **Loupedeck Live S**.  
It provides a highly customizable interface to assign commands, control external tools, and build dynamic layouts using touchscreen and rotary inputs.

---

## ⚠️ Disclaimer

> ⚠️ **This software is in an early, experimental stage.**
> ⚠️ **Many features are under active development or not fully implemented.**

---

## ✨ Features

- Custom touchscreen button rendering:
  - Images, background colors, text
  - Tiled wallpaper rendered across all touchscreen buttons
- Full input support:
  - Touch input, rotary encoder turns/clicks, and physical button presses
  - Haptic feedback (vibration) when touching buttons
- Dynamic page system:
  - Independent pages for touch and rotary buttons
  - Temporary on-screen display of the active page index
- Flexible command actions:
  - Execute shell commands on button press
  - Send keyboard macros
- Device configuration:
  - Adjust display brightness
  - Set individual RGB colors for each physical button
- Persistent configuration:
  - Save and restore full device layout and settings
- Command selection menu:
  - Assign commands directly with parameters
- External tool integration:
  - Control **OBS Studio** via WebSocket (e.g., toggle virtual camera)
  - Manage **Elgato Key Lights** (power, brightness, color temperature)
- System interaction:
  - Show system notifications via **D-Bus**

---

## 🖥️ Supported Devices

- ✅ **Loupedeck Live S**

> Support for additional Loupedeck models may be considered in the future, but is not currently planned.

## 🛠️ Build Instructions

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

### Windows

```bash
git clone https://github.com/RadiatorTwo/LoupixDeck.git
cd LoupixDeck
dotnet publish LoupixDeck.csproj -c Release -r win-x64 --self-contained true `
            /p:PublishSingleFile=true `
            /p:PublishTrimmed=false `
            /p:EnableCompressionInSingleFile=true `
            /p:ReadyToRun=true `
            -o publish/win-x64
```
