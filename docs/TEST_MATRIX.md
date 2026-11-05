# Testing the add-on against a matrix of exports

`Tools/verify_export_matrix.py` shades a spread of exports - different settings, both characters, files from the
old viewer - each in a fresh scene, and reports what came out. Run it before a shading change ships: a model the
add-on quietly does less for shows up beside the ones it handles fully.

## Running it

```powershell
# a folder (recursively), the shader to shade with, and a report path
blender -b --factory-startup --python Tools\verify_export_matrix.py -- C:\tmp\matrix_report.txt umashader-0.2.6 C:\tmp\matrix

# or single files
blender -b --factory-startup --python Tools\verify_export_matrix.py -- C:\tmp\m.txt spork-1.0 C:\tmp\a.pmx C:\tmp\b.pmx
```

Second argument is the shader (`umashader-0.2.6` or `spork-1.0`); run a folder twice to cover both.

## What it reports, per file

| line | what to look for |
| --- | --- |
| `imported: N mesh(es), N material(s), N morph(s), metadata tokens: yes/no` | `no (legacy)` means a pre-metadata export (the old viewer) - expected there, and the rest should still work |
| `shading: {'FINISHED'}` | anything else, or `SHADING FAILED`, is the thing to chase |
| `decals: N (multiply using the shipped blush: yes)` | the blush should stand in for the cheek only |
| the per-decal list | one line per decal copy: its map and its `CLIP`/`REPEAT` |
| `spinning decals on CLIP:` | should list the `@_@` material of **both** eyes (`…000` and `…000.001`) |
| `weather: N wet image(s) bound, N wet slot(s) mirrored, N dirt map(s)` | zeros are correct where `Config.PmxTextureVariants` is false; with it on, the body should have four twins and the face two |
| `eye drivers: 11` | the eye knobs resolved; 0 means the eye step did not land |
| `outline shell present: yes` | the outline modifier was built |

## The matrix

Two characters from the current viewer, six settings each, plus the old viewer's own exports of the same characters:

| # | export | what it exercises |
| --- | --- | --- |
| 1 | defaults | the normal path: metadata, decals, and whatever `Config.PmxTextureVariants` is set to |
| 2 | `-NoExtraMorphs` | no decal parts at all: the decals list should be empty and morph count lower |
| 3 | `-TextureVariants` | the flag only forces the key on; identical to row 1 when it is already true |
| 4 | `-MorphNameMode 0` (tagged) | the add-on must cope with the tagged spelling it does not ask for |
| 5 | `-PlainMaterials` | no uma shader data in the material comments: the add-on should shade from the manifest alone |
| 6 | `-APose` | the A-pose rest pose, which changes bone rolls and nothing the add-on reads |
| 7 | old viewer export | no metadata tokens; the add-on must still shade, decals included, and report `legacy` |

`Config.PmxTextureVariants` is off by default and `-TextureVariants` only sets it true, so a variant-carrying export
needs the flag (or the config) on. A costume with no twins should report zeros rather than failures, so one of the
six can be that costume.

## Measured: the old viewer's exports against ours

Four characters (1001, 1004, 1065, 1097, first costume) from both viewers, read with `Tools/pmx_inspect.py` and
shaded with `umashader-0.2.6`. The pattern is identical on all four, so one row of numbers stands for the set.

| | old viewer | this fork |
| --- | --- | --- |
| textures | 22-23 | 31-32 |
| materials | **8** | **27** |
| vertices | 13,968 | 14,492 |
| morphs | 240 (96 vertex / 56 bone / 56 UV / 32 material) | 205 (48 / 56 / 56 / 45) |
| morph names | tagged (`EyeBrow_1_R(WaraiA)[M_Face]`) | unified (`Brow_WaraiA_R`) |
| rest pose | T-pose | A-pose (76 bones move) |
| metadata tokens | none | `export settings:`, `object morphs:`, `morph=`, `wet=`/`dirt=` |

The morph count differs by two effects that cancel: this fork prunes the vertex morphs that move no vertex (48
here) and adds the thirteen deploy morphs. Both spellings resolve, and `pmx_inspect check` confirms the eye contract
in both files - the old one carries the tagged spelling, which the *stock* add-on needs and this one also accepts.

Through the pipeline, the difference is what the material split buys:

| | old viewer | this fork |
| --- | --- | --- |
| shading finishes | yes | yes |
| eye drivers | 11 | 11 |
| outline shell | yes | yes |
| decals recognised | **0** | 45 (blush stands in for the multiply sheet) |
| spinning eye on CLIP | none | both (`…000`, `…000.001`) |
| weather bound | 0 | 48 wet image(s), 24 mirrored, 15 dirt |

Renders identically: **no** for the old viewer - 22.6% of a face view differs, and the old one is darker
(0.266/0.233/0.279 against 0.468/0.393/0.500 in the differing pixels).

An old-viewer model is supported for the body, the eyes and the outline, and cannot be for the decals: with eight
materials the eye manga and the tear sheets share the body's materials, so nothing can be styled separately. That
is a property of the file - export with this fork.

## Measured: the settings rows (1001, first costume)

| setting | materials | morphs | decals | spin | weather | eye drivers |
| --- | --- | --- | --- | --- | --- | --- |
| defaults (variants on) | 27 | 205 | 45 | both eyes | 48 / 24 / 15 | 11 |
| `-TextureVariants` | 27 | 205 | 45 | both eyes | 48 / 24 / 15 | 11 |
| `-PlainMaterials` | 27 | 205 | 45 | both eyes | 48 / 24 / 15 | 11 |
| `-MorphNameMode 0` (tagged) | 27 | 253 | 45 | both eyes | 48 / 24 / 15 | 11 |
| `-NoExtraMorphs` | **8** | 192 | **0** | none | 48 / 24 / 15 | 11 |

The `defaults` row was measured with `Config.PmxTextureVariants` on; with it off (the shipped default) the weather
column is `0 / 0 / 0` and nothing else changes.

`-PlainMaterials` costs nothing: the add-on shades from the manifest, not the material's own shader data.
`-NoExtraMorphs` loses what an old-viewer file loses (no decal materials, no spin), since the extra-morphs pass is
what splits the decals into materials of their own. Tagged names make 253 morphs and still resolve.

## Reading the result

`SHADING FAILED`, `no mesh imported`, `eye drivers: 0` and `spinning decals on CLIP: none found` are real defects
for that combination. `decals: 0` is correct only for `-NoExtraMorphs` and for a model whose morphs never had
decals; every other difference is compared against row 1.
