# LoupixDeck User Manual

This manual is for people who want to use LoupixDeck, not develop plugins for it. It is based on the current GitHub README, the existing docs, the application source, and the Plugin Store catalogue and manifests. Because LoupixDeck is moving quickly, small names and details may change between releases.

## Index

1. [What LoupixDeck Is](#what-loupixdeck-is)
2. [Supported Devices](#supported-devices)
3. [Install and Start](#install-and-start)
4. [First Launch](#first-launch)
5. [The Main Window](#the-main-window)
6. [Profiles and Workspaces](#profiles-and-workspaces)
7. [Companion Devices](#companion-devices)
8. [Portable Profiles](#portable-profiles)
9. [Pages](#pages)
10. [Touch Buttons](#touch-buttons)
11. [Button Layers](#button-layers)
12. [Button States](#button-states)
13. [Commands and Command Sequences](#commands-and-command-sequences)
14. [Rotary Controls](#rotary-controls)
15. [Physical Buttons](#physical-buttons)
16. [Macros](#macros)
17. [Dynamic Text](#dynamic-text)
18. [Wallpapers](#wallpapers)
19. [Feedback and Haptics](#feedback-and-haptics)
20. [Screensaver](#screensaver)
21. [Profile Rules](#profile-rules)
22. [Plugins and Integrations](#plugins-and-integrations)
23. [Device Power and Window Commands](#device-power-and-window-commands)
24. [Automation and CLI](#automation-and-cli)
25. [Settings Reference](#settings-reference)
26. [Files and Backup](#files-and-backup)
27. [Troubleshooting](#troubleshooting)
28. [Notes and Limitations](#notes-and-limitations)

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
| Razer Stream Controller X | 5 x 3 physical-key panel; no dials, LED buttons, side strips, or haptic motor |
| Loupedeck CT | Partial support; not all controls are feature-complete yet |

Multiple devices can run at the same time. If more than one device is connected, the main window shows a device selector. Two identical devices are separated reliably by USB serial number—including composite USB devices on Windows—so each keeps its own layout. Existing configuration files keep their names; connecting a second unit gives it a separate file instead of renaming the first unit's file.

The Stream Controller X's physical keys act like touches at the centre of their matching keys. Touch-button actions, including press-and-hold, work normally. Settings and controls for hardware the device does not have are hidden.

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

The installer is self-contained, so you do not need to install the .NET runtime separately. LoupixDeck v1.22.0 and later run on .NET 10 internally; only people building the app from source need the .NET 10 SDK.

If you prefer the portable build:

1. Download `LoupixDeck-win-x64.zip`.
2. Extract it to a folder you control.
3. Run `LoupixDeck.exe`.

### Updating, Repairing, and Uninstalling on Windows

If LoupixDeck is already installed, the Windows setup detects the existing installation and shows the installed and available versions. You can update in place while preserving your settings, or repair a damaged installation.

The updater can install in place even when the installation folder was held open by a shell command. Shell commands now run from your home directory, so programs started by them should not lock the LoupixDeck installation folder. If an update still cannot proceed, the error identifies the program that must be closed. The installer and app use matching SkiaSharp builds; if the app cannot start, Windows now shows the startup error instead of closing silently.

The installer can register LoupixDeck to start with Windows using the `Start on system startup` checkbox. In v1.12.1 and later, you can also change this after installation from `Settings > General > Start with Windows` inside LoupixDeck.

LoupixDeck also registers an uninstaller in Windows `Installed apps`. Uninstalling removes the program, shortcuts, and registry entries. Your configuration and plugins are kept by default, so reinstalling later should preserve your layouts and integrations.

### Linux

Use the installer from the README:

```bash
curl -fsSL https://github.com/RadiatorTwo/LoupixDeck/releases/latest/download/install-loupixdeck.sh | bash
```

The installer installs the app, creates udev rules for every supported device—including the Razer Stream Controller X—and adds a desktop entry. When it runs in a terminal, release downloads show a progress bar. After install, launch it from your app menu or run:

```bash
loupixdeck
```

When upgrading from a pre-v1.28 release, the Linux installer moves previously bundled plugins and their `settings.json` files from the root-owned application directory into `~/.config/LoupixDeck/plugins`. The invoking user owns the migrated copies, so they can be managed by the Plugin Store. If a same-version or newer user copy already exists, it is kept instead.

#### SteamOS

In Desktop Mode, open Konsole and run the same installer command shown above as your normal user, without `sudo`. SteamOS replaces its read-only system image during system updates, so LoupixDeck is installed inside your home folder instead of under `/usr/local`:

- Application: `~/.local/lib/loupixdeck`
- Command-line launcher: `~/.local/bin/loupixdeck`
- Application-menu entry: `~/.local/share/applications/loupixdeck.desktop`

The app files stay owned by your user. Only the device-permission rule requires administrator rights. The installer writes `/etc/udev/rules.d/99-loupixdeck.rules` and adds it to the SteamOS keep list at `/etc/atomic-update.conf.d/loupixdeck.conf`, so a SteamOS update does not remove the device permissions. Other systems that provide the same keep-list directory receive this protection as well. If your SteamOS user does not have a password yet, run `passwd` before the installer so `sudo` can create the rule.

The home-folder installation is recognized by LoupixDeck's in-app updater and updates in the same way as the normal Linux script installation. A Flatpak is not provided because its sandbox would block device access, input simulation, application launching, and many plugins.

To compile and install the current `master` branch instead of downloading a release, first download the script and pass `--from-source`:

```bash
curl -fsSLO https://raw.githubusercontent.com/RadiatorTwo/LoupixDeck/master/install-loupixdeck.sh
bash install-loupixdeck.sh --from-source
```

This mode requires Git and the .NET 10 SDK. It builds LoupixDeck and the Plugin SDK; plugins are installed separately from the in-app Plugin Store. Without `--from-source`, the script retains its normal release-download behavior.

Add `--restart` to close a running LoupixDeck cleanly before replacing its files and start it again afterwards:

```bash
bash install-loupixdeck.sh --restart
```

The in-app Linux updater uses this option automatically. If the update is cancelled or fails while the previous installation is still available, the script starts that version again so LoupixDeck is not left closed.

### Update notifications

LoupixDeck checks GitHub for a newer stable release shortly after it starts. The check runs in the background: no network connection, a timeout or a GitHub rate limit never delays startup or opens an error dialog; the result only goes to the log. Pre-releases and drafts are never offered.

When a new version exists, a short hint appears below the profile and workspace bar. If the window is minimized or in the tray, the operating system shows a notification instead. **Details** opens the update dialog with the release notes of every version between the installed and the latest one:

- **Update now** downloads the installer, checks it against the SHA-256 checksum GitHub publishes for the release file and only then starts it. On Windows the setup wizard opens; it closes LoupixDeck, installs the update and can start it again. On Linux, installs made with `install-loupixdeck.sh`—including the SteamOS home-folder install—run the script for the new version in a terminal window, where it asks for your password, closes LoupixDeck, installs and restarts it. If the update is cancelled or fails before replacement completes, the previous installation is started again when it is still available.
- **Later** closes the dialog and keeps the hint.
- **Skip this version** hides the hint until the next release comes out.

Installations that cannot update themselves (the portable Windows zip, package-manager or source builds) and releases without a matching installer get **Open release page** instead. An update never touches your configuration, macros, dial presets or asset store.

Turn the automatic check off under `Settings > General > Check for updates automatically`. **Check for updates** in the About dialog always works, even with the automatic check off, and also reports a version you skipped.

### Runtime and performance

In v1.22.0 and later, display frames reuse pooled buffers, WebSocket payloads are masked in place, and incoming serial data is parsed through a fixed buffer. Command parsing, lookup tables, and native calls also use lower-allocation paths. These changes are automatic; there is no performance setting to enable, and existing layouts and plugins continue to work.

## First Launch

On start, LoupixDeck opens the main window first and brings supported USB devices online in the background. There is no separate splash screen. A connected device joins the window as soon as its link is ready; only devices whose links are ready appear in the device selector.

When several devices connect during startup, the first one that becomes available stays selected while the others join the selector. A later connection no longer takes the editor away from the device you are using.

If no device is ready, the device area shows `No device connected` instead of empty profile and workspace selectors. Plug a supported deck in over USB and LoupixDeck picks it up automatically. If the deck is already plugged in, the vendor software or another program may still be holding its serial port. LoupixDeck retries a busy port with backoff and adds the device when the port becomes available, without requiring an app restart. Device actions stay disabled in the meantime, while `About` and `Quit` remain available from the hamburger menu. Starting minimized also works when no device is ready yet.

When the computer wakes from sleep or standby, LoupixDeck rebuilds the device connection and sends the current state again. Brightness, LED colours, the active touch page, and side-strip content are restored. The same state refresh happens after automatic reconnect or after you press `Reconnect` in `Settings > General`, so the display should not remain black after the link returns.

This also covers Modern Standby, hibernate, hybrid sleep, and Fast Startup, even when Windows does not report a normal resume event. After a wake, LoupixDeck keeps retrying the rebuilt connection for up to 30 seconds and retries repaint requests that have not reached the hardware. If you turned the device off yourself, it stays off rather than being switched back on by resume recovery.

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
- Open the left apps and commands panel or the right Folders panel from the header.
- Use the hamburger menu for `Settings`, `Plugins`, `Macros`, `About`, and `Quit`. The Plugins window remains available even when no device is connected.

The top header shows your current context. It contains `DEVICE`, `PROFILE`, and `WORKSPACE` selectors. If only one device is connected, the device selector is hidden and you will usually just see the profile and workspace selectors. The hamburger menu sits at the right end of this header.

Changing profile or workspace from the header changes what the device shows and what the editor is editing. The selectors also update when a rule, command, or device button switches context for you.

Next to the `PROFILE` and `WORKSPACE` selectors there is a `⋮` button. It creates a new profile or workspace, renames the one currently selected, or deletes it after asking. A new profile or workspace is opened right away. The last profile of a device and the last workspace of a profile cannot be deleted.

The profile menu can also link an application to the active profile: pick it from the list, and the profile opens whenever that application is in front, as long as automatic switching is turned on. This creates a profile rule, which you can refine under `Settings > Profile Rules`. Remove the link from the same menu.

Switching to a device with a different physical layout resizes the main window to fit it. A maximized window stays maximized. The apps and commands panel keeps its open or closed state across device switches instead of unexpectedly disappearing or reopening.

### Apps and commands panel

When a device is connected, the button at the left of the header opens a side panel with three tabs:

| Tab | What it contains |
| --- | --- |
| Apps | Applications discovered on the computer, plus applications added manually |
| Commands | The same searchable command catalogue used by the button editors |
| Dial presets | Built-in, plugin-provided, and user-created mappings for all three gestures of a rotary control, grouped by source |

Single-click a panel row to select it. To assign it, first select a compatible touch key, physical LED button, or dial and then double-click the row, or drag the row directly onto the control. An invalid target is outlined in red and is not changed. If the target already has content, LoupixDeck asks before replacing it. Command-picker groups stay expanded when a plugin finishes loading its menu, and the tighter rows keep more entries visible.

An application assigned to a touch key receives its launch command and, when available, an extracted app icon sized to leave room for a caption. App icons are cached at full resolution and refreshed when their source changes. A catalogue command receives its glyph and name. Physical LED buttons receive the command, while a dial receives the command and a side-strip label. Side displays themselves are not drop targets. Some catalogue entries are dial-only because they configure all three rotary gestures together.

Assigning **Shell Command** or **Open Website** from the panel first asks for the command line or the web address; cancelling leaves the control unchanged. A touch key is labelled with the program name or the site, for example `notepad` or `youtube.com`. Open Website only opens `http://` and `https://` addresses; an address without a scheme is opened as `https://`.

Right-click an application in the panel to open the active profile whenever that application is in front, or to stop doing so. This is the same link as **Link application…** in the profile menu. An application whose process cannot be detected, such as a Flatpak or a program started through `Update.exe`, shows a disabled entry instead.

On Windows, the Apps tab scans Start Menu shortcuts, Steam libraries—including libraries on other drives—and Epic Games installations. Steam and Epic entries launch through their own launcher. On Linux, it scans XDG desktop entries, including Flatpak and Snap exports. Use the `+` button beside the app search field to add a portable program, script, or shortcut that discovery missed; hand-added entries remain after rescans and restarts. Use the refresh button to scan again.

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

Open `Settings > Profiles` to manage this structure, or use the `⋮` menus in the header for the quick cases (new, rename, delete). From Settings you can:

- Add, rename, and delete profiles.
- Add, rename, and delete workspaces inside each profile.
- Activate a profile. It opens on that profile's Home workspace.
- Open a workspace so the device shows it and the editor edits its pages.
- Set a workspace as the profile's Home workspace.
- Set the Default profile that opens when LoupixDeck starts.

Old layouts are migrated automatically. If you had pages before profiles and workspaces existed, they are placed in a `Default` profile with a `Home` workspace, so the app should behave as it did before.

Profiles and workspaces can also be changed from commands. The command picker has a `Profiles` group with commands such as `Activate Profile`, `Go to Workspace`, `Next Workspace`, `Previous Workspace`, and `Go to Home Workspace`. In the picker, profile and workspace choices are shown by their real names.

## Companion Devices

A companion group lets several connected decks share context without forcing them to show the same layout. Open `Settings > Companions`, create a group, choose one master, and add one or more companions. A device can belong to only one group. The page also shows which members are connected; identical devices that cannot report a usable serial number cannot be placed in a group when LoupixDeck cannot tell them apart.

The master owns the group's profiles, workspaces, and custom-folder tree. A companion follows the master's profile and workspace changes—whether they come from the header, commands, or Profile Rules—but keeps its own touch pages, rotary pages, folder layouts, and round LED-button assignments inside that shared structure. Its original profiles are put aside while it belongs to the group and return when it leaves.

On a companion, profile and workspace selectors are locked while it follows the master. Profiles, workspaces, Profile Rules, and the folder tree are read-only, while pages and the layouts inside folders remain editable. If the master is offline, the companion stays on its current context until the master returns.

Each group offers two optional navigation controls:

- **Follow pages**: `Off` lets every companion page independently; `Touch pages` follows the master's touch-page number when that position exists; `Touch and rotary pages` follows both touch and rotary positions. A companion can still page on its own between master page changes.
- **Follow into folders**: when the master opens or closes a custom folder, companions in the same workspace open or close the corresponding folder using their own layouts.

The `Companions` command group is available on a master. It can move a particular companion to its next, previous, or named touch/rotary page; show every companion's start pages; change page-follow mode; pause or resume the group; and resync it. A pause lets companions switch profile and workspace independently until the group is resumed or LoupixDeck restarts. Resync rebuilds their shared structure from the master and applies its current context again.

A master's Profile Rules can also select touch and rotary pages for individual companions inside the rule's target workspace. See [Profile Rules](#profile-rules).

Deleting a profile, workspace, or folder on the master also removes the matching companion content below it. When that would discard configured companion pages, LED buttons, or folder layouts, the confirmation lists the affected companions and content before anything is removed.

## Portable Profiles

You can move layouts between machines or devices with a `.loupixprofile` package. Open `Settings > Profiles` and use the `Export` button beside a profile or workspace, or export an individual touch/rotary page from `Settings > Pages`. The profile and workspace menus in the main-window header provide export actions too.

Every export opens a dialog where you can add a description and edit or browse for the output file. The dialog suggests a file name and remembers the folder used by the last successful export. The package includes the selected layout, its custom folders, images, and referenced macros, so the receiving machine does not need those assets prepared in advance. A whole-profile export also includes that profile's round LED-button commands and colours.

To use a package on another machine, choose `Import Package…` in `Settings > Profiles` or in either main-window header menu, then select the `.loupixprofile` file. LoupixDeck shows the package description and a preview before changing anything. Review:

- Whether the package came from a different device type or model.
- Which plugins the layout needs and whether they are installed or enabled for this device.
- Commands that cannot currently be resolved because their plugin is missing. These assignments are kept and can start working after the plugin is installed.
- Macro name conflicts. For each conflict, keep the local macro (`Skip`), import the incoming macro under a new name (`Rename`), or replace the local macro (`Replace`).
- Companion pages included by a master. When this device leads a group, choose which local companion receives each part; otherwise only the package's main-device content is imported.

You can import as a copy, which adds a new profile or inserts a workspace/page into a chosen destination, or choose replace to overwrite an existing matching item. Replace creates an automatic backup first; that backup can be restored through the same import dialog. Replacing a profile keeps the target profile and matching workspace identities, so existing Profile Rules, macros, and companion pages remain linked wherever the imported profile still has the corresponding workspace. A warning appears if unmatched workspaces would remove companion content. An installed plugin is never enabled silently, but the preview can offer to enable an installed, disabled plugin for the current device.

The package is deliberately layout-focused. Enabled plugins, Profile Rules, app bindings, and the screensaver clip are device-wide settings and do not travel with the package. Check those settings separately on the receiving machine.

Profiles exported by v1.25.0 and later carry their LED-button row. When an older package has no such row, LoupixDeck creates the device's normal default LED buttons during import.

When a master exports a whole profile or workspace, the export dialog offers **Include the companions' own pages** if its companions have content there. This adds their touch/rotary pages, folder layouts, and—for a profile—their LED buttons. Folder structure is already part of the master's profile or workspace and is always included.

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

`Cycle pages` is enabled by default in the same settings card. When enabled, next from the last page wraps to the first and previous from the first wraps to the last. Turn it off to make both touch-page and rotary-page navigation stop at their respective ends. This applies to page commands as well as the controls in the main window.

Pages have stable internal ids. Existing configuration files are migrated automatically when they are first loaded, including Profile Rule page targets that can be resolved. The visible layout behaves as before, while later page reordering no longer changes which page a saved reference means.

### Custom folders

Custom folders belong to the active workspace and can be nested without a fixed depth limit. Open the right-side **Folders** panel in the main window to manage them:

- Create a folder or a subfolder from the panel.
- Click a folder to open and edit it; double-click its name to rename it.
- Drag folders within the tree to reorder them or place them inside another folder.
- Use the search box to filter the tree.
- Drag a folder onto a touch key to assign a ready-made button that opens it.

Every folder owns a touch layout that uses the current device's real key grid. Its bottom-left tile is an automatic Back button and cannot be edited. Breadcrumbs above the device show the open path and let you return to an earlier folder. The panel can also show the normal page again without deleting or unassigning the folder.

Deleting a folder warns about nested folders, buttons that link to it, and affected companion layouts before clearing those links. On a companion, the master owns the folder tree, so its structure is read-only; the companion's own layout inside each folder remains editable.

Use the built-in commands `Folder Back` to move up one level and `Close All Folders` to return directly to the normal page. `Go to Home Workspace` closes open folders too.

### Plugin folders

Some plugin commands open a temporary folder directly on the device, for example an audio mixer, an OBS scene picker, or a monitoring dashboard. The folder uses the active device's real key layout rather than assuming a 5×3 grid. The bottom-left key is reserved for Back; on 4×3 devices, entries stay within the twelve centre keys and do not overwrite the side strips.

Opening a different profile or workspace closes the current folder, including nested folders, and draws the selected layout. Leaving through the Back key instead returns to the previous folder level or the normal page.

## Touch Buttons

Single-click a touch button to select it. Double-click it to open the touch button editor.

Common button options:

- `Run while device is off`: lets the button still run while the device display is blanked by LoupixDeck.
- `Vibration enabled`: enables feedback for that button.
- `Pattern`: chooses the haptic/vibration pattern.
- `Background`: draws the selected state's background color below its layers. Turn it off to leave the key bare, so the page wallpaper shows through.
- Layers: controls what the button looks like.
- States: lets a button change between multiple visual/action states.
- Command sequence: controls what the button does when pressed.

Changes are saved when you close the editor.

### Button editor windows

The touch-button, rotary, and physical-button editors can be resized. Drag a window edge or corner if you need more room for the preview, command sequence, or settings. Each editor has a minimum size so its controls remain usable.

The command picker is wider in these editors. In the Touch Button editor, the behavior area has its own scroll bar, so a long command sequence scrolls inside that panel without pushing the main preview out of view. The color swatch remains visible beside its editor, even in tight layouts. Command rows and section headers keep a gutter beside the scroll bar, while the selected-row highlight still spans the full row. The Rotary and physical-button editors use the same clean picker layout without the old gray outer frame.

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

Plugin-rendered layers normally belong to the command that created them and are removed by unbinding that command. If an old render leaves behind a plugin layer whose command is no longer bound to the button, you can select and delete that leftover layer in the editor.

## Button States

Touch buttons can have multiple states. State-capable physical/LED buttons use the same model, with a per-state LED color instead of touch layers. This is useful for toggles, mode buttons, and buttons that should visually change after being pressed.

In the touch button editor you can:

- Add a state.
- Duplicate a state.
- Move states up or down.
- Set the default state.
- Delete a state.
- Give each state its own layers and command sequence.

Each state also has its own background color and `Background` checkbox. When enabled, the color covers the page wallpaper on that key. When disabled, the wallpaper shows through; if the page has no wallpaper, the key is shown as black in both the app and on the device. Existing configurations are migrated automatically: a deliberately chosen color stays enabled, while an untouched default background stays visually unchanged.

The `Transition after press` setting controls what happens when you press the button:

- Stay in the current state.
- Move to the next/previous state.
- Jump to a specific state.
- Return to the default state.

The transition is applied immediately at press time, using the state that was active for that press. The command sequence then continues running separately. This means a long macro, a macro with a delay, or an endless repeat loop does not keep the old visual state on screen until the command finishes. It also prevents a late transition from undoing a newer press. For example, a start/stop macro pair now changes state and works in two presses, even when one macro keeps running.

Some plugin commands declare the states they need. Assigning one of these commands creates exactly those states, selects the first one, and switches the button to externally controlled state changes. Existing states are reused in order where possible, so layers you already built on them are preserved.

While the command manages the states, the state list, names, order, default, transitions, and reset rules are locked and the editor shows `States are managed by the assigned command`. You can still select each state and edit, move, hide, or arrange its layers. Removing or replacing the owning command asks whether to keep the generated states as normal editable states or discard them and return to a single state. Closing that prompt keeps the states.

## Commands and Command Sequences

Most controls use command sequences. A sequence is a row of one or more commands that run in order.

To assign commands:

1. Open a touch button, physical button, rotary control, or page command editor.
2. Select the command strip you want to edit.
3. Open the command picker.
4. Select a group in the slim rail on the left: `Core`, `Macros`, or `Plugins`.
5. Select a command in the list on the right. Plugin sub-groups still open inline when needed.
6. Press `+ Add` in the footer to insert the selected command, or double-click/press `Enter` on a command as a shortcut.
7. Use the edit icon on a command chip to set parameters.
8. Drag chips to reorder them.
9. Use remove or clear to delete commands.

The picker shows the selected group's commands at full panel height. Rows use a compact two-line layout with an accent bar and separators. The footer identifies the selected command; `+ Add` stays disabled until a command is selected. Search with `Ctrl+K` still filters across all groups, including nested plugin commands, and shows each result's group path.

Plugin commands appear in the order in which the plugin registers them, so related toggle and one-way actions can stay together instead of being sorted arbitrarily.

Built-in command groups include:

| Group | Examples |
| --- | --- |
| Pages | Next/previous touch page, next/previous rotary page, go to page number, left/right rotary page commands on devices with side displays |
| Profiles | Activate profile, go to workspace, next/previous workspace, go to Home workspace |
| Macros | Type text, key combination or sequence, mouse click or scroll, keyboard-plus-mouse, Windows virtual desktops, run a named macro, stop macros |
| Shell | Run a shell command; open an HTTP or HTTPS website in the default browser |
| Button Control | Update a touch button at runtime, remove a named layer |
| Device Control | Device off/on/toggle/wakeup, toggle main window |
| Dynamic Text | Clock |
| User Macros | One entry per macro you create |
| Plugin groups | Commands supplied by installed plugins |

Some commands have parameters, such as a page number, key combination, date/time format, shell command, or target button index. Parameter fields appear in the command chip editor, opened with the pencil icon on the command chip.

For the `Shell Command` chip, the chip label follows the command text while you edit it. If the field is empty, it returns to the `Shell Command` placeholder.

`Open Website` accepts only `http://` and `https://` addresses and uses the system's default browser. If you omit the scheme, LoupixDeck adds `https://`. Other schemes are refused so the command cannot act as a general application launcher.

Shell commands start in your home directory, not in the LoupixDeck installation folder. Use absolute paths when a command refers to a program or file in a particular location; relative paths are resolved from your home directory.

In v1.17.0 and later, commands can provide their own default settings. When you add such a command, its settings popup is already filled with sensible values. You can still change those values for that one button, knob direction, physical button, or page command. Existing assignments are left as they were.

### Direct keyboard and mouse commands

The `Macros` group contains input commands that can run directly from any compatible touch key, physical button, dial gesture, or page command. You do not have to create a named macro first.

| Command | Use |
| --- | --- |
| Mouse Click | Click `Left`, `Right`, `Middle`, `X1`, or `X2` at the current pointer position |
| Mouse Scroll | Scroll the wheel; positive values scroll up and negative values scroll down |
| Keyboard + Mouse | Hold one or more keys while clicking a mouse button or scrolling |
| Key Combination | Press one chord, such as `Ctrl+Shift+S` |
| Key Sequence | Play several chords in order, such as `X, Alt, Ctrl+C` |
| Previous Desktop / Next Desktop | Switch Windows virtual desktops; these two commands are Windows-only |

Use the record button beside the relevant parameter to capture a key combination, a sequence of combinations, or the modifier keys for `Keyboard + Mouse`. In sequence recording, press the chords one after another; `Undo Last` removes the most recent chord before you save. Key sequences insert a short gap between steps, and ordinary combinations are held briefly so applications can recognise them reliably. Mouse buttons X1 and X2 work with the normal Windows and Linux input backends and with the optional Windows Interception backend.

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

### Quick menu and dial presets

Right-click a dial in the main window to configure it without opening the full editor. The menu has separate submenus for `Turn left`, `Turn right`, and `Press`, each backed by the full compatible command catalogue. Use `Remove` to clear one gesture, or `Advanced settings…` to open the complete rotary editor. Clearing the final assigned gesture also clears the dial's side-strip label.

The `Presets` submenu applies a related set of rotary gestures in one step. Built-in presets cover touch-page navigation, rotary-page navigation, workspaces, display brightness, the mouse wheel, and—on Windows—virtual desktops. A preset can leave one of the three gestures empty.

Use `Save this dial as a preset…` to store the current dial under a name. User presets are shared across devices and profiles, appear in the main window's `Dial presets` panel, and can be renamed or deleted there. Built-in presets cannot be edited or removed. Applying a preset copies its commands to the dial; renaming or deleting that preset later does not change dials that already use it. Presets can be applied only to dials.

An enabled plugin can contribute presets for its own commands. The quick menu and action panel group presets under **Built-in**, each plugin's name, and **Yours**. Plugin presets are read-only: you can apply one, but not edit, rename, or delete it. They are read again whenever a preset surface is built, so presets can appear or disappear with live plugin state or after a device is connected, without restarting LoupixDeck. Applying one still copies its commands once; the configured dial does not keep a link to the plugin preset.

For devices with side strips, each knob can also have a strip label. On the Razer Stream Controller, open a side strip and choose its mode for the current rotary page:

- `Segmented` shows the adjacent knobs as separate labelled sections.
- `FreeDraw` uses the normal layer canvas for static or animated image content.
- `PluginOverride` lets you choose an installed plugin provider to draw the whole strip.

A plugin override may be static or animated. Animated providers are paced by LoupixDeck's display scheduler per side, so the left and right strips update steadily without changing how FreeDraw animations work. If a selected provider is unavailable, choose an installed provider again or switch the strip back to `Segmented` or `FreeDraw`.

Devices with separate side displays also offer left and right variants of the rotary page commands. Use the normal `Next Rotary Page`, `Previous Rotary Page`, or `Go to Rotary Page` commands when you want both rotary columns to move together. Use the left/right variants when you want to page only one side.

Use rotary controls for repeated actions such as volume up/down, scene switching, light brightness, timeline navigation, zooming, or page changes.

For device brightness, use `Brightness Up` and `Brightness Down` from the `Device Control` group. A common setup is to put `Brightness Down` on rotate left and `Brightness Up` on rotate right. Each command has a `Step` setting, pre-filled with `5`, which controls how much brightness changes per rotary click. Brightness is clamped to the 0 to 100 range.

## Physical Buttons

Physical buttons are edited similarly to touch buttons, but without the touch-screen layer editor. Double-click a physical button in the main window, then assign one or more commands.

On devices with round LED buttons, their commands, states, and colours belong to the active profile. Switching profiles repaints that profile's LED row; switching workspaces within the same profile leaves it unchanged. Existing configurations are migrated by copying the former shared LED row into every existing profile. A newly created profile starts with dark LED buttons until you assign them.

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

The **ⓘ** button next to Capture opens a list of every key name that works on your system, grouped by kind and with the alternative spellings that mean the same key. The list stays open while you keep editing the macro, and its text can be selected and copied.

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

The main wallpaper can be a still image or a video clip. Select the main panel in `Edit Wallpaper`, press `Select`, and choose an image or a supported video such as MP4, WebM, MOV, MKV, M4V, or AVI. Video wallpaper options include frame rate, opacity, and three fitting modes:

- `Fit` preserves the aspect ratio and letterboxes the clip.
- `Fill` preserves the aspect ratio and crops the overflow.
- `Stretch` distorts the clip to fill the panel.

On devices with side displays, the main clip also covers those columns. A still wallpaper assigned directly to a side display remains on top of the video for that side. Side-display wallpaper slots accept still images, not separate video clips.

Video wallpapers need `ffmpeg` on your system `PATH`. If `ffmpeg` or the selected clip is unavailable, LoupixDeck keeps using the slot's still image and logs the problem once. Short looping clips that are at most 15 seconds long and fit within the 96 MB decoded-frame cache are decoded once for a seamless loop; longer or larger clips stream through `ffmpeg` instead.

Buttons keep updating over the playing clip, including animated button layers. Playback pauses while a screensaver, plugin full-display takeover, folder, or exclusive touch-grid renderer owns the display. It is stopped when you change to another page, so pages without video render and use resources exactly as before.

## Feedback and Haptics

Open `Settings > Feedback` to configure touch feedback and haptics. Touch buttons also have per-button vibration controls when the connected device supports vibration.

The Feedback page has:

- Touch flash: optional visual flash on touch-button press, with color and opacity controls.
- Haptic: an enable toggle and one global effect picker.

In current releases, haptics use LoupixDeck's software vibration pulse. Older config files still load, but the old delay, duration, second-step, and firmware haptic controls are no longer part of the settings page.

Feedback controls are capability-aware. For example, the haptic card and per-button vibration options are not shown for a device without a vibration motor; colour controls are likewise available only where that hardware supports them.

## Screensaver

Open `Settings > Screensaver`.

The screensaver fills the whole device display after the device is idle. It stops when the device receives input, and that first input only wakes the display — it does not also run the button or dial action.

Choose the **Source** first:

- **Video** plays a video or GIF file.
- **Plugin** plays an animation provided by an installed plugin.

Options include:

- Enable animated screensaver.
- Source (Video or Plugin).
- Select or clear the video (Video source).
- Choose the plugin providing the animation (Plugin source).
- Idle timeout in seconds.
- FPS limit.
- Loop continuously (Video source).

A **video** screensaver needs `ffmpeg` on your system `PATH`. If LoupixDeck cannot find it, the settings page shows a warning.

A **plugin** screensaver does not need `ffmpeg` — the plugin renders the frames itself. Notes:

- The FPS limit only applies when the plugin does not declare a frame rate of its own.
- If the selected plugin is disabled or uninstalled, no screensaver plays and the normal page stays on screen. Selecting an available plugin again restores it.
- A plugin animation that reaches its end stops the screensaver and returns to the active page, just like a video that is not set to loop.

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
- On a group master, optionally choose touch and rotary pages for individual companions.
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

Companion page targets belong to the selected workspace and are applied after the master changes context. A companion does not evaluate a separate copy of the master's rules. If a saved target no longer exists, the rule editor marks it so you can choose another page or remove the target.

Process matching is case-insensitive, and a trailing `.exe` is ignored. On Linux, foreground process names reported by the kernel can be truncated after 15 characters; current releases also match a longer configured name when its first 15 characters equal that reported value. Profile Rules require X11 or XWayland plus `xprop`; pure Wayland is not supported by the current README.

## Plugins and Integrations

LoupixDeck v1.28.0 and later install integrations separately from the app. In v1.30.0 and later, open **Plugins** from the hamburger menu instead of looking under Settings. The separate window remains usable without a connected device. Its left rail switches between installed plugins and the Plugin Store and shows the installed count plus a badge when updates are pending.

### Plugin Store

Search the store or browse its two-column tile grid. Each tile shows the plugin's description, author, published version, compatibility, and installed state. Release notes load on demand and can be read without installing anything.

Use `Install`, `Update`, or `Remove` on a tile. Before an install or update starts, LoupixDeck shows that release's notes and waits for confirmation. It then displays download progress and verifies the package's SHA-256 checksum before opening the archive. A missing or mismatched checksum prevents installation, and `Cancel` stops a download in progress.

The store selects only releases whose `plugin.json` supports the current operating system and an SDK version provided by this LoupixDeck release. A plugin without a compatible release is labelled accordingly instead of being installed. Plugin releases are independent from LoupixDeck releases, so fixes can be delivered without updating the main app.

Installing a plugin does not enable it. Choose `Setup` to move to the installed-plugins page, select the device, and enable it explicitly. Its commands then appear in the side panel immediately, without restarting. The enabled state is saved as soon as it changes. Updating or removing a plugin also takes effect immediately when its files can be replaced safely. If the operating system has a loaded file locked, LoupixDeck stages the change, shows `Restart required`, and offers to restart before completing it on the next start. Updates preserve the plugin's `settings.json`.

The store also offers `Adopt` when it recognizes a hand-copied plugin, making that installation store-managed. `Check for app update` appears when the available plugin needs a newer LoupixDeck release.

Published versions now come from the catalogue, so `Refresh` needs one request instead of querying every plugin's GitHub releases. If the current catalogue cannot be fetched, the store can use a cached copy, marks it as possibly outdated, and shows when it was saved. A missing catalogue path no longer leaves the page waiting indefinitely, and an entry without a usable release is shown as unavailable.

The background plugin-update check follows the main app's automatic update-check setting. When compatible plugin updates are available, a hint appears in the main window; open it to go to the Plugin Store.

Plugins installed from a zip or copied into the user plugin folder still load. They are labelled `Manually installed`, and the store does not replace them automatically unless you choose `Adopt`. Use the installed-plugins page for manual installations and plugin-specific configuration.

### Upgrading from an older LoupixDeck release

Plugins that came with a pre-v1.28 installation are retained, including their settings, and become manageable by the store. The Windows installer already keeps plugins in the per-user configuration directory. On Linux, `install-loupixdeck.sh` moves previously bundled copies from the application directory to `~/.config/LoupixDeck/plugins`; an existing user copy of the same or a newer version wins, and its settings are kept.

New v1.28 installations contain no bundled plugins. Install only the integrations you need from the store.

### Missing or removed plugins

Removing a plugin does not erase its button, dial, physical-button, page-command, or macro assignments. Editors show the unresolved command as unavailable, and it starts working again after the owning plugin is installed and enabled. An unresolved command known to belong to a plugin is not treated as free-form shell text and is never executed as a shell command.

After startup, LoupixDeck checks saved configuration for commands owned by catalogue plugins that are not installed. It offers to open the Plugin Store with the first missing plugin highlighted. Declining the offer is remembered for that plugin; the assignments themselves remain unchanged.

### Installed plugins and settings

The installed-plugins page has a searchable list with each plugin's status dot, version, and pending-update state. The detail header names the device being configured and provides a picker when more than one device is running. The enable switch is also in this header because enablement is stored per device.

Plugin setting forms use the current interface language. If you switch plugins or close the window with unsaved changes, LoupixDeck asks before discarding them. An action supplied by a plugin shows its returned message without guessing that it succeeded; only a failed call is labelled as a failure.

From this page you can:

- Install a plugin from a zip file.
- Remove a plugin.
- Open the plugins folder.
- Select a plugin and edit its settings if it provides a settings UI.
- Enable or disable a plugin for the selected device. The choice is saved immediately and refreshes its commands and dial presets live.

The v1.30.0 Plugin Store catalogue includes these integrations:

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
| LinuxHwInfo | Linux |
| SteelSeries Sonar | Windows |

Plugins can add commands, dynamic text, settings pages, folders, side-strip providers, or special integration behavior. The exact command names depend on the installed plugin version and what external app or service is configured.

LoupixDeck v1.30.0 uses Plugin SDK 1.23.0. It adds `DialPresetDescriptor` and `GetDialPresets()` so plugins can contribute presets for their own rotary commands. The change is additive, so existing plugins do not need to be rebuilt and older 1.x plugins continue to load.

The Plugins page only shows plugins that can run on the current operating system. For example, Windows-only plugins are hidden on Linux instead of appearing as disabled rows with controls that cannot work.

Plugins can also provide default values for command settings. In current store versions, some Audio, Elgato, and Spotify commands use this for editable step sizes, so a rotary can move volume, light brightness, or a Spotify value faster or slower without special syntax.

On a multi-device setup, plugin button-state reads, state changes, and refresh requests apply across every device on which that plugin is enabled. A stateful plugin button on a secondary device therefore stays synchronized just like one on the primary device.

Installing, removing, enabling, disabling, or switching the active version of a plugin refreshes the command catalogue, dial presets, and side-strip providers for every connected device. You do not need to reconnect secondary devices for the refreshed plugin state to appear.

### Audio

The Audio plugin from the Plugin Store controls output and input devices on Windows and Linux and can also work with the separate audio streams of running applications. Assign `Audio: Mixer` to a touch or physical button to open a live folder with one tile per application currently playing on any output device. Tap a tile to select that application; the first dial then changes its volume, and pressing the dial toggles mute. The selected tile is blue and muted tiles are red.

The mixer updates when applications start or stop playing. If the selected application disappears, another available tile becomes selected so the dial does not keep pointing at a missing stream. The display is only repainted when its content changes.

The Audio command group also offers per-application volume up/down, mute toggle, and exact-volume commands. A saved application binding uses its process identity so it can work again after the application restarts. Device commands can set an exact volume and, where supported, make a selected output device the system default.

Choose a sound folder in the Audio plugin settings to expose its files under `Audio > Play Sound`. Normally, pressing a sound button repeatedly starts overlapping copies. Enable `Stop on second press` in the plugin settings if a second press should stop that sound instead; this option is off by default. `Audio: Stop Sounds` stops every sound started by the plugin at once without affecting audio from other applications.

On Linux, MP3 and M4A playback uses an external player when a particular playback device is selected. Current releases only use players that accept that device explicitly; when no suitable player is installed, the plugin writes an explanation to the log instead of silently using the default output.

### SteelSeries Sonar

Install the Windows-only SteelSeries Sonar plugin from the Plugin Store. It exposes controls for the Sonar mixer, including the separate streaming and monitoring volumes used by stream mode. Enable it for the current device on the installed-plugins page; it is not offered on Linux.

### OBS Studio

The OBS plugin from the Plugin Store reports recording, replay-buffer, virtual-camera, streaming, and studio-mode status live through obs-websocket. Buttons using the corresponding toggle commands follow changes made inside OBS and resynchronize after either OBS or LoupixDeck restarts.

OBS plugin 1.3.0 fixes authenticated connections that could previously fail with `Authentication failed` even when the password was correct. `Test Connection` now reuses an existing connection while the saved host, port, and password are unchanged, so repeated tests no longer fail with `already Identified`; changing connection settings still starts a fresh session.

`Toggle Recording`, `Toggle Replay Buffer`, `Toggle Streaming`, `Toggle Virtual Camera`, and `Toggle Studio Mode` are stateful commands. Assigning one creates its button states automatically and draws the current-state indicator over your own background and layers. The matching one-way actions, such as `Start Recording` or `Stop Recording`, remain ordinary commands because they do not represent both sides of a toggle. `Pause Recording` resumes a paused recording when used again.

OBS commands are grouped by recording, replay buffer, streaming, virtual camera, and studio mode, with each toggle followed by its one-way actions. New actions include stream start/stop, studio-mode toggle, and `Trigger Transition`. The command picker also provides dynamic folders for:

- `Scenes` and `Preview Scenes`, including `Set Preview Scene` in the latter.
- `Audio`, with mute, unmute, and toggle-mute commands for each input.
- `Sources`, grouped first by scene and then by source, with show, hide, and toggle commands.

### Monitoring Plugins

Argus Monitor, HWiNFO, LibreHardwareMonitor, and LinuxHwInfo can show sensor readings on touch buttons when the matching Plugin Store integration is installed and enabled. LibreHardwareMonitor was bundled with LoupixDeck from v1.13.1 through v1.27.1; LinuxHwInfo was bundled in Linux releases from v1.25.0 through v1.27.1. From v1.28.0 onward, install either one from the store when needed.

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
| Display Test Pattern | Show a diagnostic pattern on the device display |

These are useful for a sleep/wake button, a clean desk mode, or a button that brings the editor back when hidden.

### Display test patterns

Assign **Device Control > Display Test Pattern** to a button when you need to check a panel or its key alignment. Its submenu offers **Key grid**, **Pixel ruler**, **Colours**, and **Panel edges**. The unqualified command cycles through all four patterns: key grid shows frames, corner blocks and centring squares; pixel ruler counts inward from each key edge; colours shows red, green, blue and white quadrants with a grey ramp; and panel edges shows a full-panel frame, key boundaries and centring squares.

The test temporarily owns the display, so normal pages are not drawn over it. Press any device control to end it. LoupixDeck blanks the panel while handing the test display over and back, then restores the current page.

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
| `System.DisplayTest(<grid|ruler|color|edges>)` | Show a named diagnostic pattern; without an argument, cycle through the patterns |
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

- Language: English, German, or Spanish. The selection applies immediately and is shared by all connected devices.
- Check for updates automatically: look for a new release after startup (on by default). See [Update notifications](#update-notifications).
- Device name and connection state.
- Port and baudrate.
- Firmware and serial.
- Reconnect.
- Brightness.
- Dithering.
- Key alignment: adjust key size, horizontal and vertical spacing, and the first key's X/Y centre. Changes update the device live; use **Show test pattern on device** to line up the crosshair and border, or **Reset to device default** to discard an adjustment.
- Startup touch page.
- Start with Windows (Windows only).
- Close button behavior: minimize to tray or quit.
- Start minimized to tray.
- Page switching: show the page name overlay, animate rotary page transitions, animate touch page transitions, and choose whether next/previous navigation cycles at the ends.

English is the source language. If a translated string is unavailable, LoupixDeck falls back to its English text instead of showing a blank label. Built-in command names, group headings, category cards, and command chips are translated at display time, and command search matches the translated wording. Assignments, macros, and dial presets continue to store stable internal command ids, so changing the language does not rewrite them. Names supplied directly by plugins remain in the language provided by the plugin.

On Windows, `Start with Windows` controls whether LoupixDeck launches at login. The installer can set the same behavior during setup with `Start on system startup`, but v1.12.1 and later let you turn it on or off from this settings page. Use it together with `Start minimized to tray` if you want LoupixDeck to launch quietly after login.

`Dithering` is next to the brightness control. It is off by default. Turning it on smooths gradients and can reduce colour banding on the device displays, especially on the Razer Stream Controller. The setting repaints the current display immediately when changed.

Key alignment normally needs no changes: LoupixDeck uses the measured layout for the selected model, and existing configurations retain their previous layout. It is available for panels whose image is visibly offset or the wrong size. The test preview ends when the settings window closes or you press a device key.

### Profiles

- Add, rename, and delete profiles.
- Add, rename, and delete workspaces inside profiles.
- Activate a profile or workspace.
- Set a profile's Home workspace.
- Set the Default profile used at startup.

### Companions

- Create, rename, and delete companion groups.
- Choose one master and one or more companions from connected devices.
- Choose whether companions follow touch pages, touch and rotary pages, or neither.
- Choose whether companions follow the master into custom folders.
- See whether each group member is currently connected.
- Pause, resume, or resync a group from the master's commands while LoupixDeck is running.

### Pages

- Manage touch pages for the active workspace.
- Manage rotary pages for the active workspace.
- Reorder pages.
- Edit wallpapers.
- Edit page commands.

Custom folders are created and arranged from the right-side **Folders** panel in the main window rather than this settings page.

### Feedback

- Configure touch flash color and opacity.
- Enable haptic feedback and choose the global haptic effect.

### Screensaver

- Enable animated screensaver.
- Choose `Video` or `Plugin` as the source.
- Select a video/GIF or an installed plugin provider.
- Set idle timeout and FPS limit; looping applies to video sources.
- Install `ffmpeg` only when using a video source.

### Plugin Store

Plugin management and the Plugin Store now live in their own **Plugins** window rather than Settings. See [Plugins and Integrations](#plugins-and-integrations).

### Macro Driver

Windows only. Shows Interception driver status and lets you install, uninstall, or enable use of the driver for keyboard and mouse macros.

Important: Interception is third-party software. It is not bundled with LoupixDeck, and commercial use may require a separate license from its author.

### Profile Rules

- Enable automatic profile and workspace switching.
- Add rules based on process name and optional title text.
- Choose profile, workspace, and optional page behavior.
- On a companion-group master, choose optional page targets for individual companions.
- Set rule priority.
- Trigger rules when a process starts.
- Choose what happens to the profile when you leave matched apps.

### Theme

- Dark.
- Light.
- System.

Current releases give cards, borders, and hint text distinct tones in both Light and Dark themes. The page-name placeholder in the main pager follows the same readable palette.

### About

- Version.
- Project website link.
- Check for updates: asks GitHub for the latest release, even when the automatic check is off.

You can close the About window from its title-bar close button as well as from its dialog controls. Other dialogs can also be closed from the title bar without leaving the app stuck or keeping their menu entry disabled.

## Files and Backup

LoupixDeck stores configuration as JSON in the user config directory. Typical files include:

| File | Purpose |
| --- | --- |
| `config.json` | Global settings |
| `config_<device>.json` | Per-device layout and device settings |
| `macros.json` | Shared macro definitions |
| `custom-apps.json` | Applications added manually to the Apps panel |
| `dial-presets.json` | User-created dial presets shared across devices and profiles |
| `ui-settings.json` | Interface language and update-check preferences shared by all devices |
| `companions.json` | Global master and companion group definitions |
| Plugin config files | Integration-specific settings |

Per-device layout is scoped by serial number when possible. Existing file names stay stable: adding a second identical device creates a separate serial-scoped file rather than renaming the first device's file. The per-device layout contains that device's profiles, workspaces, stable page ids, custom folders, and device-specific settings. If a config file is corrupted, LoupixDeck creates a backup before writing a fresh file.

## Troubleshooting

### Device is not detected

- If the deck is plugged in but the empty state remains, another program may be holding its serial port. LoupixDeck keeps retrying in the background; close the vendor application or service if you want to release the port immediately.
- A busy device does not appear in the selector until its serial link succeeds. You do not need to restart LoupixDeck after the port is released.
- Unplug and reconnect the device.
- Try `Settings > General > Reconnect`.
- If a device re-enumerates after a cable or power change, LoupixDeck re-resolves its serial port when exactly one device with the saved USB identity is present. Disconnect duplicate matching devices before retrying.
- On Linux, check that udev rules were installed and reconnect the device.
- If a Razer Stream Controller X is detected on Linux but its keys do nothing, rerun the current Linux installer and reconnect the device. A manual udev setup also needs this additional HID-raw rule:

  ```udev
  SUBSYSTEM=="hidraw", ATTRS{idVendor}=="1532", ATTRS{idProduct}=="0d09", MODE="0666"
  ```

- Avoid unreliable USB hubs.

### Macros do not affect the target app

- On Windows, try running without Interception first.
- If the target app reads raw input, consider the optional Interception driver.
- Some games or protected applications may block synthetic input.
- On Linux, check `uinput` and input group permissions.

### Macro recording does not work on Linux

Recording needs read access to `/dev/input/event*`. The installer attempts to handle this through group permissions, but you may need to log out and back in after group changes.

### Screensaver does not play

- Check the selected **Source** in `Settings > Screensaver`.
- For a video source: install `ffmpeg` and make sure it is on `PATH`.
- Try a lower FPS limit.
- Test with a simple local video file.
- For a plugin source: open the Plugins window, make sure the plugin is still installed and enabled for the current device, and check that it is selected in the screensaver picker.

### Video wallpaper does not play

- Make sure the clip is assigned to the page's main wallpaper rather than a left or right side slot.
- Install `ffmpeg` and make sure it is on `PATH`.
- Check that the selected video file still exists at its saved path.
- If one side remains static, check whether that side has its own still wallpaper, which intentionally appears over the main clip.
- Leave a screensaver, plugin display takeover, folder, or other exclusive touch-grid view; wallpaper playback pauses while one of them owns the display.

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

- Open **Plugins > Plugin Store** and install a missing catalogue plugin, or update it if a compatible release is available.
- On the installed-plugins page, check whether the plugin is enabled for the current device.
- If you use more than one device, enable the plugin on the device where you want to use it.
- Restart LoupixDeck if the plugin page says some changes need a restart.
- An unavailable plugin command remains assigned and is not executed as shell text. Reinstalling and enabling its plugin restores it.
- A loaded plugin group with no currently available commands stays visible as a grey information row. For example, this can happen when an integration is enabled but no matching external device is connected.
- Confirm that external services such as OBS, Spotify, or monitoring tools are running and configured.
- For LibreHardwareMonitor, confirm that LibreHardwareMonitor is running in the background and that its HTTP web server is enabled. If the web server uses authentication, check the username and password in the plugin settings.

### Monitoring tiles look wrong or still use old settings

Sensor plugins changed from plain text output to tile rendering. If an old Argus, HWiNFO, or LibreHardwareMonitor button looks wrong after updating, remove the old sensor command or plugin-specific layer/settings from the button and add the sensor command again.

### Collecting crash logs

If LoupixDeck cannot start, current Windows releases first show the startup error in a message box instead of closing silently. If LoupixDeck closes unexpectedly after startup, start it with a diagnostics flag to record what happened:

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
