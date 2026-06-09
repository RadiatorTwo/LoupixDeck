# BodyTextureBaker

Regenerates the Loupedeck Live S device body images from matte source textures —
one per app theme:

| Variant | Source texture          | Output SVG                            |
|---------|-------------------------|---------------------------------------|
| dark    | `texture-no-light.png`  | `Assets/loupedeck-gehaeuse.svg`       |
| light   | `texture-light.png`     | `Assets/loupedeck-gehaeuse-light.svg` |

The device layout (`Views/Devices/LoupedeckLiveSLayout.axaml`) binds the body SVG
to the theme-aware `DeviceBodySvgPath` resource (defined per `ThemeVariant` in
`App.axaml`), so Light mode shows the light body and Dark mode the dark one.

## Why baking instead of SVG gradients

Skia renders SVG radial gradients in 8-bit **without dithering**, so a smooth
sheen/vignette gradient bands visibly on the device body. This tool bakes the
whole surface — dark texture **plus** lighting — into one 8-bit grayscale image
using **Floyd-Steinberg** error diffusion. The dithered image is the band-free
representation and stays band-free at every display scale (including HiDPI),
because up-scaling averages the dither back toward the true value. The output SVG
contains only that baked image (clipped to the rounded body, with an outer drop
shadow) and **no SVG gradients**, so nothing can band.

## Usage

Run from anywhere in the repo (the tool locates the repo root via
`LoupixDeck.csproj`):

```bash
# Regenerate both variants with the committed defaults
dotnet run --project tools/BodyTextureBaker

# Bake just one variant
dotnet run --project tools/BodyTextureBaker -- --variant light

# Tweak the look of a single variant
dotnet run --project tools/BodyTextureBaker -- --variant dark --grain 0.35 --dark 0.55
```

After running, rebuild the app to pick up the new `Assets/loupedeck-gehaeuse*.svg`.

## Options

| Option      | Default            | Meaning                                                  |
|-------------|--------------------|----------------------------------------------------------|
| `--variant` | `both`             | Which variant(s) to bake: `dark`, `light`, or `both`.    |
| `--input`   | per-variant texture (next to the tool) | Source matte texture (needs a single `--variant`). |
| `--output`  | per-variant SVG under `<repo>/Assets`  | SVG to write (needs a single `--variant`).         |
| `--width`   | `1100`             | Baked image width in px (height derived from the 750×420 body). |
| `--dark`    | `0.6` dark / `0.92` light | Brightness multiplier (lower = darker).           |
| `--grain`   | `0.5`              | Grain contrast (1 = full texture grain, 0 = smooth).     |

The defaults reproduce the committed SVGs. The Light variant keeps the white
texture bright (`--dark 0.92`) and internally softens the vignette/sheen so the
light body does not read as dirty or blown out; the Dark variant uses the matte
black texture with the original lighting.

## Changing the texture

Drop a replacement `texture-no-light.png` (dark body) or `texture-light.png`
(light body) into this folder — a flat, evenly-lit matte texture works best, as
the lighting is added by the baker, so the source should **not** have baked-in
lighting — then re-run the tool. `texture.png` is the original dark variant that
already has lighting baked in; it is kept here for reference only and is not used
by the default pipeline.

The source textures live here (not in `Assets/`) on purpose: everything under
`Assets/**` is embedded into the app binary as an Avalonia resource, and the
multi-megabyte source PNGs do not belong in the shipped app — only the small
baked SVG does.
