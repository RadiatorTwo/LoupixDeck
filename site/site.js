"use strict";

// The store index is read live from master, so the list stays current without redeploying the site.
const PLUGIN_STORE_URL = "https://raw.githubusercontent.com/RadiatorTwo/LoupixDeck/master/plugin-store.json";
const PLATFORMS = ["Windows", "Linux", "macOS"];
const ICON_PUZZLE = "\u{F0431}";

function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
}

// Install tabs

function initInstallTabs() {
    const tabs = Array.from(document.querySelectorAll('.install [role="tab"]'));

    const select = (tab) => {
        for (const t of tabs) {
            const on = t === tab;
            t.setAttribute("aria-selected", on ? "true" : "false");
            t.tabIndex = on ? 0 : -1;
            document.getElementById(t.getAttribute("aria-controls")).hidden = !on;
        }
    };

    tabs.forEach((tab, i) => {
        tab.addEventListener("click", () => select(tab));
        tab.addEventListener("keydown", (e) => {
            let next = null;
            if (e.key === "ArrowRight") next = tabs[(i + 1) % tabs.length];
            else if (e.key === "ArrowLeft") next = tabs[(i - 1 + tabs.length) % tabs.length];
            else if (e.key === "Home") next = tabs[0];
            else if (e.key === "End") next = tabs[tabs.length - 1];
            if (next) {
                e.preventDefault();
                select(next);
                next.focus();
            }
        });
    });

    // Preselect the visitor's platform.
    const ua = navigator.userAgent;
    const id = /Mac OS X|Macintosh/.test(ua) ? "tab-mac" : /Linux|X11/.test(ua) && !/Android/.test(ua) ? "tab-linux" : null;
    if (id) select(document.getElementById(id));
}

function initCopyButton() {
    const button = document.getElementById("copy-install");
    const label = button.querySelector(".label");
    const command = document.getElementById("install-cmd").textContent;
    let timer = 0;

    button.addEventListener("click", async () => {
        try {
            await navigator.clipboard.writeText(command);
            label.textContent = "Copied";
        } catch (e) {
            label.textContent = "Copy failed";
        }
        clearTimeout(timer);
        timer = setTimeout(() => { label.textContent = "Copy"; }, 1600);
    });
}

// Downloads

// Release assets keep their names across versions, so these always resolve to the latest release.
const DOWNLOAD_BASE = "https://github.com/RadiatorTwo/LoupixDeck/releases/latest/download/";
const DOWNLOADS = {
    "win-x64": { file: "LoupixDeck-Setup-win-x64.exe", label: "Download for Windows", detail: "Windows x64 installer" },
    "win-arm64": { file: "LoupixDeck-Setup-win-arm64.exe", label: "Download for Windows", detail: "Windows on ARM installer" },
    "mac-arm64": { file: "LoupixDeck-osx-arm64.dmg", label: "Download for macOS", detail: "macOS disk image for Apple Silicon" },
    "mac-x64": { file: "LoupixDeck-osx-x64.dmg", label: "Download for macOS", detail: "macOS disk image for Intel Macs" },
};

function detectPlatform() {
    const ua = navigator.userAgent;
    if (/iPhone|iPad|iPod|Android/.test(ua)) return null;
    if (/Windows/.test(ua)) return "win";
    if (/Mac OS X|Macintosh/.test(ua)) return "mac";
    if (/Linux|X11|CrOS/.test(ua)) return "linux";
    return null;
}

// Only Chromium exposes the CPU architecture; elsewhere x64 on Windows and Apple Silicon on macOS
// are the likely picks, and the package tables list the alternatives.
async function detectArm(platform) {
    const fallback = platform === "mac";
    if (!navigator.userAgentData || !navigator.userAgentData.getHighEntropyValues) return fallback;
    try {
        const { architecture } = await navigator.userAgentData.getHighEntropyValues(["architecture"]);
        return architecture ? architecture === "arm" : fallback;
    } catch (e) {
        return fallback;
    }
}

