#!/usr/bin/env bash
# LoupixDeck Linux installer – distro-agnostic.
# Downloads a GitHub release binary (or builds master from source), installs
# it system-wide, and sets up udev rules and a desktop entry. The build is
# self-contained, so no separate .NET runtime is required to run it.
#
# Usage: install-loupixdeck.sh [version] [--from-source] [--restart]
#   version        Release tag to install (e.g. v1.22.0). Defaults to the
#                  latest release. A leading 'v' is optional. With
#                  --from-source it is the version stamped into the build.
#   --from-source  Clone and build master of LoupixDeck instead. Needs git
#                  and the .NET SDK. Plugins come from the in-app Plugin Store.
#   --restart      Close a running LoupixDeck before installing and start it
#                  again afterwards. Used by the in-app updater.
set -euo pipefail

REPO="RadiatorTwo/LoupixDeck"
ASSET_NAME="LoupixDeck-linux-x64.tar.gz"
INSTALL_DIR="/usr/local/lib/loupixdeck"
SYMLINK="/usr/local/bin/loupixdeck"
DESKTOP_FILE="/usr/share/applications/loupixdeck.desktop"
UDEV_RULES_FILE="/etc/udev/rules.d/99-loupixdeck.rules"
# SteamOS 3.6+ only carries files under /etc over to a new OS image when they are listed here.
ATOMIC_KEEP_DIR="/etc/atomic-update.conf.d"
ATOMIC_KEEP_FILE="$ATOMIC_KEEP_DIR/loupixdeck.conf"

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

log()  { printf '\033[1;34m>>>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m!!!\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31mERR\033[0m %s\n' "$*" >&2; exit 1; }

require() { command -v "$1" >/dev/null 2>&1 || die "Required tool missing: $1"; }

# ---------- root / sudo ----------
if [ "$(id -u)" -ne 0 ]; then
    command -v sudo >/dev/null 2>&1 || die "Please run as root or install sudo."
    SUDO="sudo"
else
    SUDO=""
fi

# ---------- Target user ----------
# The user the app runs as: the one who gets the 'input' group and owns the plugin folder.
TARGET_USER="${SUDO_USER:-}"
if [ -z "$TARGET_USER" ] && command -v logname >/dev/null 2>&1; then
    TARGET_USER="$(logname 2>/dev/null || true)"
fi
if [ -z "$TARGET_USER" ] && [ "$(id -u)" -ne 0 ]; then
    TARGET_USER="$(id -un)"
fi
TARGET_HOME=""
if [ -n "$TARGET_USER" ] && [ "$TARGET_USER" != "root" ]; then
    TARGET_HOME="$({ getent passwd "$TARGET_USER" || true; } | cut -d: -f6)"
fi

# ---------- SteamOS ----------
# SteamOS ships a read-only OS image that every system update replaces, so nothing may be
# installed below /usr. The app goes into the user's home instead; only the udev rule needs
# root, and it is added to the atomic-update keep list so it survives the next update.
STEAMOS=0
if [ -r /etc/os-release ] && grep -qx 'ID=steamos' /etc/os-release; then
    STEAMOS=1
    # Run as root, every file in the home would belong to root and the app could not update it.
    [ "$(id -u)" -ne 0 ] || die "On SteamOS, run the installer as your normal user (without sudo)."
    TARGET_HOME="${TARGET_HOME:-$HOME}"
    INSTALL_DIR="$TARGET_HOME/.local/lib/loupixdeck"
    SYMLINK="$TARGET_HOME/.local/bin/loupixdeck"
    DESKTOP_FILE="$TARGET_HOME/.local/share/applications/loupixdeck.desktop"
fi

# The prefix for app files: sudo for the system-wide install, nothing for the home install.
HOME_SUDO="$SUDO"
[ "$STEAMOS" -eq 0 ] || HOME_SUDO=""

# ---------- Architecture check ----------
ARCH="$(uname -m)"
case "$ARCH" in
    x86_64|amd64) ;;
    *) die "Unsupported architecture: $ARCH (only x86_64/amd64)." ;;
esac

