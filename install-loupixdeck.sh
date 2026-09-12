#!/usr/bin/env bash
# LoupixDeck Linux installer – distro-agnostic.
# Downloads a GitHub release binary (or builds master from source), installs
# it system-wide, and sets up udev rules and a desktop entry. The build is
# self-contained, so no separate .NET runtime is required to run it.
#
# Usage: install-loupixdeck.sh [version | --from-source]
#   version        Release tag to install (e.g. v1.22.0). Defaults to the
#                  latest release. A leading 'v' is optional.
#   --from-source  Clone and build master of LoupixDeck, the Plugin SDK and
#                  all bundled plugins instead. Needs git and the .NET SDK.
set -euo pipefail

REPO="RadiatorTwo/LoupixDeck"
ASSET_NAME="LoupixDeck-linux-x64.tar.gz"
INSTALL_DIR="/usr/local/lib/loupixdeck"
SYMLINK="/usr/local/bin/loupixdeck"
DESKTOP_FILE="/usr/share/applications/loupixdeck.desktop"
UDEV_RULES_FILE="/etc/udev/rules.d/99-loupixdeck.rules"

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

# ---------- Architecture check ----------
ARCH="$(uname -m)"
case "$ARCH" in
    x86_64|amd64) ;;
    *) die "Unsupported architecture: $ARCH (only x86_64/amd64)." ;;
esac

# ---------- Base tools ----------
require uname
require tar
if command -v curl >/dev/null 2>&1; then
    DL() { curl -fsSL "$1" -o "$2"; }
    DL_STDOUT() { curl -fsSL "$1"; }
elif command -v wget >/dev/null 2>&1; then
    DL() { wget -qO "$2" "$1"; }
    DL_STDOUT() { wget -qO- "$1"; }
else
    die "Neither curl nor wget found."
fi

# ---------- Arguments ----------
REQUESTED_VERSION=""
FROM_SOURCE=0
for arg in "$@"; do
    case "$arg" in
        -h|--help)
            printf 'Usage: %s [version | --from-source]\n\n' "$(basename "$0")"
            printf '  version        Release tag to install (e.g. v1.22.0).\n'
            printf '                 Defaults to the latest release.\n'
            printf '  --from-source  Clone master of LoupixDeck, the Plugin SDK and all\n'
            printf '                 bundled plugins, build them locally and install the\n'
            printf '                 result. Requires git and the .NET SDK.\n'
            exit 0
            ;;
        --from-source) FROM_SOURCE=1 ;;
        -*) die "Unknown option: $arg (see --help)." ;;
        *)
            [ -z "$REQUESTED_VERSION" ] || die "Only one version may be given (see --help)."
            REQUESTED_VERSION="$arg"
            ;;
    esac
done
if [ "$FROM_SOURCE" -eq 1 ] && [ -n "$REQUESTED_VERSION" ]; then
    die "--from-source builds master; a version cannot be combined with it."
fi

# ---------- Build from source ----------
# Mirrors .github/workflows/release.yml: the SDK package is built first, the
# plugins restore it from a local feed, and each plugin is assembled into
# plugins/<id>/ next to the self-contained app. Keep PLUGIN_REPOS in sync with
# the plugin list of the 'build-plugins' job there.
# Entry format: <GitHub repository name>:<directory / project name>
PLUGIN_REPOS=(
    LoupixDeck.Plugin.Obs:LoupixDeck.Plugin.Obs
    LoupixDeck.Plugin.Elgato:LoupixDeck.Plugin.Elgato
    LoupixDeck.Plugin.HwInfo:LoupixDeck.Plugin.HwInfo
    LoupixDeck.Plugin.CoolerControl:LoupixDeck.Plugin.CoolerControl
    LoupixDeck.Plugin.LibreHardwareMonitor:LoupixDeck.Plugin.LibreHardwareMonitor
    LoupixDeck.Plugin.Audio:LoupixDeck.Plugin.Audio
    LoupixDeck.Plugin.Argus:LoupixDeck.Plugin.Argus
    LoupixDeck.Plugin.SpotifyPremium:LoupixDeck.Plugin.SpotifyPremium
    LoupixDeck.Plugin.LinuxHWInfo:LoupixDeck.Plugin.LinuxHwInfo
    LoupixDeck.Plugin.SteelseriesSonar:LoupixDeck.Plugin.SteelseriesSonar
)

