# LoupixDeck User Manual

This manual is for people who want to use LoupixDeck, not develop plugins for it. It is based on the current GitHub README, the existing docs, the application source, and the bundled plugin manifests. Because LoupixDeck is moving quickly, small names and details may change between releases.

## Index

1. [What LoupixDeck Is](#what-loupixdeck-is)
2. [Supported Devices](#supported-devices)
3. [Install and Start](#install-and-start)
4. [First Launch](#first-launch)
5. [The Main Window](#the-main-window)
6. [Profiles and Workspaces](#profiles-and-workspaces)
7. [Portable Profiles](#portable-profiles)
8. [Pages](#pages)
9. [Touch Buttons](#touch-buttons)
10. [Button Layers](#button-layers)
11. [Button States](#button-states)
12. [Commands and Command Sequences](#commands-and-command-sequences)
13. [Rotary Controls](#rotary-controls)
14. [Physical Buttons](#physical-buttons)
15. [Macros](#macros)
16. [Dynamic Text](#dynamic-text)
17. [Wallpapers](#wallpapers)
18. [Feedback and Haptics](#feedback-and-haptics)
19. [Screensaver](#screensaver)
20. [Profile Rules](#profile-rules)
21. [Plugins and Integrations](#plugins-and-integrations)
22. [Device Power and Window Commands](#device-power-and-window-commands)
23. [Automation and CLI](#automation-and-cli)
24. [Settings Reference](#settings-reference)
25. [Files and Backup](#files-and-backup)
26. [Troubleshooting](#troubleshooting)
27. [Notes and Limitations](#notes-and-limitations)

## What LoupixDeck Is

LoupixDeck is an open-source control deck app for Loupedeck and Razer Stream Controller devices. It lets you build profiles, workspaces, and pages of touch buttons, assign commands to knobs and physical buttons, create macros, display dynamic values, and connect to apps such as OBS, audio devices, lighting tools, monitoring tools, and Spotify Premium.

You do not need the official Loupedeck application to use LoupixDeck.

## Supported Devices

Current documented support:

| Device | Main controls |
| --- | --- |
| Loupedeck Live | 4 x 3 touch grid, side touch strips, 6 rotary encoders, 8 round buttons |
| Loupedeck Live S | 5 x 3 touch grid, 2 rotary encoders, 8 physical buttons |
| Razer Stream Controller | 4 x 3 touch grid, side panels, 6 rotary encoders, 8 LED buttons |
| Loupedeck CT | Partial support; not all controls are feature-complete yet |

Multiple devices can run at the same time. If more than one device is connected, the main window shows a device selector. Two identical devices are separated by USB serial number when possible, so each keeps its own layout.

## Install and Start

### Windows

Windows users have two download choices:

- `LoupixDeck-Setup-win-x64.exe`: native Windows installer.
- `LoupixDeck-win-x64.zip`: portable ZIP, useful when you do not want an installed app.

For most users, use the installer:

1. Download `LoupixDeck-Setup-win-x64.exe` from the latest GitHub release.
2. Run the installer.
3. Choose the install location.
4. Choose whether to create Start menu and desktop shortcuts.
5. Choose whether LoupixDeck should start with Windows by enabling `Start on system startup`.
6. Choose whether to launch LoupixDeck when setup finishes.

The installer is self-contained, so you do not need to install the .NET runtime separately.

If you prefer the portable build:

1. Download `LoupixDeck-win-x64.zip`.
2. Extract it to a folder you control.
3. Run `LoupixDeck.exe`.

### Updating, Repairing, and Uninstalling on Windows

If LoupixDeck is already installed, the Windows setup detects the existing installation and shows the installed and available versions. You can update in place while preserving your settings, or repair a damaged installation.

The installer can register LoupixDeck to start with Windows using the `Start on system startup` checkbox. In v1.12.1 and later, you can also change this after installation from `Settings > General > Start with Windows` inside LoupixDeck.

LoupixDeck also registers an uninstaller in Windows `Installed apps`. Uninstalling removes the program, shortcuts, and registry entries. Your configuration and plugins are kept by default, so reinstalling later should preserve your layouts and integrations.

### Linux

Use the installer from the README:

```bash
curl -fsSL https://raw.githubusercontent.com/RadiatorTwo/LoupixDeck/master/install-loupixdeck.sh | bash
```

The installer installs the app, creates udev rules, and adds a desktop entry. After install, launch it from your app menu or run:

```bash
loupixdeck
```

## First Launch

On start, LoupixDeck auto-detects supported USB devices. If a device is connected, the main window opens with a visual layout matching the device.

If the app cannot talk to the device:

- Check that the device is plugged in directly or through a reliable hub.
- On Linux, make sure the udev rules were installed and reconnect the device.
- Open `Settings > General` and try `Reconnect`.

## The Main Window

The main window is a live editor for your connected device.

- Single-click a touch button, rotary control, or physical button to select it.
- Double-click the selected control to edit its image, text, symbol layers, states, and actions.
- Use page arrows near the device controls to move between pages.
- Use `+` and delete controls to add or remove pages.
- Edit the page name directly in the page name field.
- Use the hamburger menu for `Settings`, `Macros`, `About`, and `Quit`.

The top header shows your current context. It contains `DEVICE`, `PROFILE`, and `WORKSPACE` selectors. If only one device is connected, the device selector is hidden and you will usually just see the profile and workspace selectors. The hamburger menu sits at the right end of this header.

Changing profile or workspace from the header changes what the device shows and what the editor is editing. The selectors also update when a rule, command, or device button switches context for you.

## Profiles and Workspaces

LoupixDeck organises layouts as:

`Device > Profile > Workspace > Pages`

A profile is a larger context, such as an app, activity, or setup. A workspace lives inside a profile and contains its own touch pages and rotary pages. This means one profile can hold several complete page sets without mixing them into one flat list.

For example, you might create:

| Profile | Workspaces |
| --- | --- |
| Streaming | Setup, Live, Moderation |
| Editing | Timeline, Color, Export |
| General | Home, Audio, System |

Open `Settings > Profiles` to manage this structure. From there you can:

- Add, rename, and delete profiles.
- Add, rename, and delete workspaces inside each profile.
- Activate a profile. It opens on that profile's Home workspace.
- Open a workspace so the device shows it and the editor edits its pages.
- Set a workspace as the profile's Home workspace.
- Set the Default profile that opens when LoupixDeck starts.

Old layouts are migrated automatically. If you had pages before profiles and workspaces existed, they are placed in a `Default` profile with a `Home` workspace, so the app should behave as it did before.

Profiles and workspaces can also be changed from commands. The command picker has a `Profiles` group with commands such as `Activate Profile`, `Go to Workspace`, `Next Workspace`, `Previous Workspace`, and `Go to Home Workspace`. In the picker, profile and workspace choices are shown by their real names.

## Portable Profiles

You can move layouts between machines or devices with a `.loupixprofile` package. Open `Settings > Profiles` and use the `Export` button beside a profile, workspace, or individual touch/rotary page. The package includes the selected layout, its images, and the macros referenced by its buttons, so the receiving machine does not need those assets prepared in advance.

To use a package on another machine, choose `Import Package…` in `Settings > Profiles` and select the `.loupixprofile` file. LoupixDeck shows a preview before changing anything. Review:

- Whether the package came from a different device type or model.
- Which plugins the layout needs and whether they are installed or enabled for this device.
- Commands that cannot currently be resolved because their plugin is missing. These assignments are kept and can start working after the plugin is installed.
- Macro name conflicts. For each conflict, keep the local macro (`Skip`), import the incoming macro under a new name (`Rename`), or replace the local macro (`Replace`).

You can import as a copy, which adds a new profile or inserts a workspace/page into a chosen destination, or choose replace to overwrite an existing matching item. Replace creates an automatic backup first; that backup can be restored through the same import dialog. An installed plugin is never enabled silently, but the preview can offer to enable an installed, disabled plugin for the current device.

The package is deliberately layout-focused. Enabled plugins, Profile Rules, app bindings, and the screensaver clip are device-wide settings and do not travel with the package. Check those settings separately on the receiving machine.

## Pages

Pages belong to the active workspace. When you switch workspace, LoupixDeck switches to that workspace's own touch and rotary pages.

Inside a workspace, LoupixDeck uses separate page sets for touch buttons and rotary controls.

Touch pages contain the touch grid layout. Rotary pages contain knob actions. Some devices, such as the Razer Stream Controller, can have independent left and right rotary pages.

From the main window you can quickly move between pages in the current workspace. From `Settings > Pages`, you can:

- Add touch pages.
- Add rotary pages.
- Remove pages, as long as at least one remains.
- Move pages up or down.
- Edit a touch page wallpaper.
- Edit page-level commands.

`Settings > General` also lets you choose the startup touch page used when the current workspace opens.

Page changes normally slide horizontally when triggered from the on-screen page buttons or page commands. In `Settings > General > Page switching`, turn off `Animate touch page transitions` or `Animate rotary page transitions` if you prefer instant page changes. LoupixDeck also falls back to an instant change when an animation is not possible, such as when the device is off, inside folders, on single-page sets, or on hardware without side displays.

## Touch Buttons

Single-click a touch button to select it. Double-click it to open the touch button editor.

Common button options:

- `Run while device is off`: lets the button still run while the device display is blanked by LoupixDeck.
- `Vibration enabled`: enables feedback for that button.
- `Pattern`: chooses the haptic/vibration pattern.
- Layers: controls what the button looks like.
- States: lets a button change between multiple visual/action states.
- Command sequence: controls what the button does when pressed.

Changes are saved when you close the editor.

### Editing and Rearranging Buttons

In v1.14.0 and later, the main window supports faster editing for buttons and side displays:

- Select a button with one click.
- Open it for editing with a double-click.
- Use `Ctrl+C`, `Ctrl+X`, and `Ctrl+V` to copy, cut, and paste the selected button.
- Right-click a button to open a context menu with `Copy`, `Cut`, `Paste`, and `Clear`.
- Drag a configured button onto another button of the same kind. Dropping onto an empty slot moves it. Dropping onto a filled slot swaps the two buttons.
- Hold `Ctrl` while dragging to copy instead of move or swap.
- Press `Esc` while dragging to cancel the drag.

The drop-target ring previews the action before you release the mouse: green means move, amber means swap, blue means copy, and red means the drop is not valid. If a paste or `Ctrl`-drag copy would overwrite a configured target, LoupixDeck asks for confirmation first.

## Button Layers

Touch buttons are built from layers. A button can contain image, text, symbol, and plugin-rendered layers.

Typical layer workflow:

1. Open a touch button.
2. Add or select a layer.
3. Use the preview to position and resize it.
4. Use the properties panel to edit color, text, size, rotation, opacity, outline, shadow, and other options.
5. Reorder layers so background images sit below text and symbols.

The editor has a live preview. Layers are useful because you can build a button from reusable pieces instead of flattening everything into one image.

### Animated image layers

An image layer can also show an animation. This is a property of a normal image layer, not a separate layer type: choose an animated source for an image layer and the button plays it in a loop.

- GIF and animated WebP sources are stored as-is.
- Video files (MP4, MOV, and similar) are converted once, at import time, into a small button-size looping GIF. This import step needs `ffmpeg` on your system `PATH`.
- Animated image/video content can also be used on side displays on devices that have them.
- Video import keeps the source aspect ratio. On square touch buttons, video is letterboxed inside the button. On tall side strips, video is scaled to the strip height at full width, so it stays centred instead of being squashed.
- Playback itself does not need `ffmpeg`. Frames are decoded once and cached, so animated buttons stay light at runtime.

Text and symbol layers can still sit on top of an animated image layer.

## Button States

Touch buttons can have multiple states. This is useful for toggles, mode buttons, and buttons that should visually change after being pressed.

In the touch button editor you can:

- Add a state.
- Duplicate a state.
- Move states up or down.
- Set the default state.
- Delete a state.
- Give each state its own layers and command sequence.

The `Transition after press` setting controls what happens when you press the button:

- Stay in the current state.
- Move to the next/previous state.
- Jump to a specific state.
- Return to the default state.

The transition is applied immediately at press time, using the state that was active for that press. The command sequence then continues running separately. This means a long macro, a macro with a delay, or an endless repeat loop does not keep the old visual state on screen until the command finishes. It also prevents a late transition from undoing a newer press. For example, a start/stop macro pair now changes state and works in two presses, even when one macro keeps running.

## Commands and Command Sequences

Most controls use command sequences. A sequence is a row of one or more commands that run in order.

To assign commands:

1. Open a touch button, physical button, rotary control, or page command editor.
2. Select the command strip you want to edit.
3. Open the command picker.
4. Choose a category card, or press `Ctrl+K` and search across all available commands.
5. Double-click a command to append it, press `Enter` on the selected command, or drag it into the strip.
6. Use the edit icon on a command chip to set parameters.
7. Drag chips to reorder them.
8. Use remove or clear to delete commands.

The command picker groups commands into `Core`, `Macros`, and `Plugins`. Each category card has an icon, description, and command count. Plugin groups can expand inline, so nested commands such as audio devices, OBS scenes, and Elgato lights can be opened without leaving the picker. Search results include their group path, which helps when different plugins expose similarly named commands.

Built-in command groups include:

| Group | Examples |
| --- | --- |
| Pages | Next/previous touch page, next/previous rotary page, go to page number, left/right rotary page commands on devices with side displays |
| Profiles | Activate profile, go to workspace, next/previous workspace, go to Home workspace |
| Macros | Type text, key combination, run a named macro, stop macros |
| Shell | Run a shell command |
| Button Control | Update a touch button at runtime, remove a named layer |
| Device Control | Device off/on/toggle/wakeup, toggle main window |
| Dynamic Text | Clock |
| User Macros | One entry per macro you create |
| Plugin groups | Commands supplied by installed plugins |

Some commands have parameters, such as a page number, key combination, date/time format, shell command, or target button index. Parameter fields appear in the command chip editor, opened with the pencil icon on the command chip.

In v1.17.0 and later, commands can provide their own default settings. When you add such a command, its settings popup is already filled with sensible values. You can still change those values for that one button, knob direction, physical button, or page command. Existing assignments are left as they were.

## Rotary Controls

Single-click a rotary control to select it. Double-click it to open its editor.

Each rotary control can have separate command sequences for:

- Rotate left.
- Rotate right.
- Button press.

### Command groups

Some plugins offer command groups that configure a whole rotary in one step. In the rotary editor's command picker, a group entry is marked with a violet `Group` badge and a tooltip.

- Double-click the group to fill all three rotary slots at once: counter-clockwise maps to rotate left, clockwise to rotate right, and the click maps to press.
- Dragging the group onto the strips does the same. Dropping it anywhere applies the whole mapping, while a plain command drops into a single slot.
- Slots that the group does not define are left untouched, and every command can still be reassigned individually afterwards.

For devices with side strips, each knob can also have a strip label. On the Razer Stream Controller, side strips can show segmented knob labels or be edited as free-draw strip canvases depending on mode.

Devices with separate side displays also offer left and right variants of the rotary page commands. Use the normal `Next Rotary Page`, `Previous Rotary Page`, or `Go to Rotary Page` commands when you want both rotary columns to move together. Use the left/right variants when you want to page only one side.

Use rotary controls for repeated actions such as volume up/down, scene switching, light brightness, timeline navigation, zooming, or page changes.

For device brightness, use `Brightness Up` and `Brightness Down` from the `Device Control` group. A common setup is to put `Brightness Down` on rotate left and `Brightness Up` on rotate right. Each command has a `Step` setting, pre-filled with `5`, which controls how much brightness changes per rotary click. Brightness is clamped to the 0 to 100 range.

## Physical Buttons

Physical buttons are edited similarly to touch buttons, but without the touch-screen layer editor. Double-click a physical button in the main window, then assign one or more commands.

Good uses include:

- Global navigation.
- Stop macros.
- Toggle device display.
- Switch pages.
- Trigger OBS, audio, or lighting commands.

## Macros

Open the macro editor from the hamburger menu with `Macros`.

Macros are reusable sequences. After creating a macro, it appears in the command picker under user macros and can be assigned to buttons, knobs, or page actions.

### Macro basics

In the macro editor you can:

- Add and remove macros.
- Rename macros.
- Choose what happens when a macro is triggered while already running.
- Add, edit, reorder, duplicate, copy, paste, and delete steps.
- Test a macro after a short countdown.
- Record real key presses where recording is supported.
- Set a global stop hotkey.

### Execution modes

The editor describes these modes:

| Mode | Meaning |
| --- | --- |
| Run Once | Ignore new triggers while the macro is already running |
| Restart On Trigger | Cancel the current run and start again |
| Allow Parallel | Let multiple copies run at the same time |

### Macro steps

Available step types include:

| Step | What it does |
| --- | --- |
| Type Text | Types text |
| Key Combination | Sends combinations such as `Ctrl+Shift+Esc` |
| Key Down / Key Up | Holds or releases a key |
| Delay | Waits for a number of milliseconds |
| Mouse | Clicks, presses, releases, moves, or scrolls |
| Command | Runs a LoupixDeck command |
| Set Variable | Stores a value for later use |
| If / Else / End If | Runs steps conditionally |
| Wait For | Waits until a condition becomes true, including until the triggering button is released |
| Prompt | Asks for a value while the macro runs |
| Repeat Start / Repeat End | Repeats a block of steps |

Prompts and variables allow more flexible macros. For example, a macro can ask for a name, store it, and then type text that includes that value.

### Holding a key while the button is held

The **Wait For** step offers the condition **Trigger button released**, which pauses the macro until you let go of the button or touch that started it:

1. **Key Down** with the key you want to hold, for example `Ctrl`.
2. **Wait For** with condition **Trigger button released** and timeout `0`.
3. **Key Up** with the same key.

The key is now held for exactly as long as you hold the button. To repeat something while the button is held, put the steps in an infinite **Repeat** block and add a **Wait For** on **Trigger button released** with **Negate** enabled, a short timeout, and **On timeout: Fail**. Releasing the button ends the macro, and any key it still holds is released automatically.

This condition needs a real button press. When a macro is started another way, for example with the Test button in the editor, from a hotkey, or by a plugin, it counts as released and the wait continues immediately. A press whose release never arrives, for instance because the device was unplugged, counts as released after 30 seconds so no key can stay stuck.
### Key names

Key Combination, Key Down and Key Up take key names joined with `+`, for example `Ctrl+Shift+Esc`. The easiest way to fill them in is the **Capture** button next to the field: press the keys and LoupixDeck writes them down for you.

A key can be named in two ways:

- By the character it types, such as `Ü`, `Ä`, `ß`, `#` or `+` — so `Ctrl+Ü` and `Ctrl++` work. These are resolved through the keyboard layout that is active when the macro runs, which means the macro follows the layout rather than a fixed position.
- By its position on the keyboard, using the US legend: `Semicolon`, `LeftBracket`, `Minus`, `Oem102`, `Num5`, `PrintScreen`, `F13`. These always hit the same physical key, whatever the layout. Capture uses them for keys that type no character, such as the dead keys `^` and `´`.

Both spellings can be mixed in one combination.

The **ⓘ** button next to Capture opens a list of every key name that works on your system, grouped by kind and with the alternative spellings that mean the same key. The list stays open while you keep editing the macro, its text can be selected and copied, and **Copy all** puts the whole list on the clipboard.

Media, volume, browser and launcher keys have names of their own: `PlayPause`, `NextTrack`, `PrevTrack`, `MediaStop`, `Mute`, `VolumeUp`, `VolumeDown`, `BrowserBack`, `BrowserForward`, `BrowserHome`, `LaunchMail`, `LaunchCalculator` and friends. On Windows these keys usually cannot be recorded with Capture, because the system reports them to applications in a different way than ordinary keys — type the name into the field instead. Note that `Pause` is the Pause/Break key; the media key is `PlayPause`.

### Macro input backends

On Windows, normal macros use `SendInput`. LoupixDeck can optionally use the Interception driver for applications that read raw input.

On Linux, macro execution uses `uinput`; recording may need access to `/dev/input/event*`.

## Dynamic Text

Dynamic text commands provide text that updates automatically. The built-in clock command updates once per second.

A typical use is:

1. Add or select a text layer on a touch button.
2. Assign a dynamic text command such as `Clock`.
3. Set the format parameter if needed.

The default clock format is `HH:mm:ss`. You can use .NET date/time format strings such as `HH:mm`, `yyyy-MM-dd`, or `ddd HH:mm`.

Plugins can add more dynamic text providers, for example now-playing information, sensor values, or app state.

## Wallpapers

Touch pages can have wallpapers. Go to `Settings > Pages`, then use the wallpaper edit button for the page.

Wallpapers are separate from button layers. They are best for page-wide context, such as a color theme, app logo, or background image. Button layers still sit on top.

## Feedback and Haptics

Open `Settings > Feedback` to configure touch feedback and haptics. Touch buttons also have per-button vibration controls.

The Feedback page has:

- Touch flash: optional visual flash on touch-button press, with color and opacity controls.
- Haptic: an enable toggle and one global effect picker.

In current releases, haptics use LoupixDeck's software vibration pulse. Older config files still load, but the old delay, duration, second-step, and firmware haptic controls are no longer part of the settings page.

## Screensaver

Open `Settings > Screensaver`.

The screensaver can play a video or GIF across the whole device display after the device is idle. It stops when the device receives input.

Options include:

- Enable animated screensaver.
- Select video.
- Clear selected video.
- Idle timeout in seconds.
- FPS limit.
- Loop continuously.

The screensaver needs `ffmpeg` on your system `PATH`. If LoupixDeck cannot find it, the settings page shows a warning.

## Profile Rules

Open `Settings > Profile Rules`.

Profile Rules change context automatically when applications start or become active. They replace the older App Switching page.

You can:

- Enable or disable automatic profile and workspace switching.
- Add rules.
- Match by process name.
- Optionally match by part of the window title.
- Choose a profile.
- Choose a workspace.
- Optionally choose a page.
- Give each rule a priority.
- Trigger a rule when its process starts, not only when its window comes to the front.
- Choose what happens to the profile when you leave matched apps.

The highest-priority matching rule wins. If rules have the same priority, list order breaks the tie.

If a rule leaves `Profile` empty, it keeps the current profile. If it leaves `Workspace` empty, it opens the chosen profile's Home workspace. What happens when no rule matches is set by `When leaving matched apps`:

| Option | Behavior |
| --- | --- |
| Keep the current profile | The profile is not changed. Whatever the last matching rule activated stays active until another rule matches. This is the default. |
| Return to the profile active before the rule | LoupixDeck goes back to the profile that was active before a rule first took over. |
| Switch to a fixed fallback profile | LoupixDeck always activates the profile you pick in `Fallback profile`. |

The workspace and page are not restored separately; they follow the profile that gets activated.

When you switch profile or workspace by hand, LoupixDeck pauses automatic switching until the foreground app changes. This prevents a rule from immediately pulling you back while you are deliberately working somewhere else.

Process matching is case-insensitive, and a trailing `.exe` is ignored. On Linux, Profile Rules require X11 or XWayland plus `xprop`; pure Wayland is not supported by the current README.

## Plugins and Integrations

Open `Settings > Plugins`.

From this page you can:

- Install a plugin from a zip file.
- Remove a plugin.
- Open the plugins folder.
- Select a plugin and edit its settings if it provides a settings UI.
- Enable or disable plugins live where supported.

The current binary installation includes these plugin manifests:

| Plugin | Platform |
| --- | --- |
| OBS Studio | All |
| Elgato Key Lights | All |
| Audio | All |
| SpotifyPremium | All |
| CoolerControl | All |
| Argus Monitor | Windows |
| HWiNFO | Windows |
| LibreHardwareMonitor | Windows |

Plugins can add commands, dynamic text, settings pages, folders, side-strip providers, or special integration behavior. The exact command names depend on the installed plugin version and what external app or service is configured.

Plugins can also provide default values for command settings. In current bundled plugins, some Audio, Elgato, and Spotify commands use this for editable step sizes, so a rotary can move volume, light brightness, or a Spotify value faster or slower without special syntax.

### Monitoring Plugins

Argus Monitor, HWiNFO, and LibreHardwareMonitor can show sensor readings on touch buttons when the matching plugin is installed and enabled. LibreHardwareMonitor is bundled with LoupixDeck starting in v1.13.1.

In current releases, sensor commands render as monitoring tiles instead of plain text. A tile can show:

- Sensor name or header.
- Current value.
- Gauge bar.

You can put several sensor readings on one touch button by chaining sensor commands with `&&`. LoupixDeck draws one row per sensor, up to four rows. If the button has only one sensor command, it uses a larger single-value tile layout.

Some monitoring plugins also offer a transparent background option. When enabled, the tile panel is removed so the page wallpaper shows through; text is outlined to stay readable.

### Plugin display takeover

Some plugins can temporarily render animated content directly on the device, including full-display video or other continuously changing scenes. While a plugin owns the whole display, a button press ends the stream. Switching profile or workspace also ends it. Turning the device off pauses the stream; turning it on again resumes it.

Plugins can also claim only selected surfaces. For example, a plugin may take over the touch grid while leaving rotary controls, physical buttons, or side displays available for their normal assignments. The exact surfaces depend on the plugin. Starting a plugin takeover stops a running screensaver, and the screensaver does not start while a takeover is active.

Monitoring plugin settings are device-specific. If you enable a plugin on one connected device, its commands and menu entries do not automatically appear on another device. Saved buttons still load, and re-enabling the plugin for that device restores the real command chips.

LibreHardwareMonitor setup has one extra requirement: LibreHardwareMonitor itself must be running in the background. In LibreHardwareMonitor, enable its HTTP web server in the settings. Current LibreHardwareMonitor versions use this HTTP server for external access; the LoupixDeck plugin does not need one exact LibreHardwareMonitor build, but it expects a version newer than v0.8.5. If you enable authentication for the HTTP server, enter the same username and password in the plugin settings. If authentication is off, leave those fields empty.

## Device Power and Window Commands

Device control commands can be assigned to buttons or run from automation:

| Command | Use |
| --- | --- |
| Device OFF | Blank display and LEDs |
| Device ON | Restore the configured page |
| Device Toggle | Toggle between off and on |
| Device Wakeup | Reconnect serial and restore display |
| Toggle Main Window | Show or hide the LoupixDeck window |
| Brightness Up / Brightness Down | Raise or lower display brightness by an editable step |

These are useful for a sleep/wake button, a clean desk mode, or a button that brings the editor back when hidden.

## Automation and CLI

While LoupixDeck is running, you can run the binary again to send commands to the existing instance.

Examples:

```powershell
.\LoupixDeck.exe nextpage
.\LoupixDeck.exe page 3
.\LoupixDeck.exe updatebutton 6 text=Build_OK backColor=LimeGreen
.\LoupixDeck.exe removelayer 6 MyLayer
```

On Linux:

```bash
./LoupixDeck nextpage
./LoupixDeck page 3
./LoupixDeck updatebutton 6 text=Build_OK backColor=LimeGreen
./LoupixDeck removelayer 6 MyLayer
```

For multiple devices, target a specific device:

```bash
./LoupixDeck --device A1B2C3 page 3
./LoupixDeck -d "Loupedeck Live S" nextpage
```

### Available CLI verbs

| Verb | Effect |
| --- | --- |
| `nextpage` / `previouspage` | Move to the next or previous touch page |
| `page <N>` | Go to touch page number `N` |
| `nextrotarypage` / `previousrotarypage` | Move to the next or previous rotary page |
| `rotarypage <N>` | Go to rotary page number `N` |
| `System.NextRotaryPageLeft` / `System.PreviousRotaryPageLeft` | Move the left rotary side to the next or previous page, on devices with side displays |
| `System.NextRotaryPageRight` / `System.PreviousRotaryPageRight` | Move the right rotary side to the next or previous page, on devices with side displays |
| `System.GotoRotaryPageLeft(<N>)` / `System.GotoRotaryPageRight(<N>)` | Go to rotary page number `N` on only one side, on devices with side displays |
| `System.NextWorkspace` / `System.PreviousWorkspace` | Move to the next or previous workspace in the active profile |
| `System.GoHomeWorkspace` | Return to the active profile's Home workspace |
| `System.ActivateProfile(<profileId>)` / `System.GotoWorkspace(<workspaceId>)` | Advanced: activate a profile or workspace by internal id |
| `updatebutton <index> key=value ...` | Update a touch button at runtime |
| `removelayer <index> <layerName>` | Remove a named layer from a button |
| `off` / `on` / `toggle-device` | Blank, restore, or toggle the device display |
| `wakeup` | Reconnect the serial link and restore the display |
| `System.BrightnessUp(<Step>)` / `System.BrightnessDown(<Step>)` | Raise or lower display brightness by `Step`; if omitted or invalid, the command uses `5` |
| `show` / `hide` / `toggle` | Show, hide, or toggle the LoupixDeck window |
| `quit` | Quit the running instance |

Any full `System.*` command string is also accepted and forwarded as-is, so you can trigger built-in and plugin commands by name (for example `System.ObsStartRecord`).

Runtime button updates are not saved to the layout. That is intentional, so scripts can update button text, colors, and images frequently without rewriting the config.

Useful runtime update properties:

| Property | Example |
| --- | --- |
| `text` | `text=Build_OK` |
| `textColor` | `textColor=White` or `textColor=#FFFFFFFF` |
| `backColor` | `backColor=LimeGreen` |
| `image` | `image=C:\path\icon.png` |
| `layer` | `layer=StatusText` |

Underscores in `text` are treated as spaces in the short CLI form.

## Settings Reference

### General

- Device name and connection state.
- Port and baudrate.
- Firmware and serial.
- Reconnect.
- Brightness.
- Dithering.
- Startup touch page.
- Start with Windows (Windows only).
- Close button behavior: minimize to tray or quit.
- Start minimized to tray.
- Page switching: show the page name overlay, animate rotary page transitions, and animate touch page transitions.

On Windows, `Start with Windows` controls whether LoupixDeck launches at login. The installer can set the same behavior during setup with `Start on system startup`, but v1.12.1 and later let you turn it on or off from this settings page. Use it together with `Start minimized to tray` if you want LoupixDeck to launch quietly after login.

`Dithering` is next to the brightness control. It is off by default. Turning it on smooths gradients and can reduce colour banding on the device displays, especially on the Razer Stream Controller. The setting repaints the current display immediately when changed.

### Profiles

- Add, rename, and delete profiles.
- Add, rename, and delete workspaces inside profiles.
- Activate a profile or workspace.
- Set a profile's Home workspace.
- Set the Default profile used at startup.

### Pages

- Manage touch pages for the active workspace.
- Manage rotary pages for the active workspace.
- Reorder pages.
- Edit wallpapers.
- Edit page commands.

### Feedback

- Configure touch flash color and opacity.
- Enable haptic feedback and choose the global haptic effect.

### Screensaver

- Enable animated screensaver.
- Select video/GIF source.
- Set idle timeout, FPS limit, and looping.

### Plugins

- Install, remove, open folder, and configure plugins.

### Macro Driver

Windows only. Shows Interception driver status and lets you install, uninstall, or enable use of the driver for keyboard and mouse macros.

Important: Interception is third-party software. It is not bundled with LoupixDeck, and commercial use may require a separate license from its author.

### Profile Rules

- Enable automatic profile and workspace switching.
- Add rules based on process name and optional title text.
- Choose profile, workspace, and optional page behavior.
- Set rule priority.
- Trigger rules when a process starts.
- Choose what happens to the profile when you leave matched apps.

### Theme

- Dark.
- Light.
- System.

Some controls may keep the old palette until the next app launch.

### About

- Version.
- Project website link.

## Files and Backup

LoupixDeck stores configuration as JSON in the user config directory. Typical files include:

| File | Purpose |
| --- | --- |
| `config.json` | Global settings |
| `config_<device>.json` | Per-device layout and device settings |
| `macros.json` | Shared macro definitions |
| Plugin config files | Integration-specific settings |

Per-device layout is scoped by serial number when possible. The per-device layout file contains that device's profiles, workspaces, pages, and device-specific settings. If a config file is corrupted, LoupixDeck creates a backup before writing a fresh file.

## Troubleshooting

### Device is not detected

- Unplug and reconnect the device.
- Try `Settings > General > Reconnect`.
- On Linux, check that udev rules were installed and reconnect the device.
- Avoid unreliable USB hubs.

### Macros do not affect the target app

- On Windows, try running without Interception first.
- If the target app reads raw input, consider the optional Interception driver.
- Some games or protected applications may block synthetic input.
- On Linux, check `uinput` and input group permissions.

### Macro recording does not work on Linux

Recording needs read access to `/dev/input/event*`. The installer attempts to handle this through group permissions, but you may need to log out and back in after group changes.

### Screensaver does not play

- Install `ffmpeg`.
- Make sure `ffmpeg` is on `PATH`.
- Try a lower FPS limit.
- Test with a simple local video file.

### Profile Rules do not work on Linux

- It needs X11 or XWayland.
- Install `xprop`.
- Pure Wayland is not currently supported according to the README.

### Wrong profile or workspace keeps opening

- Open `Settings > Profile Rules` and check whether automatic switching is enabled.
- Check the priority of matching rules. The highest-priority match wins.
- Check the `When leaving matched apps` setting; it decides whether the profile stays, returns to the previous one, or switches to a fixed fallback profile.
- If you just switched profile or workspace manually, automatic switching waits until the foreground app changes before taking over again.

### Plugin commands are missing

- Open `Settings > Plugins`.
- Check whether the plugin is installed and enabled.
- If you use more than one device, enable the plugin on the device where you want to use it.
- Restart LoupixDeck if the plugin page says some changes need a restart.
- Confirm that external services such as OBS, Spotify, or monitoring tools are running and configured.
- For LibreHardwareMonitor, confirm that LibreHardwareMonitor is running in the background and that its HTTP web server is enabled. If the web server uses authentication, check the username and password in the plugin settings.

### Monitoring tiles look wrong or still use old settings

Sensor plugins changed from plain text output to tile rendering. If an old Argus, HWiNFO, or LibreHardwareMonitor button looks wrong after updating, remove the old sensor command or plugin-specific layer/settings from the button and add the sensor command again.

### Collecting crash logs

If LoupixDeck closes unexpectedly, start it with a diagnostics flag to record what happened:

```bash
./LoupixDeck --crashlog
```

On Windows:

```powershell
.\LoupixDeck.exe --crashlog
```

- `--crashlog` writes unhandled errors to a `crash.log` file in the LoupixDeck user config directory.
- `--firstchance` additionally logs every internal error as it is thrown. This is very noisy and is only meant for deep debugging. It implies `--crashlog`.

These flags cover managed errors. Native crashes are not captured this way; for those, the standard .NET minidump environment variables can be used instead. Attaching `crash.log` to a bug report helps a lot.

## Notes and Limitations

- The project is actively developed, so this manual may lag behind new features.
- Loupedeck CT support is currently partial.
- Plugin command lists can differ by installed version.
- Shell commands and macros are powerful. Only assign commands you trust.
- Runtime CLI updates are temporary and reset when the page is redrawn or the app restarts.