# ---------- Base tools ----------
require uname
require tar
# DL fetches the release archive and shows a progress bar when a terminal is attached; piped
# or logged runs stay quiet. DL_STDOUT (API queries) is always silent.
if command -v curl >/dev/null 2>&1; then
    DL() {
        if [ -t 2 ]; then curl -fL --progress-bar "$1" -o "$2"; else curl -fsSL "$1" -o "$2"; fi
    }
    DL_STDOUT() { curl -fsSL "$1"; }
elif command -v wget >/dev/null 2>&1; then
    DL() {
        if [ -t 2 ]; then wget -q --show-progress -O "$2" "$1"; else wget -qO "$2" "$1"; fi
    }
    DL_STDOUT() { wget -qO- "$1"; }
else
    die "Neither curl nor wget found."
fi

# ---------- Arguments ----------
REQUESTED_VERSION=""
FROM_SOURCE=0
RESTART=0
for arg in "$@"; do
    case "$arg" in
        -h|--help)
            printf 'Usage: %s [version] [--from-source] [--restart]\n\n' "$(basename "$0")"
            printf '  version        Release tag to install (e.g. v1.22.0).\n'
            printf '                 Defaults to the latest release. With --from-source\n'
            printf '                 it is the version stamped into the build.\n'
            printf '  --from-source  Clone master of LoupixDeck, build it locally and\n'
            printf '                 install the result. Requires git and the .NET SDK.\n'
            printf '                 Plugins come from the in-app Plugin Store.\n'
            printf '  --restart      Close a running LoupixDeck before installing and\n'
            printf '                 start it again afterwards.\n'
            exit 0
            ;;
        --from-source) FROM_SOURCE=1 ;;
        --restart) RESTART=1 ;;
        -*) die "Unknown option: $arg (see --help)." ;;
        *)
            [ -z "$REQUESTED_VERSION" ] || die "Only one version may be given (see --help)."
            REQUESTED_VERSION="$arg"
            ;;
    esac
done
# Matched by the executable a process runs, not by its name: started through the
# lowercase symlink (the desktop entry does that), the process is named 'loupixdeck',
# so 'pgrep -x LoupixDeck' never saw it. /proc/<pid>/exe resolves to the real binary.
app_running() {
    local exe
    for exe in /proc/[0-9]*/exe; do
        [ "$(readlink "$exe" 2>/dev/null)" = "$INSTALL_DIR/LoupixDeck" ] && return 0
    done
    return 1
}

# Started by the in-app updater in its own terminal window. The app has already quit, so a
# failed or cancelled update (no password, no network, ...) starts the previous version again
# instead of leaving the user without it, and the window stays open so the error can be read.
on_restart_exit() {
    local rc=$1
    rm -rf "$TMP_DIR"
    [ "$rc" -ne 0 ] || return 0
    if [ "$(id -u)" -ne 0 ] && [ -x "$INSTALL_DIR/LoupixDeck" ] && ! app_running; then
        warn "Installation failed - starting the installed LoupixDeck again."
        setsid nohup "$SYMLINK" >/dev/null 2>&1 < /dev/null &
    fi
    if [ -t 0 ]; then
        read -rp "Installation failed. Press Enter to close this window." _ || true
    fi
}
if [ "$RESTART" -eq 1 ]; then
    trap 'on_restart_exit $?' EXIT
fi

# Ask for the password before anything is downloaded, built or removed: declining it then
# ends the script with the installation untouched, not halfway through replacing it.
if [ -n "$SUDO" ]; then
    if [ "$STEAMOS" -eq 1 ]; then
        log "Administrator rights are needed to set up the device permissions (udev rule)."
        # The 'deck' user has no password until one is set, so sudo cannot succeed before that.
        sudo -v || die "No administrator rights - nothing was changed. If your user has no password yet, set one with 'passwd' and run the installer again."
    else
        log "Administrator rights are needed to install into $INSTALL_DIR."
        sudo -v || die "No administrator rights - nothing was changed."
    fi
    # Keep the credentials fresh for long source builds; ends together with this script.
    ( while kill -0 "$$" 2>/dev/null; do sudo -n true 2>/dev/null; sleep 50; done ) &
