#!/usr/bin/env bash
# LoupixDeck Linux installer – distro-agnostic.
# Downloads the latest GitHub release binary, installs it system-wide,
# sets up udev rules, a desktop entry, and the .NET runtime if missing.
set -euo pipefail

REPO="RadiatorTwo/LoupixDeck"
ASSET_NAME="LoupixDeck-linux-x64.tar.gz"
INSTALL_DIR="/usr/local/lib/loupixdeck"
SYMLINK="/usr/local/bin/loupixdeck"
DESKTOP_FILE="/usr/share/applications/loupixdeck.desktop"
UDEV_RULES_FILE="/etc/udev/rules.d/99-loupixdeck.rules"
DOTNET_REQUIRED_MAJOR=9

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

# ---------- Distro detection ----------
DISTRO_ID=""
DISTRO_ID_LIKE=""
DISTRO_VERSION_ID=""
if [ -r /etc/os-release ]; then
    # shellcheck disable=SC1091
    . /etc/os-release
    DISTRO_ID="${ID:-}"
    DISTRO_ID_LIKE="${ID_LIKE:-}"
    DISTRO_VERSION_ID="${VERSION_ID:-}"
fi
log "Distribution: ${PRETTY_NAME:-unknown} (id=$DISTRO_ID, like=$DISTRO_ID_LIKE)"

distro_matches() {
    # $1 = space-separated list of ids to test against $DISTRO_ID / $DISTRO_ID_LIKE
    local needle
    for needle in $1; do
        case " $DISTRO_ID $DISTRO_ID_LIKE " in
            *" $needle "*) return 0 ;;
        esac
    done
    return 1
}

# ---------- Package manager wrapper ----------
PKG_MGR=""
for c in apt-get dnf yum zypper pacman apk xbps-install eopkg emerge; do
    if command -v "$c" >/dev/null 2>&1; then PKG_MGR="$c"; break; fi
done
log "Package manager: ${PKG_MGR:-none found}"

pkg_install() {
    # $@ = package names; returns 0 on success, non-zero otherwise.
    case "$PKG_MGR" in
        apt-get)      $SUDO apt-get update -qq && $SUDO apt-get install -y "$@" ;;
        dnf|yum)      $SUDO "$PKG_MGR" install -y "$@" ;;
        zypper)       $SUDO zypper --non-interactive install "$@" ;;
        pacman)       $SUDO pacman -S --noconfirm --needed "$@" ;;
        apk)          $SUDO apk add --no-cache "$@" ;;
        xbps-install) $SUDO xbps-install -Sy "$@" ;;
        eopkg)        $SUDO eopkg install -y "$@" ;;
        emerge)       $SUDO emerge --quiet --noreplace "$@" ;;
        *)            return 1 ;;
    esac
}

# ---------- Ensure .NET runtime ----------
have_dotnet_runtime() {
    command -v dotnet >/dev/null 2>&1 || return 1
    dotnet --list-runtimes 2>/dev/null \
        | awk '/^Microsoft\.NETCore\.App /{print $2}' \
        | cut -d. -f1 \
        | grep -qx "$DOTNET_REQUIRED_MAJOR"
}

install_dotnet_via_pkg() {
    # Tries distro-typical package names, from specific to generic.
    local candidates=()
    if distro_matches "arch cachyos manjaro endeavouros"; then
        candidates=("dotnet-runtime-${DOTNET_REQUIRED_MAJOR}.0" "dotnet-runtime")
    elif distro_matches "fedora rhel centos rocky almalinux"; then
        candidates=("dotnet-runtime-${DOTNET_REQUIRED_MAJOR}.0")
    elif distro_matches "opensuse opensuse-tumbleweed opensuse-leap suse sles"; then
        candidates=("dotnet-runtime-${DOTNET_REQUIRED_MAJOR}.0")
    elif distro_matches "debian ubuntu linuxmint pop elementary kali raspbian"; then
        # Try Microsoft repo first, distro packages are often missing/outdated
        ms_repo_debian_ubuntu && candidates=("dotnet-runtime-${DOTNET_REQUIRED_MAJOR}.0")
    elif distro_matches "alpine"; then
        candidates=("dotnet${DOTNET_REQUIRED_MAJOR}-runtime")
    elif distro_matches "void"; then
        candidates=("dotnet-runtime")
    elif distro_matches "gentoo"; then
        candidates=("dev-dotnet/dotnet-runtime-bin")
    elif distro_matches "solus"; then
        candidates=("dotnet-runtime")
    fi

    local pkg
    for pkg in "${candidates[@]}"; do
        log "Trying runtime package: $pkg"
        if pkg_install "$pkg"; then return 0; fi
    done
    return 1
}

ms_repo_debian_ubuntu() {
    # Adds packages.microsoft.com (Debian/Ubuntu family). Best effort.
    dpkg -l packages-microsoft-prod 2>/dev/null | grep -q '^ii' && return 0
    local id="${DISTRO_ID:-ubuntu}" ver="${DISTRO_VERSION_ID:-22.04}"
    local url="https://packages.microsoft.com/config/${id}/${ver}/packages-microsoft-prod.deb"
    local deb="$TMP_DIR/msprod.deb"
    DL "$url" "$deb" || DL "https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb" "$deb" || return 1
    $SUDO dpkg -i "$deb" || return 1
    return 0
}

install_dotnet_via_script() {
    # Microsoft's official install script, system-wide
    log "Installing .NET runtime via dotnet-install.sh into /usr/share/dotnet ..."
    local script="$TMP_DIR/dotnet-install.sh"
    DL "https://dot.net/v1/dotnet-install.sh" "$script"
    chmod +x "$script"
    $SUDO "$script" --channel "${DOTNET_REQUIRED_MAJOR}.0" --runtime dotnet --install-dir /usr/share/dotnet
    $SUDO ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
}

ensure_dotnet_runtime() {
    if have_dotnet_runtime; then
        log ".NET runtime ${DOTNET_REQUIRED_MAJOR} already present."
        return
    fi
    log ".NET runtime ${DOTNET_REQUIRED_MAJOR} not found, installing ..."
    if install_dotnet_via_pkg && have_dotnet_runtime; then
        log ".NET runtime installed via package manager."
        return
    fi
    warn "Package manager install failed or runtime still not found – falling back to dotnet-install.sh."
    install_dotnet_via_script
    have_dotnet_runtime || die ".NET runtime ${DOTNET_REQUIRED_MAJOR} could not be installed."
}

ensure_dotnet_runtime

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
# Razer Stream Controller (USB)
SUBSYSTEM=="usb", ATTRS{idVendor}=="1532", ATTRS{idProduct}=="0d06", MODE="0666"
# Loupedeck Live S (USB + serial tty)
SUBSYSTEM=="usb", ATTRS{idVendor}=="2ec2", ATTRS{idProduct}=="0006", MODE="0666"
SUBSYSTEM=="tty", ATTRS{idVendor}=="2ec2", ATTRS{idProduct}=="0006", MODE="0666"
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
