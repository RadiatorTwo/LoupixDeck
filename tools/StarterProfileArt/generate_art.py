"""Generates the wallpapers and animations of the starter profiles (issue #301).

Everything is drawn from code, so the art carries no third-party license and can be
regenerated or tweaked at any time. Output goes to LoupixDeck/Assets/StarterProfiles/:

  wallpaper-<template>.png   480x270  main panel
  strip-<template>.png        60x270  side displays (Razer Stream Controller, Loupedeck Live / CT)
  anim-<name>.webp            animated WebP with alpha, looping

Requires Pillow (with WebP support) and NumPy:  python generate_art.py
"""

import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = Path(__file__).resolve().parents[2] / "LoupixDeck" / "Assets" / "StarterProfiles"
PANEL = (480, 270)
STRIP = (60, 270)
KEY = 90
FRAME_MS = 66
SUPERSAMPLE = 3


# ───────── Helpers ─────────

def rgb(hex_color):
    hex_color = hex_color.lstrip("#")
    return np.array([int(hex_color[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float32) / 255.0


def grid(w, h):
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    return xs, ys


def linear(w, h, start, end, angle_deg):
    """Gradient from start to end along angle_deg (0 = left to right, 90 = top to bottom)."""
    xs, ys = grid(w, h)
    a = math.radians(angle_deg)
    proj = xs * math.cos(a) + ys * math.sin(a)
    t = (proj - proj.min()) / max(1e-6, proj.max() - proj.min())
    return start[None, None, :] * (1 - t[..., None]) + end[None, None, :] * t[..., None]


def glow(img, cx, cy, radius, color, strength):
    h, w, _ = img.shape
    xs, ys = grid(w, h)
    d2 = (xs - cx) ** 2 + (ys - cy) ** 2
    img += color[None, None, :] * (strength * np.exp(-d2 / (radius * radius)))[..., None]


def band(img, center_fn, width, color, strength):
    """A soft horizontal ribbon whose centre follows center_fn(x)."""
    h, w, _ = img.shape
    xs, ys = grid(w, h)
    centre = center_fn(xs)
    img += color[None, None, :] * (strength * np.exp(-((ys - centre) ** 2) / (2 * width * width)))[..., None]


def vignette(img, amount):
    h, w, _ = img.shape
    xs, ys = grid(w, h)
    nx = (xs - w / 2) / (w / 2)
    ny = (ys - h / 2) / (h / 2)
    img *= (1 - amount * np.clip(nx * nx * 0.6 + ny * ny * 0.9, 0, 1))[..., None]


def bokeh(img, cx, cy, radius, color, strength):
    """A soft-edged disc, like an out-of-focus light."""
    h, w, _ = img.shape
    xs, ys = grid(w, h)
    d = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2)
    img += color[None, None, :] * (strength * np.clip((radius - d) / 2.5, 0, 1))[..., None]


def stars(img, count, seed, max_alpha=0.55):
    h, w, _ = img.shape
    rng = np.random.default_rng(seed)
    for _ in range(count):
        x, y = rng.integers(0, w), rng.integers(0, h)
        a = rng.uniform(0.15, max_alpha)
        img[y, x] = np.clip(img[y, x] + a, 0, 1)


def to_image(img):
    return Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8), "RGB")


# ───────── Wallpapers ─────────
# Kept dark on purpose: white icons and captions are drawn straight on top of them.

