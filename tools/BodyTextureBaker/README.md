# BodyTextureBaker

Regenerates the Loupedeck Live S device body image
(`Assets/loupedeck-gehaeuse.svg`) from a matte source texture.

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
# Regenerate with the committed defaults
dotnet run --project tools/BodyTextureBaker

# Tweak the look
dotnet run --project tools/BodyTextureBaker -- --grain 0.35 --dark 0.55
```

After running, rebuild the app to pick up the new `Assets/loupedeck-gehaeuse.svg`.

## Options

| Option     | Default | Meaning                                                  |
|------------|---------|----------------------------------------------------------|
| `--input`  | `texture-no-light.png` (next to the tool) | Source matte texture.   |
| `--output` | `<repo>/Assets/loupedeck-gehaeuse.svg`    | SVG to write.           |
| `--width`  | `1100`  | Baked image width in px (height derived from the 820×460 body). |
| `--dark`   | `0.6`   | Brightness multiplier (lower = darker).                  |
| `--grain`  | `0.5`   | Grain contrast (1 = full texture grain, 0 = smooth).     |

The defaults reproduce the committed SVG.

## Changing the texture

Drop a replacement `texture-no-light.png` into this folder (a flat, evenly-lit
matte texture works best — the lighting is added by the baker, so the source
should **not** have baked-in lighting), then re-run the tool. `texture.png` is
the original variant that already has lighting baked in; it is kept here for
reference only and is not used by the default pipeline.

The source textures live here (not in `Assets/`) on purpose: everything under
`Assets/**` is embedded into the app binary as an Avalonia resource, and the
multi-megabyte source PNGs do not belong in the shipped app — only the small
baked SVG does.
