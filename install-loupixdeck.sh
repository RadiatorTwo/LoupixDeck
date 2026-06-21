#!/usr/bin/env bash
# LoupixDeck Linux installer – distro-agnostic.
# Downloads the latest GitHub release binary, installs it system-wide,
# and sets up udev rules and a desktop entry. The release build is
# self-contained, so no separate .NET runtime is required.
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

# ---------- Resolve & download release ----------
log "Querying latest release of $REPO ..."
API_JSON="$(DL_STDOUT "https://api.github.com/repos/$REPO/releases/latest")"

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

# ---------- Install ----------
if [ -d "$INSTALL_DIR" ]; then
    log "Removing previous installation at $INSTALL_DIR ..."
    $SUDO rm -rf "$INSTALL_DIR"
fi
log "Installing into $INSTALL_DIR ..."
$SUDO mkdir -p "$INSTALL_DIR"
$SUDO cp -a "$SRC"/. "$INSTALL_DIR/"
$SUDO chmod +x "$INSTALL_DIR/LoupixDeck"

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