def starter(w, h):
    m = min(w, h)
    img = linear(w, h, rgb("#0A0F24"), rgb("#1A1442"), 35)
    glow(img, w * 0.15, h * 0.2, m * 0.55, rgb("#2F6BFF"), 0.30)
    glow(img, w * 0.85, h * 0.85, m * 0.6, rgb("#8A3CFF"), 0.26)
    rng = np.random.default_rng(7)
    for _ in range(max(6, w * h // 7000)):
        color = ["#2F6BFF", "#8A3CFF", "#29C7FF"][rng.integers(0, 3)]
        bokeh(img, rng.uniform(0, w), rng.uniform(0, h), rng.uniform(m * 0.04, m * 0.13), rgb(color),
              rng.uniform(0.05, 0.14))
    vignette(img, 0.35)
    return img


def tour(w, h):
    img = linear(w, h, rgb("#040A12"), rgb("#0A0F1F"), 90)
    stars(img, int(w * h / 900), seed=301)
    band(img, lambda x: h * 0.42 + h * 0.12 * np.sin(x / (w / 4.2) + 0.6), h * 0.09, rgb("#16E0B0"), 0.42)
    band(img, lambda x: h * 0.58 + h * 0.10 * np.sin(x / (w / 3.1) + 2.1), h * 0.07, rgb("#7A4DFF"), 0.38)
    band(img, lambda x: h * 0.30 + h * 0.08 * np.sin(x / (w / 5.5) + 4.0), h * 0.05, rgb("#2EA8FF"), 0.20)
    vignette(img, 0.4)
    return img


def media(w, h):
    img = linear(w, h, rgb("#14060F"), rgb("#1C0A1E"), 20)
    glow(img, w * 0.5, h * 1.05, min(w, h) * 0.75, rgb("#FF3D7F"), 0.25)
    for i, (color, amp, freq, phase) in enumerate([
        ("#FF4D8D", 0.16, 2.0, 0.0), ("#FF9A3C", 0.11, 3.1, 1.3), ("#B44DFF", 0.08, 4.4, 2.6)]):
        centre = lambda x, a=amp, f=freq, p=phase: h * 0.5 + h * a * np.sin(x / w * f * math.pi * 2 + p)
        band(img, centre, 1.4, rgb(color), 0.55)
        band(img, centre, 7.0, rgb(color), 0.12)
    vignette(img, 0.4)
    return img


def obs(w, h):
    img = linear(w, h, rgb("#0B0B0F"), rgb("#141018"), 60)
    m = min(w, h)
    glow(img, w * 0.95, h * 0.05, m * 0.8, rgb("#E5202E"), 0.28)
    glow(img, w * 0.05, h * 1.0, m * 0.6, rgb("#5A2BFF"), 0.12)
    xs, ys = grid(w, h)
    lines = ((xs.astype(int) % 30 == 0) | (ys.astype(int) % 30 == 0)).astype(np.float32)
    img += lines[..., None] * 0.035
    # Diagonal accent stripes through the lower left, like a broadcast lower third.
    diag = (xs + ys * 1.6) / 10.0
    stripes = ((diag.astype(int) % 3 == 0) & (ys > h * 0.62) & (xs < w * 0.55 + (h - ys))).astype(np.float32)
    img += stripes[..., None] * rgb("#E5202E")[None, None, :] * 0.10
    vignette(img, 0.35)
    return img


def resolve(w, h):
    m = min(w, h)
    img = linear(w, h, rgb("#06262C"), rgb("#33200C"), 0 if w > h else 90)
    img *= 0.85
    glow(img, w * 0.12, h * 0.5, m * 0.7, rgb("#12A5B5"), 0.16)
    glow(img, w * 0.9, h * 0.5, m * 0.7, rgb("#FF8A2A"), 0.16)
    vignette(img, 0.45)
    return img


def resolve_overlay(im):
    """Three faint colour wheels and film perforations along the edges."""
    w, h = im.size
    s = SUPERSAMPLE
    big = Image.new("RGBA", (w * s, h * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    if w > h:
        for i, color in enumerate([(18, 165, 181), (230, 230, 230), (255, 138, 42)]):
            cx, cy, r = w * (0.25 + 0.25 * i), h * 0.5, h * 0.24
            d.ellipse([(cx - r) * s, (cy - r) * s, (cx + r) * s, (cy + r) * s], outline=color + (40,), width=2 * s)
            d.ellipse([(cx - 3) * s, (cy - 3) * s, (cx + 3) * s, (cy + 3) * s], fill=color + (60,))
        holes = [(x, y) for x in range(8, w, 22) for y in (3, h - 11)]
        size = (12, 8)
    else:
        holes = [(x, y) for y in range(8, h, 22) for x in (3, w - 11)]
        size = (8, 12)
    for x, y in holes:
        d.rounded_rectangle([x * s, y * s, (x + size[0]) * s, (y + size[1]) * s], radius=2 * s, fill=(0, 0, 0, 150))
    return Image.alpha_composite(im.convert("RGBA"), big.resize((w, h), Image.LANCZOS)).convert("RGB")


def home_assistant(w, h):
    img = linear(w, h, rgb("#050E1A"), rgb("#0B2236"), 90)
    stars(img, int(w * h / 700), seed=8123, max_alpha=0.45)
    m = min(w, h)
    glow(img, w * 0.8, h * 0.15, m * 0.12, rgb("#DDE8FF"), 0.35)
    glow(img, w * 0.8, h * 0.15, m * 0.45, rgb("#41BDF5"), 0.10)
    vignette(img, 0.3)
    return img


def home_assistant_overlay(im):
    """Rooflines along the bottom with a few lit windows."""
    w, h = im.size
    big = Image.new("RGBA", (w * SUPERSAMPLE, h * SUPERSAMPLE), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    s = SUPERSAMPLE
    rng = np.random.default_rng(42)
    x = -10
    base = h * 0.92
    while x < w:
        bw = rng.integers(max(24, w // 12), max(40, w // 6))
        bh = rng.integers(int(h * 0.10), int(h * 0.22))
        roof = rng.integers(8, 18)
        poly = [(x, base), (x, base - bh), (x + bw / 2, base - bh - roof), (x + bw, base - bh), (x + bw, base)]
        d.polygon([(px * s, py * s) for px, py in poly], fill=(3, 9, 16, 255))
        for _ in range(rng.integers(0, 3)):
            wx = x + rng.integers(4, max(6, bw - 10))
            wy = base - rng.integers(6, max(8, bh - 6))
            d.rectangle([wx * s, wy * s, (wx + 5) * s, (wy + 5) * s], fill=(255, 183, 77, 230))
        x += bw + rng.integers(2, 10)
    d.rectangle([0, base * s, w * s, h * s], fill=(3, 9, 16, 255))
    small = big.resize((w, h), Image.LANCZOS)
    return Image.alpha_composite(im.convert("RGBA"), small).convert("RGB")


WALLPAPERS = {
    "starter": (starter, None),
    "feature-tour": (tour, None),
    "media": (media, None),
    "obs": (obs, None),
    "davinci-resolve": (resolve, resolve_overlay),
    "home-assistant": (home_assistant, home_assistant_overlay),
}


# ───────── Animations ─────────

def canvas(w, h):
    return Image.new("RGBA", (w * SUPERSAMPLE, h * SUPERSAMPLE), (0, 0, 0, 0))


def finish(big, w, h):
    return big.resize((w, h), Image.LANCZOS)


def lerp_color(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def equalizer(frame, count):
    """Five bars bouncing at their own rhythm, teal to lime."""
    big = canvas(KEY, KEY)
    d = ImageDraw.Draw(big)
    s = SUPERSAMPLE
    bars, gap, bottom, top = 5, 5, 72, 18
    bw = (KEY - 2 * 16 - (bars - 1) * gap) / bars
    t = frame / count * 2 * math.pi
    for i in range(bars):
        level = 0.5 + 0.5 * math.sin(t * (1 + (i % 3)) + i * 1.7)
        level = 0.25 + 0.75 * level
        x0 = 16 + i * (bw + gap)
        y0 = bottom - (bottom - top) * level
        color = lerp_color((22, 224, 176), (182, 255, 60), i / (bars - 1))
        d.rounded_rectangle([x0 * s, y0 * s, (x0 + bw) * s, bottom * s], radius=bw * s / 2, fill=color + (255,))
    return finish(big, KEY, KEY)


def orbit(frame, count):
    """Three dots circling the centre, each with a fading trail."""
    big = canvas(KEY, KEY)
    d = ImageDraw.Draw(big)
    s = SUPERSAMPLE
    cx = cy = KEY / 2
    colors = [(47, 107, 255), (138, 60, 255), (41, 199, 255)]
    for k, color in enumerate(colors):
        radius = 18 + k * 9
        speed = 1 + k * 0.5 if k != 1 else -1.5
        base = frame / count * 2 * math.pi * speed + k * 2.1
        for trail in range(10):
            a = base - trail * 0.12 * math.copysign(1, speed)
            px, py = cx + radius * math.cos(a), cy + radius * math.sin(a)
            r = 5.0 - trail * 0.35
            alpha = int(255 * (1 - trail / 10) ** 1.6)
            d.ellipse([(px - r) * s, (py - r) * s, (px + r) * s, (py + r) * s], fill=color + (alpha,))
    d.ellipse([(cx - 4) * s, (cy - 4) * s, (cx + 4) * s, (cy + 4) * s], fill=(255, 255, 255, 220))
    return finish(big, KEY, KEY)


def pulse(frame, count):
    """Rings expanding out of a glowing core."""
    big = canvas(KEY, KEY)
    d = ImageDraw.Draw(big)
    s = SUPERSAMPLE
    c = KEY / 2
    for k in range(3):
        t = (frame / count + k / 3) % 1.0
        r = 10 + 32 * t
        alpha = int(255 * (1 - t) ** 1.5)
        width = max(1, int((4 - 2.5 * t) * s))
        d.ellipse([(c - r) * s, (c - r) * s, (c + r) * s, (c + r) * s], outline=(255, 77, 141, alpha), width=width)
    core = 9 + 1.5 * math.sin(frame / count * 2 * math.pi * 2)
    d.ellipse([(c - core) * s, (c - core) * s, (c + core) * s, (c + core) * s], fill=(255, 154, 60, 255))
    return finish(big, KEY, KEY)


def strip_flow(frame, count):
    """Colour blobs drifting down the side display; the loop wraps seamlessly."""
    w, h = STRIP
    img = linear(w, h, rgb("#050A14"), rgb("#0A0F1F"), 90)
    shift = frame / count * h
    for i, color in enumerate(["#16E0B0", "#7A4DFF", "#2EA8FF"]):
        y = (i * h / 3 + shift) % h
        for wrap in (-h, 0, h):
            glow(img, w * (0.3 + 0.2 * i), y + wrap, 34, rgb(color), 0.55)
    return to_image(img).convert("RGBA")


ANIMATIONS = {
    "equalizer": (equalizer, 24),
    "orbit": (orbit, 36),
    "pulse": (pulse, 30),
    "strip-flow": (strip_flow, 40),
}


def main():
    OUT.mkdir(parents=True, exist_ok=True)

    for name, (render, overlay) in WALLPAPERS.items():
        for prefix, (w, h) in (("wallpaper", PANEL), ("strip", STRIP)):
            # Waves would be squeezed into 60 px across, so the strip gets them running lengthwise.
            im = to_image(render(h, w)).rotate(90, expand=True) if render is media and w < h else to_image(render(w, h))
            if overlay:
                im = overlay(im)
            path = OUT / f"{prefix}-{name}.png"
            im.save(path, optimize=True)
            print(f"{path.name}: {path.stat().st_size // 1024} KB")

    for name, (render, count) in ANIMATIONS.items():
        frames = [render(i, count) for i in range(count)]
        path = OUT / f"anim-{name}.webp"
        frames[0].save(path, save_all=True, append_images=frames[1:], duration=FRAME_MS, loop=0,
                       lossless=False, quality=90, method=6)
        print(f"{path.name}: {path.stat().st_size // 1024} KB, {count} frames")


if __name__ == "__main__":
    main()