async function initDownloads() {
    const hero = document.getElementById("hero-download");
    const label = hero.querySelector(".label");
    const note = document.getElementById("hero-download-note");
    const platform = detectPlatform();

    if (platform === "linux") {
        label.textContent = "Install on Linux";
        return;
    }
    if (platform === null) return;

    const arm = await detectArm(platform);
    const download = DOWNLOADS[`${platform}-${arm ? "arm64" : "x64"}`];
    const href = DOWNLOAD_BASE + download.file;

    hero.href = href;
    label.textContent = download.label;
    note.querySelector(".detail").textContent = download.detail;
    note.hidden = false;
    document.getElementById(`download-${platform}`).href = href;
}

// Plugin store

function pluginTile(plugin) {
    const tile = el("a", "card plugin");
    tile.href = plugin.repository ? `https://github.com/${plugin.repository}` : "https://github.com/RadiatorTwo/LoupixDeck";

    const head = el("div", "plugin-head");
    const icon = el("span", "plugin-icon");
    const fallback = () => {
        icon.replaceChildren(el("span", "mdi", ICON_PUZZLE));
        icon.firstChild.setAttribute("aria-hidden", "true");
    };
    if (plugin.icon) {
        const img = el("img");
        img.src = plugin.icon;
        img.alt = "";
        img.loading = "lazy";
        img.width = 40;
        img.height = 40;
        img.addEventListener("error", fallback, { once: true });
        icon.append(img);
    } else {
        fallback();
    }

    const name = el("div", "plugin-name");
    name.append(el("h3", null, plugin.name));
    if (plugin.author) name.append(el("span", null, `by ${plugin.author}`));
    head.append(icon, name);

    const tags = el("div", "tags");
    for (const p of plugin.platforms || []) tags.append(el("span", "tag", p));

    tile.append(head, el("p", null, plugin.description || ""), tags);
    return tile;
}

function renderPlugins(plugins) {
    const grid = document.getElementById("plugin-grid");
    const filterBar = document.getElementById("plugin-filter");
    const supports = (plugin, platform) => (plugin.platforms || []).includes(platform);
    let current = "all";

    const tiles = plugins.map((p) => ({ plugin: p, node: pluginTile(p) }));
    const buttons = [];

    const apply = () => {
        const visible = tiles.filter((t) => current === "all" || supports(t.plugin, current));
        grid.replaceChildren(...visible.map((t) => t.node));
        for (const b of buttons) b.setAttribute("aria-pressed", b.dataset.platform === current ? "true" : "false");
    };

    for (const platform of ["all", ...PLATFORMS]) {
        const count = platform === "all" ? plugins.length : plugins.filter((p) => supports(p, platform)).length;
        if (count === 0) continue;
        const button = el("button", null, platform === "all" ? "All" : platform);
        button.type = "button";
        button.dataset.platform = platform;
        button.append(el("span", "count", String(count)));
        button.addEventListener("click", () => {
            current = platform;
            apply();
        });
        buttons.push(button);
    }

    filterBar.replaceChildren(...buttons);
    filterBar.hidden = false;
    document.getElementById("plugin-count").textContent = String(plugins.length);
    apply();
}

async function initPlugins() {
    const grid = document.getElementById("plugin-grid");
    try {
        const response = await fetch(PLUGIN_STORE_URL, { cache: "no-cache" });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const store = await response.json();
        const plugins = (store.plugins || [])
            .filter((p) => p && p.name)
            .sort((a, b) => a.name.localeCompare(b.name, "en", { sensitivity: "base" }));
        if (plugins.length === 0) throw new Error("empty store");
        renderPlugins(plugins);
    } catch (e) {
        const message = el("p", "plugin-status", "The plugin list could not be loaded. ");
        const link = el("a", null, "Browse the store index on GitHub");
        link.href = "https://github.com/RadiatorTwo/LoupixDeck/blob/master/plugin-store.json";
        message.append(link, document.createTextNode("."));
        grid.replaceChildren(message);
    }
}

initInstallTabs();
initCopyButton();
initDownloads();
initPlugins();
