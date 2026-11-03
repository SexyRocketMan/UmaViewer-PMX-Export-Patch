# Dumping every prop

`-DumpProps <dir>` writes every prop in the manifest into its own folder, with an `index.json` describing the lot.
It is the dump a picker or a web viewer reads: one model per folder with its own textures, so a page can fetch
one model at a time, and an index so it never has to walk the folder tree.

Why it exists: the props have no names in the game's data ([`PROP_LABELS.md`](PROP_LABELS.md)), and the in-app
grid built to work around that was not well received. A dump is the alternative both remaining routes need -
a browsable model pack, or a web viewer that renders PMXs in the browser.

## Running it

```powershell
# everything: 1 377 models, roughly 180 MB, 15-25 minutes in one editor run
./Tools/headless_export.ps1 -DumpProps D:/umaprops

# one family, or a few models, for a try
./Tools/headless_export.ps1 -DumpProps D:/umaprops -DumpPropsFilter rich_prop
./Tools/headless_export.ps1 -DumpProps D:/umaprops -DumpPropsFilter toon_prop -DumpPropsCount 12
```

Props whose bundles are not in the local cache yet are downloaded as they are loaded
(`Config.DownloadMissingResources`); one that cannot be is recorded in the index with a `failure` and the run
carries on. A prop that is already on disk is skipped, so re-running the same command after an interruption
resumes rather than starting again, and the index keeps what the earlier run recorded.

## What it writes

```
D:/umaprops/
  index.json
  prop/prop1001_00/pfb_chr_prop1001_00.pmx
  prop/prop1001_00/Texture2D/tex_chr_prop1001_00_diff.png
  toonprop/toon_prop1001_00/pfb_toon_prop1001_00.pmx
  toonprop/toon_prop1001_00/Texture2D/…
  richprop/rich_prop1005_00/pfb_rich_prop1005_00.pmx
  richprop/rich_prop1005_00/Texture2D/…
```

`<family>/<prop folder>/` is the game's own folder under `3d/chara`, so a model's folder is its manifest identity
with the path stripped off. `Texture2D/` is the PMX's texture folder, written by the exporter beside the model.

## index.json

```json
{
  "generated": "2026-11-03T18:51:02.1234567Z",
  "root": "D:/umaprops",
  "count": 1377,
  "models": [
    {
      "name": "pfb_toon_prop1001_00",
      "family": "toonprop",
      "source": "3d/chara/toonprop/toon_prop1001_00/pfb_toon_prop1001_00",
      "model": "toonprop/toon_prop1001_00/pfb_toon_prop1001_00.pmx",
      "textures": "toonprop/toon_prop1001_00/Texture2D",
      "bytes": 94208,
      "bytesTotal": 104857,
      "bones": 18,
      "handBones": 16,
      "size": "0.360,0.094,0.118",
      "failure": ""
    }
  ]
}
```

`model`, `textures` and `root` use forward slashes, so a page can concatenate them without caring which machine
made the dump. `bones` and `handBones` are counted from the model's skin - a prop with hand bones is one meant to
be held, which is the only classification the game's data supports (`Tools/prop_rig_names.py` measures it for a
whole family). `size` is the renderer bounds in world units, which is what a viewer needs to frame a thumbnail:
props run from 0.07 units across to 5.3.

`count` is how many models the run was asked for; `models` is what has been written so far, so an index from an
interrupted dump still describes exactly what is on disk.

## What it costs

Measured on this machine, one editor run each:

| family | models | size on disk | per model |
|---|---|---|---|
| `prop` | 6 of 1 229 | 0.59 MB | ~100 KB (24 KB PMX + 3 maps) |
| `toonprop` | 12 of 145 | 6.08 MB | ~0.5 MB |
| `richprop` | 3 of 3 | 8.14 MB | ~2.8 MB |

Extrapolated to everything: **≈ 180 MB**, mostly textures, in **15-25 minutes** (about 0.5 s per model once the
editor is up). The variation is worth knowing before choosing a host: the three `richprop` models alone are
nearly 10 MB of it.

## What can read it

* **A web viewer.** [`babylon-mmd`](https://noname0310.github.io/babylon-mmd/) loads PMX (and PMD/VMD), with
  morphs, IK and WASM Bullet physics on WebGL 1/2 and WebGPU; three.js's `MMDLoader` and
  [`three-mmd-loader`](https://github.com/yohawing/three-mmd-loader) are lighter alternatives. A static page can
  fetch `index.json`, show a grid, and load one PMX per card - and cache a thumbnail per model by drawing it once
  into a canvas, so no images had to be baked.
* **A picker in the app.** The same index is enough to show a list with a name, a size and "held or not" beside
  every prop, which is what the rejected grid was trying to guess at from pixels.

Redistribution is a separate question from the mechanics: the dump *is* the game's art. Local use, or a pack that
users generate themselves from their own install, is a different thing from a public mirror.