plugin_id() {
    if command -v jq >/dev/null 2>&1; then
        jq -r '.id // empty' "$1"
    else
        grep -oE '"id"[[:space:]]*:[[:space:]]*"[^"]+"' "$1" | head -n1 | sed -E 's/.*"([^"]+)"$/\1/'
    fi
}

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

    log "Cloning RadiatorTwo/LoupixDeck.PluginSdk (master) ..."
    git clone --quiet --depth 1 "https://github.com/RadiatorTwo/LoupixDeck.PluginSdk.git" "$src/LoupixDeck.PluginSdk"

    log "Building Plugin SDK package ..."
    dotnet build "$src/LoupixDeck.PluginSdk/LoupixDeck.PluginSdk.csproj" -c Release

    # Plugin nuget.config files point at the SDK feed either as a sibling repo
    # (../LoupixDeck.PluginSdk/nupkg) or nested in the core repo
    # (../LoupixDeck/LoupixDeck.PluginSdk/nupkg); provide both, as CI does.
    mkdir -p "$src/LoupixDeck/LoupixDeck.PluginSdk/nupkg"
    cp -r "$src/LoupixDeck.PluginSdk/nupkg/." "$src/LoupixDeck/LoupixDeck.PluginSdk/nupkg/"

    log "Publishing LoupixDeck for linux-x64 ..."
    dotnet publish "$src/LoupixDeck/LoupixDeck/LoupixDeck.csproj" -c Release -r linux-x64 --self-contained true \
        -p:PublishSingleFile=true \
        -p:PublishTrimmed=false \
        -p:EnableCompressionInSingleFile=true \
        -p:ReadyToRun=true \
        -o "$out"

    local entry repo dir manifest id built=() failed=()
    mkdir -p "$out/plugins"
    for entry in "${PLUGIN_REPOS[@]}"; do
        repo="${entry%%:*}"
        dir="${entry#*:}"
        log "Building plugin $dir ..."
        if ! git clone --quiet --depth 1 "https://github.com/RadiatorTwo/$repo.git" "$src/$dir"; then
            warn "Could not clone $repo – skipping."
            failed+=("$dir")
            continue
        fi
        manifest="$src/$dir/plugin.json"
        id="$( [ -f "$manifest" ] && plugin_id "$manifest" || true )"
        if [ -z "$id" ] || [ "$id" = "null" ]; then
            warn "$dir has no 'id' in plugin.json – skipping."
            failed+=("$dir")
            continue
        fi
        if ! dotnet build "$src/$dir/$dir.csproj" -c Release -o "$TMP_DIR/build/$dir" -p:DebugSymbols=false -p:DebugType=none; then
            warn "Build of $dir failed – skipping."
            failed+=("$dir")
            continue
        fi
        mkdir -p "$out/plugins/$id"
        find "$TMP_DIR/build/$dir" -mindepth 1 -maxdepth 1 ! -name '*.pdb' ! -name '*.runtimeconfig.json' \
            -exec cp -r {} "$out/plugins/$id/" \;
        cp "$manifest" "$out/plugins/$id/plugin.json"
        built+=("$id")
    done

    find "$out" -name '*.pdb' -delete
    log "Plugins built: ${built[*]:-none}"
    [ "${#failed[@]}" -eq 0 ] || warn "Plugins skipped: ${failed[*]}"

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