fi
# With --from-source the version is not a release to download but the version the master build
# reports, e.g. an older number to try the in-app updater against a published release.
BUILD_VERSION=""
if [ "$FROM_SOURCE" -eq 1 ] && [ -n "$REQUESTED_VERSION" ]; then
    BUILD_VERSION="${REQUESTED_VERSION#v}"
    printf '%s' "$BUILD_VERSION" | grep -qE '^[0-9]+\.[0-9]+\.[0-9]+$' \
        || die "Version '$REQUESTED_VERSION' must look like 1.25.0 (or v1.25.0)."
fi

# ---------- Build from source ----------
# Mirrors the app part of .github/workflows/release.yml. Plugins are not built: they are
# installed from the Plugin Store inside the app.
build_from_source() {
    require git
    require dotnet
    dotnet --list-sdks 2>/dev/null | grep -q . \
        || die "No .NET SDK found ('dotnet --list-sdks' is empty). Install the .NET SDK, not only the runtime."

    local src="$TMP_DIR/src"
    local out="$TMP_DIR/publish"
    mkdir -p "$src"
    export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

    log "Cloning $REPO (master) ..."
    git clone --quiet --depth 1 --recurse-submodules --shallow-submodules \
        "https://github.com/$REPO.git" "$src/LoupixDeck"
    TAG="master ($(git -C "$src/LoupixDeck" rev-parse --short HEAD))"
    [ -z "$BUILD_VERSION" ] || TAG="$TAG as v$BUILD_VERSION"

    log "Publishing LoupixDeck for linux-x64 ..."
    dotnet publish "$src/LoupixDeck/LoupixDeck/LoupixDeck.csproj" -c Release -r linux-x64 --self-contained true \
        -p:PublishSingleFile=true \
        -p:PublishTrimmed=false \
        -p:EnableCompressionInSingleFile=true \
        -p:ReadyToRun=true \
        ${BUILD_VERSION:+-p:Version=$BUILD_VERSION} \
        -o "$out"

    find "$out" -name '*.pdb' -delete

    SRC="$out"
    [ -f "$SRC/LoupixDeck" ] || die "Binary 'LoupixDeck' not found in publish output ($SRC)."
}

# ---------- Resolve & download release ----------
download_release() {
    if [ -n "$REQUESTED_VERSION" ]; then
        log "Querying release $REQUESTED_VERSION of $REPO ..."
        API_JSON="$(DL_STDOUT "https://api.github.com/repos/$REPO/releases/tags/$REQUESTED_VERSION" || true)"
        # Retry with a 'v' prefix so both '1.22.0' and 'v1.22.0' work.
        case "$REQUESTED_VERSION" in
            v*) ;;
            *)
                if [ -z "$API_JSON" ] || ! printf '%s' "$API_JSON" | grep -q '"tag_name"'; then
                    API_JSON="$(DL_STDOUT "https://api.github.com/repos/$REPO/releases/tags/v$REQUESTED_VERSION" || true)"
                fi
                ;;
        esac
        if [ -z "$API_JSON" ] || ! printf '%s' "$API_JSON" | grep -q '"tag_name"'; then
            die "Release '$REQUESTED_VERSION' not found. List available tags with: curl -fsSL https://api.github.com/repos/$REPO/releases | grep tag_name"
        fi
    else
        log "Querying latest release of $REPO ..."
        API_JSON="$(DL_STDOUT "https://api.github.com/repos/$REPO/releases/latest")"
    fi

    TAG="$(printf '%s' "$API_JSON" | grep -oE '"tag_name"[[:space:]]*:[[:space:]]*"[^"]+"' | head -n1 | sed -E 's/.*"([^"]+)"$/\1/')"
    DOWNLOAD_URL="$(printf '%s' "$API_JSON" \
        | grep -oE '"browser_download_url"[[:space:]]*:[[:space:]]*"[^"]+"' \
        | sed -E 's/.*"([^"]+)"$/\1/' \
        | grep -F "$ASSET_NAME" \
        | head -n1)"

    [ -n "$TAG" ]          || die "Could not determine release tag."
    [ -n "$DOWNLOAD_URL" ] || die "Asset '$ASSET_NAME' not found in release $TAG."
    log "Release $TAG → $DOWNLOAD_URL"

    log "Downloading archive ..."
    DL "$DOWNLOAD_URL" "$TMP_DIR/loupixdeck.tar.gz"

    log "Extracting ..."
    mkdir -p "$TMP_DIR/extract"
    tar -xzf "$TMP_DIR/loupixdeck.tar.gz" -C "$TMP_DIR/extract"

    # Resolve source: extracted directly or a single subdirectory
    SRC="$TMP_DIR/extract"
    mapfile -t TOP < <(find "$SRC" -mindepth 1 -maxdepth 1)
    if [ "${#TOP[@]}" -eq 1 ] && [ -d "${TOP[0]}" ]; then
        SRC="${TOP[0]}"
    fi
    [ -f "$SRC/LoupixDeck" ] || die "Binary 'LoupixDeck' not found in archive ($SRC)."
}

