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

## Measured: the old viewer's exports against ours

Four characters (1001, 1004, 1065, 1097, first costume), exported from both viewers, compared with
`Tools/pmx_inspect.py` and then shaded with `umashader-0.2.6`. The pattern is identical on all four, so one row of
numbers stands for the set.

| | old viewer | this fork |
| --- | --- | --- |
| textures | 22-23 | 31-32 |
| materials | **8** | **27** |
| vertices | 13,968 | 14,492 |
| morphs | 240 (96 vertex / 56 bone / 56 UV / 32 material) | 205 (48 / 56 / 56 / 45) |
| morph names | tagged (`EyeBrow_1_R(WaraiA)[M_Face]`) | unified (`Brow_WaraiA_R`) |
| rest pose | T-pose | A-pose (76 bones move) |
| metadata tokens | none | `export settings:`, `object morphs:`, `morph=`, `wet=`/`dirt=` |

The morph *count* differs for two reasons that cancel out: this fork prunes the vertex morphs that move no vertex
(48 of them here) and adds the thirteen deploy morphs. The name difference is the naming work - both spellings are
readable by this add-on's resolver, and `pmx_inspect check` confirms the eye contract holds in both (the old file
carries the tagged spelling, which the *stock* add-on needs and this one also accepts).

**Through the pipeline** the difference is what the material split buys:

| | old viewer | this fork |
| --- | --- | --- |
| shading finishes | yes | yes |
| eye drivers | 11 | 11 |
| outline shell | yes | yes |
| decals recognised | **0** | 45 (blush stands in for the multiply sheet) |
| spinning eye on CLIP | none | both (`…000`, `…000.001`) |
| weather bound | 0 | 48 wet image(s), 24 mirrored, 15 dirt |
| renders identically | **no** - 22.6% of a face view differs, and the old one is darker (0.266/0.233/0.279 against 0.468/0.393/0.500 in the differing pixels) |

So an old-viewer model is *supported* for the body, the eyes and the outline, and cannot be for the decals: with
eight materials the eye manga and the tear sheets share the body's materials, so there is nothing to style
separately - the metadata could name them, but no such material exists. That is a property of the file, not of the
add-on, and the fix is "export with this fork" rather than a fallback here.

## Measured: the settings rows (1001, first costume)

| setting | materials | morphs | decals | spin | weather | eye drivers |
| --- | --- | --- | --- | --- | --- | --- |
| defaults (variants on) | 27 | 205 | 45 | both eyes | 48 / 24 / 15 | 11 |
| `-TextureVariants` | 27 | 205 | 45 | both eyes | 48 / 24 / 15 | 11 |
| `-PlainMaterials` | 27 | 205 | 45 | both eyes | 48 / 24 / 15 | 11 |
| `-MorphNameMode 0` (tagged) | 27 | 253 | 45 | both eyes | 48 / 24 / 15 | 11 |
| `-NoExtraMorphs` | **8** | 192 | **0** | none | 48 / 24 / 15 | 11 |

Two things that table settles: `-PlainMaterials` costs nothing (the add-on shades from the manifest, not from the
material's own shader data), and `-NoExtraMorphs` is not merely "fewer morphs" - it is the same loss as an old-viewer
file (no decal materials, no spin), because the extra-morphs pass is what splits the decals into materials of their
own. Tagged names make 253 morphs (the tagged spelling beside the short one) and still resolve.

## Reading the result

Anything that says `SHADING FAILED`, `no mesh imported`, `eye drivers: 0`, or `spinning decals on CLIP: none found`
is a real defect for that combination. `decals: 0` is only correct for `-NoExtraMorphs` and for a model whose morphs
never had decals. Everything else - a different count, a missing wet twin - is worth comparing against row 1: the
numbers should differ *only* in the way the setting explains.
