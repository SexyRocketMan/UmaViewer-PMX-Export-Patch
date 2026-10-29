# Testing the add-on against a matrix of exports

The last thing before shipping: shade a spread of exports - different settings, both characters, and models from the
old viewer - and check that each one gets what it is supposed to get. The harness is
`Tools/verify_export_matrix.py`; it shades each file in a fresh scene and reports what came out, so a model the
add-on quietly does less for is visible next to the ones it handles fully.

## Running it

```powershell
# a folder (recursively), the shader to shade with, and a report path
blender -b --factory-startup --python Tools\verify_export_matrix.py -- C:\tmp\matrix_report.txt umashader-0.2.6 C:\tmp\matrix

# or single files
blender -b --factory-startup --python Tools\verify_export_matrix.py -- C:\tmp\m.txt spork-1.0 C:\tmp\a.pmx C:\tmp\b.pmx
```

The scene's shader is chosen by the second argument (`umashader-0.2.6` or `spork-1.0`), so the same folder can be
run twice to cover both.

## What it reports, per file

| line | what to look for |
| --- | --- |
| `imported: … morph(s), metadata tokens: yes/no` | `no (legacy)` means a pre-metadata export (the old viewer) - expected there, and the rest should still work |
| `shading: {'FINISHED'}` | anything else, or `SHADING FAILED`, is the thing to chase |
| `decals: N (multiply using the shipped blush: yes)` | the blush should stand in for the cheek only |
| the per-decal list | one line per decal copy: its map and its `CLIP`/`REPEAT` |
| `spinning decals on CLIP:` | should list the `@_@` material of **both** eyes (`…000` and `…000.001`) |
| `weather: N wet image(s), M mirrored, D dirt map(s)` | zeros are correct for an export without `-TextureVariants`; with it, the body should have four twins and the face two |
| `eye drivers: 11` | the eye knobs resolved; 0 means the eye step did not land |
| `outline shell present: yes` | the outline modifier was built |

## The matrix

Two characters from the current viewer, six settings each, plus the old viewer's own exports of the same characters:

| # | export | what it exercises |
| --- | --- | --- |
| 1 | defaults | the normal path: metadata, decals, weather off by default |
| 2 | `-NoExtraMorphs` | no decal parts at all: the decals list should be empty and morph count lower |
| 3 | `-TextureVariants` | wet/dirt sets present: weather counts non-zero, face's base/ctrl mirrored |
| 4 | `-MorphNameMode 0` (tagged) | the add-on must cope with the tagged spelling it does not ask for |
| 5 | `-PlainMaterials` | no uma shader data in the material comments: the add-on should shade from the manifest alone |
| 6 | `-APose` | the A-pose rest pose, which changes bone rolls and nothing the add-on reads |
| 7 | old viewer export | no metadata tokens; the add-on must still shade, decals included, and report `legacy` |

`-TextureVariants` on a character whose costume has no twins should report zeros rather than failures, so one of the
six can be that costume if one exists.

## Reading the result

Anything that says `SHADING FAILED`, `no mesh imported`, `eye drivers: 0`, or `spinning decals on CLIP: none found`
is a real defect for that combination. `decals: 0` is only correct for `-NoExtraMorphs` and for a model whose morphs
never had decals. Everything else - a different count, a missing wet twin - is worth comparing against row 1: the
numbers should differ *only* in the way the setting explains.