if [ "$FROM_SOURCE" -eq 1 ]; then
    build_from_source
else
    download_release
fi

# ---------- Close running app (--restart) ----------
# The running app is asked to quit through its own IPC channel (a second instance forwards
# 'quit'), so it shuts its devices down cleanly before its files are replaced. The in-app
# updater quits on its own right after starting this script; then there is nothing to do.
if [ "$RESTART" -eq 1 ] && app_running; then
    log "Closing running LoupixDeck ..."
    if [ -x "$SYMLINK" ]; then
        "$SYMLINK" quit >/dev/null 2>&1 || true
    fi
    for _ in $(seq 1 40); do
        app_running || break
        sleep 0.5
    done
    app_running && die "LoupixDeck is still running. Close it and run the installer again."
fi

# ---------- Move bundled plugins to the user plugin folder ----------
# Releases before the Plugin Store shipped every plugin in $INSTALL_DIR/plugins. The new build
# has none, so wiping the install directory would take them (and their settings) away. They
# move into the user plugin folder instead, where the app loads them and the store updates
# them. A copy already there with the same or a newer version wins; its settings are kept.
manifest_field() {
    if command -v jq >/dev/null 2>&1; then
        jq -r --arg f "$2" '.[$f] // empty' "$1"
    else
        { grep -oE "\"$2\"[[:space:]]*:[[:space:]]*\"[^\"]+\"" "$1" || true; } | head -n1 | sed -E 's/.*"([^"]+)"$/\1/'
    fi
}

# True when version $1 is lower than version $2.
version_lt() {
    [ "$1" != "$2" ] && [ "$(printf '%s\n%s\n' "$1" "$2" | sort -V | head -n1)" = "$1" ]
}

if [ -d "$INSTALL_DIR/plugins" ]; then
    if [ -z "$TARGET_HOME" ] || [ ! -d "$TARGET_HOME" ]; then
        warn "Target user unknown - bundled plugins in $INSTALL_DIR/plugins are removed with the old version. Install them again from the Plugin Store."
    else
        USER_PLUGINS="$TARGET_HOME/.config/LoupixDeck/plugins"
        moved=()
        for plugin_dir in "$INSTALL_DIR"/plugins/*/; do
            manifest="${plugin_dir}plugin.json"
            [ -f "$manifest" ] || continue
            id="$(manifest_field "$manifest" id)"
            [ -n "$id" ] || continue
            case "$id" in */*|.|..) continue ;; esac

            target="$USER_PLUGINS/$id"
            if [ -f "$target/plugin.json" ]; then
                bundled_version="$(manifest_field "$manifest" version)"
                user_version="$(manifest_field "$target/plugin.json" version)"
                version_lt "$user_version" "$bundled_version" || continue
            fi

            staged="$TMP_DIR/migrate/$id"
            mkdir -p "$staged"
            cp -a "$plugin_dir". "$staged/"
            # The user copy's settings are newer than anything next to the old binary.
            [ -f "$target/settings.json" ] && cp -a "$target/settings.json" "$staged/settings.json"

            $SUDO mkdir -p "$USER_PLUGINS"
            $SUDO rm -rf "$target"
            $SUDO cp -a "$staged" "$target"
            $SUDO chown -R "$TARGET_USER": "$target"
            moved+=("$id")
        done
        # The folders above the plugins may have been created by root just now.
        if [ "${#moved[@]}" -gt 0 ]; then
            $SUDO chown "$TARGET_USER": "$TARGET_HOME/.config/LoupixDeck" "$USER_PLUGINS" 2>/dev/null || true
            log "Moved bundled plugins to $USER_PLUGINS: ${moved[*]}"
        fi
    fi
