#!/usr/bin/env bash
# LoupixDeck Linux installer – distro-agnostic.
# Lädt das neueste GitHub-Release-Binary, installiert systemweit,
# richtet udev-Regeln, Desktop-Eintrag und (falls nötig) .NET-Runtime ein.
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

require() { command -v "$1" >/dev/null 2>&1 || die "Erforderliches Tool fehlt: $1"; }

# ---------- root / sudo ----------
if [ "$(id -u)" -ne 0 ]; then
    command -v sudo >/dev/null 2>&1 || die "Bitte als root ausführen oder sudo installieren."
    SUDO="sudo"
else
    SUDO=""
fi

# ---------- Architektur prüfen ----------
ARCH="$(uname -m)"
case "$ARCH" in
    x86_64|amd64) ;;
    *) die "Nicht unterstützte Architektur: $ARCH (nur x86_64/amd64)." ;;
esac

# ---------- Basis-Tools ----------
require uname
require tar
if command -v curl >/dev/null 2>&1; then
    DL() { curl -fsSL "$1" -o "$2"; }
    DL_STDOUT() { curl -fsSL "$1"; }
elif command -v wget >/dev/null 2>&1; then
    DL() { wget -qO "$2" "$1"; }
    DL_STDOUT() { wget -qO- "$1"; }
else
    die "Weder curl noch wget gefunden."
fi

# ---------- Distro-Erkennung ----------
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
log "Distribution: ${PRETTY_NAME:-unbekannt} (id=$DISTRO_ID, like=$DISTRO_ID_LIKE)"

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

# ---------- Paketmanager-Wrapper ----------
PKG_MGR=""
for c in apt-get dnf yum zypper pacman apk xbps-install eopkg emerge; do
    if command -v "$c" >/dev/null 2>&1; then PKG_MGR="$c"; break; fi
done
log "Paketmanager: ${PKG_MGR:-keiner gefunden}"

pkg_install() {
    # $@ = Paketnamen; gibt 0 bei Erfolg zurück, !=0 sonst.
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

# ---------- .NET Runtime sicherstellen ----------
have_dotnet_runtime() {
    command -v dotnet >/dev/null 2>&1 || return 1
    dotnet --list-runtimes 2>/dev/null \
        | awk '/^Microsoft\.NETCore\.App /{print $2}' \
        | cut -d. -f1 \
        | grep -qx "$DOTNET_REQUIRED_MAJOR"
}

install_dotnet_via_pkg() {
    # Liefert distro-typische Paketnamen, probiert von spezifisch nach generisch.
    local candidates=()
    if distro_matches "arch cachyos manjaro endeavouros"; then
        candidates=("dotnet-runtime-${DOTNET_REQUIRED_MAJOR}.0" "dotnet-runtime")
    elif distro_matches "fedora rhel centos rocky almalinux"; then
        candidates=("dotnet-runtime-${DOTNET_REQUIRED_MAJOR}.0")
    elif distro_matches "opensuse opensuse-tumbleweed opensuse-leap suse sles"; then
        candidates=("dotnet-runtime-${DOTNET_REQUIRED_MAJOR}.0")
    elif distro_matches "debian ubuntu linuxmint pop elementary kali raspbian"; then
        # Microsoft-Repo zuerst probieren, da Distro-Pakete oft fehlen/veraltet sind
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
        log "Versuche Runtime-Paket: $pkg"
        if pkg_install "$pkg"; then return 0; fi
    done
    return 1
}

ms_repo_debian_ubuntu() {
    # Bindet packages.microsoft.com ein (Debian/Ubuntu-Familie). Best effort.
    dpkg -l packages-microsoft-prod 2>/dev/null | grep -q '^ii' && return 0
    local id="${DISTRO_ID:-ubuntu}" ver="${DISTRO_VERSION_ID:-22.04}"
    local url="https://packages.microsoft.com/config/${id}/${ver}/packages-microsoft-prod.deb"
    local deb="$TMP_DIR/msprod.deb"
    DL "$url" "$deb" || DL "https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb" "$deb" || return 1
    $SUDO dpkg -i "$deb" || return 1
    return 0
}

install_dotnet_via_script() {
    # Microsofts offizielles Installer-Skript, systemweit
    log "Installiere .NET Runtime via dotnet-install.sh nach /usr/share/dotnet ..."
    local script="$TMP_DIR/dotnet-install.sh"
    DL "https://dot.net/v1/dotnet-install.sh" "$script"
    chmod +x "$script"
    $SUDO "$script" --channel "${DOTNET_REQUIRED_MAJOR}.0" --runtime dotnet --install-dir /usr/share/dotnet
    $SUDO ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
}

ensure_dotnet_runtime() {
    if have_dotnet_runtime; then
        log ".NET Runtime ${DOTNET_REQUIRED_MAJOR} bereits vorhanden."
        return
    fi
    log ".NET Runtime ${DOTNET_REQUIRED_MAJOR} nicht gefunden, installiere ..."
    if install_dotnet_via_pkg && have_dotnet_runtime; then
        log ".NET Runtime via Paketmanager installiert."
        return
    fi
    warn "Paketmanager-Installation fehlgeschlagen oder Runtime weiterhin nicht gefunden – Fallback auf dotnet-install.sh."
    install_dotnet_via_script
    have_dotnet_runtime || die ".NET Runtime ${DOTNET_REQUIRED_MAJOR} konnte nicht installiert werden."
}

ensure_dotnet_runtime

# ---------- Release ermitteln & laden ----------
log "Ermittle neuestes Release von $REPO ..."
API_JSON="$(DL_STDOUT "https://api.github.com/repos/$REPO/releases/latest")"

TAG="$(printf '%s' "$API_JSON" | grep -oE '"tag_name"[[:space:]]*:[[:space:]]*"[^"]+"' | head -n1 | sed -E 's/.*"([^"]+)"$/\1/')"
DOWNLOAD_URL="$(printf '%s' "$API_JSON" \
    | grep -oE '"browser_download_url"[[:space:]]*:[[:space:]]*"[^"]+"' \
    | sed -E 's/.*"([^"]+)"$/\1/' \
    | grep -F "$ASSET_NAME" \
    | head -n1)"

