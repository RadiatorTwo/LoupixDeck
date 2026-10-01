# StarterProfileArt

Generates the wallpapers and animations of the starter profiles (issue #301). All art is
drawn from code, so it carries no third-party license and can be changed and regenerated
at any time.

```bash
pip install pillow numpy   # Pillow needs WebP support (the standard wheels have it)
python generate_art.py
```

Output goes to `LoupixDeck/Assets/StarterProfiles/` and ships with the app as Avalonia
resources. Creating a starter profile copies the files it uses into the asset store.

| File                        | Size    | Used for                                             |
|-----------------------------|---------|------------------------------------------------------|
| `wallpaper-<template>.png`  | 480×270 | main panel of every page and folder                  |
| `strip-<template>.png`      | 60×270  | side displays (Razer Stream Controller, Live, CT)    |
| `anim-<name>.webp`          | 90×90   | animated keys of the feature tour                    |
| `anim-strip-flow.webp`      | 60×270  | animated side displays of the feature tour's dials   |

`<template>` is the template's id (`StarterProfileTemplate.Id`). The wallpapers are kept
dark on purpose: the keys draw white icons and captions straight on top of them.