fi

if [ -d "$INSTALL_DIR" ]; then
    log "Removing previous installation at $INSTALL_DIR ..."
    $HOME_SUDO rm -rf "$INSTALL_DIR"
fi
log "Installing into $INSTALL_DIR ..."
$HOME_SUDO mkdir -p "$INSTALL_DIR"
$HOME_SUDO cp -a "$SRC"/. "$INSTALL_DIR/"
$HOME_SUDO chmod +x "$INSTALL_DIR/LoupixDeck"

log "Creating symlink $SYMLINK -> $INSTALL_DIR/LoupixDeck ..."
$HOME_SUDO mkdir -p "$(dirname "$SYMLINK")"
$HOME_SUDO ln -sf "$INSTALL_DIR/LoupixDeck" "$SYMLINK"

# ---------- udev rules ----------
if [ -d /etc/udev/rules.d ]; then
    log "Writing udev rules to $UDEV_RULES_FILE ..."
    $SUDO tee "$UDEV_RULES_FILE" >/dev/null <<'EOF'
# LoupixDeck supported devices. The deck is driven over a CDC-ACM serial port
# (/dev/ttyACM*), so EVERY supported VID/PID needs a 'tty' rule — without it,
# opening the port fails for a user who is not in the 'dialout' group. The 'usb'
# rule grants access to the raw USB node used for detection/hot-plug. Keep this
# list in sync with DeviceRegistry.SupportedDevices.

# Loupedeck Live (2ec2:0004)
SUBSYSTEM=="usb", ATTRS{idVendor}=="2ec2", ATTRS{idProduct}=="0004", MODE="0666"
SUBSYSTEM=="tty", ATTRS{idVendor}=="2ec2", ATTRS{idProduct}=="0004", MODE="0666"
# Loupedeck Live S (2ec2:0006)
SUBSYSTEM=="usb", ATTRS{idVendor}=="2ec2", ATTRS{idProduct}=="0006", MODE="0666"
SUBSYSTEM=="tty", ATTRS{idVendor}=="2ec2", ATTRS{idProduct}=="0006", MODE="0666"
# Loupedeck CT (2ec2:0003)
SUBSYSTEM=="usb", ATTRS{idVendor}=="2ec2", ATTRS{idProduct}=="0003", MODE="0666"
SUBSYSTEM=="tty", ATTRS{idVendor}=="2ec2", ATTRS{idProduct}=="0003", MODE="0666"
# Loupedeck CT (2ec2:0007)
SUBSYSTEM=="usb", ATTRS{idVendor}=="2ec2", ATTRS{idProduct}=="0007", MODE="0666"
SUBSYSTEM=="tty", ATTRS{idVendor}=="2ec2", ATTRS{idProduct}=="0007", MODE="0666"
# Razer Stream Controller (1532:0d06)
SUBSYSTEM=="usb", ATTRS{idVendor}=="1532", ATTRS{idProduct}=="0d06", MODE="0666"
SUBSYSTEM=="tty", ATTRS{idVendor}=="1532", ATTRS{idProduct}=="0d06", MODE="0666"
# Razer Stream Controller X (1532:0d09)
SUBSYSTEM=="usb", ATTRS{idVendor}=="1532", ATTRS{idProduct}=="0d09", MODE="0666"
SUBSYSTEM=="tty", ATTRS{idVendor}=="1532", ATTRS{idProduct}=="0d09", MODE="0666"
# The X also needs its HID interface readable: its firmware stalls the serial key frames
# until the HID reports are drained, and on Linux only LoupixDeck does that.
SUBSYSTEM=="hidraw", ATTRS{idVendor}=="1532", ATTRS{idProduct}=="0d09", MODE="0666"
# uinput – virtual keyboard/mouse for macro execution (granted to the 'input' group)
KERNEL=="uinput", SUBSYSTEM=="misc", GROUP="input", MODE="0660", OPTIONS+="static_node=uinput"
EOF
    # Without the keep-list entry SteamOS drops the rule on the next A/B system update and the
    # deck stops working until the installer runs again.
    if [ "$STEAMOS" -eq 1 ] || [ -d "$ATOMIC_KEEP_DIR" ]; then
        log "Keeping the udev rule across system updates ($ATOMIC_KEEP_FILE) ..."
        $SUDO mkdir -p "$ATOMIC_KEEP_DIR"
        printf '%s\n' "$UDEV_RULES_FILE" | $SUDO tee "$ATOMIC_KEEP_FILE" >/dev/null
    fi
    if command -v udevadm >/dev/null 2>&1; then
        $SUDO udevadm control --reload-rules || true
        $SUDO udevadm trigger || true
    else
        warn "udevadm not found – rules will apply after reboot or re-plug."
    fi