[ -n "$TAG" ]          || die "Konnte Release-Tag nicht ermitteln."
[ -n "$DOWNLOAD_URL" ] || die "Asset '$ASSET_NAME' im Release $TAG nicht gefunden."
log "Release $TAG → $DOWNLOAD_URL"

log "Lade Archiv ..."
DL "$DOWNLOAD_URL" "$TMP_DIR/loupixdeck.tar.gz"

log "Entpacke ..."
mkdir -p "$TMP_DIR/extract"
tar -xzf "$TMP_DIR/loupixdeck.tar.gz" -C "$TMP_DIR/extract"

# Quelle ermitteln: direkt entpackt oder ein einzelnes Unterverzeichnis
SRC="$TMP_DIR/extract"
mapfile -t TOP < <(find "$SRC" -mindepth 1 -maxdepth 1)
if [ "${#TOP[@]}" -eq 1 ] && [ -d "${TOP[0]}" ]; then
    SRC="${TOP[0]}"
fi
[ -f "$SRC/LoupixDeck" ] || die "Binary 'LoupixDeck' im Archiv nicht gefunden ($SRC)."

# ---------- Installieren ----------
if [ -d "$INSTALL_DIR" ]; then
    log "Entferne alte Installation in $INSTALL_DIR ..."
    $SUDO rm -rf "$INSTALL_DIR"
fi
log "Installiere nach $INSTALL_DIR ..."
$SUDO mkdir -p "$INSTALL_DIR"
$SUDO cp -a "$SRC"/. "$INSTALL_DIR/"
$SUDO chmod +x "$INSTALL_DIR/LoupixDeck"

log "Erstelle Symlink $SYMLINK -> $INSTALL_DIR/LoupixDeck ..."
$SUDO mkdir -p "$(dirname "$SYMLINK")"
$SUDO ln -sf "$INSTALL_DIR/LoupixDeck" "$SYMLINK"

# ---------- udev-Regeln ----------
if [ -d /etc/udev/rules.d ]; then
    log "Schreibe udev-Regeln nach $UDEV_RULES_FILE ..."
    $SUDO tee "$UDEV_RULES_FILE" >/dev/null <<'EOF'
# Razer Stream Controller (USB)
SUBSYSTEM=="usb", ATTRS{idVendor}=="1532", ATTRS{idProduct}=="0d06", MODE="0666"
# Loupedeck Live S (USB + serielles tty)
SUBSYSTEM=="usb", ATTRS{idVendor}=="2ec2", ATTRS{idProduct}=="0006", MODE="0666"
SUBSYSTEM=="tty", ATTRS{idVendor}=="2ec2", ATTRS{idProduct}=="0006", MODE="0666"
EOF
    if command -v udevadm >/dev/null 2>&1; then
        $SUDO udevadm control --reload-rules || true
        $SUDO udevadm trigger || true
    else
        warn "udevadm nicht gefunden – Regeln greifen erst nach Neustart oder Re-Plug."
    fi
else
    warn "/etc/udev/rules.d existiert nicht – überspringe udev-Regeln."
fi

# ---------- Desktop-Eintrag ----------
ICON_PATH=""
for cand in LoupixDeck.png LoupixDeck.svg LoupixDeck.ico icon.png; do
    if [ -f "$INSTALL_DIR/$cand" ]; then ICON_PATH="$INSTALL_DIR/$cand"; break; fi
done
[ -n "$ICON_PATH" ] || ICON_PATH="loupixdeck"

if [ -d /usr/share/applications ]; then
    log "Schreibe Desktop-Eintrag $DESKTOP_FILE ..."
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

# ---------- Fertig ----------
echo
log "Fertig. LoupixDeck $TAG installiert."
log "Start mit: loupixdeck   (oder über das Anwendungsmenü)"