# ---------- Install ----------
# A bundled plugin keeps its settings next to its own binary
# ($INSTALL_DIR/plugins/<id>/settings.json), so wiping the install directory would take
# every plugin configuration with it. Rescue those files first and put them back once
# the new build is in place.
PRESERVED_SETTINGS="$TMP_DIR/preserved-settings"
if [ -d "$INSTALL_DIR/plugins" ]; then
    for plugin_dir in "$INSTALL_DIR"/plugins/*/; do
        [ -f "${plugin_dir}settings.json" ] || continue
        plugin_id="$(basename "$plugin_dir")"
        mkdir -p "$PRESERVED_SETTINGS/$plugin_id"
        cp -a "${plugin_dir}settings.json" "$PRESERVED_SETTINGS/$plugin_id/settings.json"
    done
    if [ -d "$PRESERVED_SETTINGS" ]; then
        log "Preserving plugin settings: $(ls "$PRESERVED_SETTINGS" | tr '
' ' ')"
    fi
fi

if [ -d "$INSTALL_DIR" ]; then
    log "Removing previous installation at $INSTALL_DIR ..."
    $SUDO rm -rf "$INSTALL_DIR"
fi
log "Installing into $INSTALL_DIR ..."
$SUDO mkdir -p "$INSTALL_DIR"
$SUDO cp -a "$SRC"/. "$INSTALL_DIR/"
$SUDO chmod +x "$INSTALL_DIR/LoupixDeck"

# ---------- Restore plugin settings ----------
if [ -d "$PRESERVED_SETTINGS" ]; then
    for saved in "$PRESERVED_SETTINGS"/*/; do
        plugin_id="$(basename "$saved")"
        if [ -d "$INSTALL_DIR/plugins/$plugin_id" ]; then
            $SUDO cp -a "${saved}settings.json" "$INSTALL_DIR/plugins/$plugin_id/settings.json"
        else
            # The plugin is not part of this build; its folder would end up without a
            # plugin.json, which the app skips anyway.
            warn "Plugin '$plugin_id' is not part of this build - its settings were dropped."
        fi
    done
    log "Plugin settings restored."
fi

log "Creating symlink $SYMLINK -> $INSTALL_DIR/LoupixDeck ..."
$SUDO mkdir -p "$(dirname "$SYMLINK")"
$SUDO ln -sf "$INSTALL_DIR/LoupixDeck" "$SYMLINK"

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
# uinput – virtual keyboard/mouse for macro execution (granted to the 'input' group)
KERNEL=="uinput", SUBSYSTEM=="misc", GROUP="input", MODE="0660", OPTIONS+="static_node=uinput"
EOF
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
TARGET_USER="${SUDO_USER:-}"
if [ -z "$TARGET_USER" ] && command -v logname >/dev/null 2>&1; then
    TARGET_USER="$(logname 2>/dev/null || true)"
fi

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

# ---------- Plugin settings ownership ----------
# The app writes a bundled plugin's settings next to that plugin, inside the
# root-owned install directory. Without a writable file there, saving fails silently
# (the app only logs it) and every plugin setting is lost on restart. Handing the
# settings file - and only that file - to the user keeps the binaries root-owned.
if [ -n "$TARGET_USER" ] && [ "$TARGET_USER" != "root" ] && [ -d "$INSTALL_DIR/plugins" ]; then
    log "Making plugin settings writable for '$TARGET_USER' ..."
    for plugin_dir in "$INSTALL_DIR"/plugins/*/; do
        [ -f "${plugin_dir}plugin.json" ] || continue
        settings_file="${plugin_dir}settings.json"
        # Created empty when absent, because writing a new file would need write access
        # to the root-owned plugin directory itself.
        [ -f "$settings_file" ] || printf '{}
' | $SUDO tee "$settings_file" >/dev/null
        $SUDO chown "$TARGET_USER" "$settings_file"             || warn "Could not hand $settings_file to '$TARGET_USER'."
        $SUDO chmod 0644 "$settings_file"
    done
elif [ -d "$INSTALL_DIR/plugins" ]; then
    warn "Target user unknown - plugin settings stay root-owned and the app cannot save them."
fi

# ---------- Desktop entry ----------
ICON_PATH=""
for cand in LoupixDeck.png LoupixDeck.svg LoupixDeck.ico icon.png; do
    if [ -f "$INSTALL_DIR/$cand" ]; then ICON_PATH="$INSTALL_DIR/$cand"; break; fi
done
[ -n "$ICON_PATH" ] || ICON_PATH="loupixdeck"

if [ -d /usr/share/applications ]; then
    log "Writing desktop entry $DESKTOP_FILE ..."
    $SUDO tee "$DESKTOP_FILE" >/dev/null <<EOF
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
        && $SUDO update-desktop-database /usr/share/applications || true
fi

# ---------- Done ----------
echo
log "Done. LoupixDeck $TAG installed."
log "Launch with: loupixdeck   (or from your application menu)"