else
    warn "/etc/udev/rules.d does not exist – skipping udev rules."
fi

# ---------- input group membership ----------
# Both macro execution (/dev/uinput, via the rule above) and macro recording
# (reading /dev/input/event*) are gated behind the 'input' group. Add the invoking
# user so neither needs root or world-writable nodes.
if [ -n "$TARGET_USER" ] && [ "$TARGET_USER" != "root" ]; then
    if ! getent group input >/dev/null 2>&1; then
        log "Creating 'input' group ..."
        $SUDO groupadd -r input || warn "Could not create 'input' group."
    fi

    if id -nG "$TARGET_USER" 2>/dev/null | tr ' ' '\n' | grep -qx input; then
        log "User '$TARGET_USER' is already in the 'input' group."
    else
        log "Adding user '$TARGET_USER' to the 'input' group ..."
        if $SUDO usermod -aG input "$TARGET_USER"; then
            warn "Log out and back in for the 'input' group to take effect (needed for macros and recording)."
        else
            warn "Could not add '$TARGET_USER' to the 'input' group – add it manually: sudo usermod -aG input $TARGET_USER"
        fi
    fi
else
    warn "Could not determine the target user – add yourself to the 'input' group manually: sudo usermod -aG input <user>"
fi

# ---------- Desktop entry ----------
ICON_PATH=""
for cand in LoupixDeck.png LoupixDeck.svg LoupixDeck.ico icon.png; do
    if [ -f "$INSTALL_DIR/$cand" ]; then ICON_PATH="$INSTALL_DIR/$cand"; break; fi
done
[ -n "$ICON_PATH" ] || ICON_PATH="loupixdeck"

APPLICATIONS_DIR="$(dirname "$DESKTOP_FILE")"
[ "$STEAMOS" -eq 0 ] || mkdir -p "$APPLICATIONS_DIR"
if [ -d "$APPLICATIONS_DIR" ]; then
    log "Writing desktop entry $DESKTOP_FILE ..."
    $HOME_SUDO tee "$DESKTOP_FILE" >/dev/null <<EOF
[Desktop Entry]
Name=LoupixDeck
Comment=Razer Stream Controller & Loupedeck Live S Control
Exec=$SYMLINK
Icon=$ICON_PATH
Terminal=false
Type=Application
Categories=Utility;AudioVideo;
StartupNotify=true
EOF
    command -v update-desktop-database >/dev/null 2>&1 \
        && $HOME_SUDO update-desktop-database "$APPLICATIONS_DIR" || true
fi

# ---------- Done ----------
echo
log "Done. LoupixDeck $TAG installed."
if [ "$STEAMOS" -eq 1 ]; then
    log "Launch LoupixDeck from the application menu in Desktop Mode, or run: $SYMLINK"
else
    log "Launch with: loupixdeck   (or from your application menu)"
fi

# ---------- Restart app (--restart) ----------
if [ "$RESTART" -eq 1 ]; then
    if [ "$(id -u)" -ne 0 ]; then
        log "Starting LoupixDeck ..."
        setsid nohup "$SYMLINK" >/dev/null 2>&1 < /dev/null &
    else
        warn "Running as root - start LoupixDeck from your application menu."
    fi
    if [ -t 0 ]; then
        read -rp "Press Enter to close this window." _ || true
    fi
fi
