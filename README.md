# LoupixDeck

LoupixDeck is a Linux application for controlling the **Loupedeck Live S**, offering a customizable interface with support for button input, rotary encoders, and more.

## ⚠️ Note

⚠️ **This software is in an early and experimental state, primarily built for personal use.**  
⚠️ **Many features are still under development or not yet implemented.**

## Implemented Features

The following features are currently available:

- **Custom rendering** of touchscreen buttons:
  - Display images, background colors, and text
- **Input handling** for:
  - All button presses (touchscreen and physical)
  - Rotary encoder rotation and presses
- **Haptic feedback**:
  - Trigger vibration on touchscreen button press
- **System interaction**:
  - Execute custom shell commands on button press
  - Exceute Kexboard Macros
- **Device settings**:
  - Adjust display brightness
  - Set individual RGB lighting for the four physical buttons
- **UI flexibility**:
  - Multi-page support for touchscreen and rotary inputs
  - Command selection menu (currently available only on touchscreen buttons)
- **Configuration management**:
  - Save and restore settings between sessions
- **Integration with external tools**:
  - Control **OBS Studio** via WebSocket (e.g., toggle virtual camera)
  - Control **Elgato Key Lights**
- **System notifications**:
  - Show notifications using **DBus**

## Supported Devices

Currently, **only the Loupedeck Live S** is supported.

Future expansion to other Loupedeck models is possible, but not planned at this time.
