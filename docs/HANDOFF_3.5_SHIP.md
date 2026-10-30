# Handoff — 3.5 and add-on 2.35.18, shipped

Everything below is state at hand-off. Nothing is in flight: both repositories are committed, the two release zips were
built from `HEAD`, install-tested, and hashed. A new session can start by reading this file, then
`docs/HANDOFF_SHADER_LIBRARY.md` §7 for the shader libraries.

## What shipped

`C:\tmp\release\ship\`

| file | bytes | sha256 (first 16) |
|---|---|---|
| `AgemaViewer-3.5-win64.zip` | 49,503,547 | `4ADC62393B3A0ED8…` |
| `uma_addon-2.35.18.zip` | 6,701,208 | `7D6DBE8B410AE037…` |
| `RELEASE.md` | — | install steps, what each zip is, what to expect |

`RELEASE.md` is `docs/RELEASE_3.5.md` in this repository. The viewer zip was built from the tree at `1f0e129` and
smoke-booted from its own extracted copy; it carries 193 entries and ships `Config.json` with extra morphs on, texture
variants **off**, A-pose on, unified morph names. The add-on zip is 61 entries, no `.bak`/`.blend1`/`.pyc`, manifest
`2.35.18`, install-tested with Blender's own installer (`STATUS Installed`).

**The user has since re-packaged the ship folder themselves** (`AgemaViewer/` extracted, `exports/`, and an
`AgemaViewer.zip` of 45.03 MB). Those are theirs — do not overwrite or delete them; the two zips named above and their
hashes are what `RELEASE.md` describes.

## Repositories

| | path | HEAD |
|---|---|---|
| exporter / viewer | `C:\Users\user\Documents\dev\UmaViewer-PMX-Export-Patch` | `c448406` |
| Blender add-on | `%APPDATA%\Blender Foundation\Blender\5.2\extensions\user_default\uma_addon` (the installed extension **is** the repo) | `92e7d03` |

Working-tree notes, both theirs and deliberate:

* the exporter has an **uncommitted** `Assets/Scripts/Config.cs`: `PmxTextureVariants = false` → **`true`**. Their
  preference for local exports; the *shipped* `Config.json` still says false. Commit or revert as they decide.
* `Assets/Scenes/Version2.unity` shows as modified but diffs clean (line endings).
* **untracked files** (~55, none of them in either zip — they are outside `uma_addon/` and outside the Unity build):
  * theirs: `Program.cs`, `Config.json.bak_laxmi_pipeline`, `Assets/Editor/UmaTearWatch.cs.meta`,
    `property_metadata_dirty_check.json`;
  * scratch from earlier debugging in this project: `Tools/probe_*.py` (≈45 of them), `Tools/verify_donor_count.py`,
    `Tools/_apply_addon_scroll.py`, `Tools/_decal_*.png`. These were each written to answer one question and are mostly
    superseded by the committed `verify_*`/`port_*`/`spork_*` tools; the next session should decide commit-or-delete
    rather than leave them as permanent noise. Nothing depends on them.

## What this session did

The brief was "ship it", and it turned into five rounds of the user finding real defects in the Spork port. All five
are fixed, measured, and written up in `HANDOFF_SHADER_LIBRARY.md` §7:

1. **The Spork library was a revision behind.** Ours was his 26-node / 4-socket `Simple Diff + Shaded`; his file today
   is 51 nodes / 8 sockets (`Base`, `Highlight Color`, `Light Intensity`, `Colored Light`, `Shadow Output`,
   transparency inside the group) plus `Simple Diff + Shaded FACE` (34 nodes) for faces. Ported by
   `Tools/port_spork_group.py`; the FACE variant became a `face` role with its own master `Spork Shader Face`. Proof it
   is his maths: his group and the ported one, identical inputs, `Dirt` 0 → **zero differing pixels**.
2. **Spork had no `decal` role**, so its decals stayed mmd_tools' lit materials (blush, tears, manga eyes). Measured 45
   decal copies under Looper, **0** under Spork; now 45 under both (`Tools/add_spork_decal.py`, plus the decal opacity
   rows and the shipped blush).
3. **See-through / "inverted normals"**: the masters carried his hair wrapper's render settings (`BLEND` / `BLENDED` /
   `show_transparent_back`), which puts every copy in EEVEE's blended pass, where depth is not written and a
   character's two coplanar body slots composite in arbitrary order. Harmless before the port (flat-colour group), fatal
   after it (the group mixes a Transparent BSDF in). Measured against the same materials forced `OPAQUE`: blended is
   wrong on 54.8% of the silhouette on 1063 / 57.7% on 1001; the Looper masters' setting (`HASHED` / `DITHERED` / no
   transparent back) is wrong on **0 pixels** (`Tools/spork_render_settings.py`).
4. **Dirt was mixed, not multiplied** (`lerp(colour, tint, f)` instead of `colour * lerp(1, tint, f)`): +7.39/255
   brighter where it should be −1.66 darker. And **the face's dirt sat on a branch the FACE group does not always
   output** (`Colored Light` can route round the colour taps), so it now multiplies the *end* of that group's colour
   path, once (`Tools/spork_dirt_fix.py`). Face dirt masked by a white mask: 6.2% of a close-up moves — the export's own
   face mask is nearly empty (mean 0.014), which is data, not wiring.
5. **The face had no drivers at all**: `shader_nodes` matches a copy's group name against the manifest's `group_hints`
   *exactly*, and the FACE group is a different name. Measured 155 drivers over 23 copies, **0 on any face copy**. Both
   group names are declared now, the `Rain` row lost its `"material": "default"` (the face has the same `Dry/Wet` node),
   and `hook` asks each role's own master what it offers before reporting a socket missing (`master_sockets`).
   After: **176 drivers** (face 6×10, brows 3×14, default 12×14, eye 3×11, decals 45×5), `missing: none`, `verify()`
   clean on 89 values.

Also: his three new light sockets are panel rows now (`spork_highlight_color`, `spork_light_intensity`,
`spork_colored_light`, section `Light`); the user's own graph clean-up inside the shipped library was committed as
theirs (a second `Group Input` node, wiring unchanged — 68 edges before and after); and the verifier stopped counting UI
nodes as maths.

## Tools, and what each one proves

In `Tools/` (all headless, all measurement-first):

| tool | what it does |
|---|---|
| `port_spork_group.py` | appends his two groups into the library, injects the dirt chain (MULTIPLY taps), rewires the masters, builds `Spork Shader Face` |
| `spork_dirt_fix.py` | dirt taps → MULTIPLY; moves the FACE group's dirt to the end of its colour path (the correction to the port above) |
| `spork_render_settings.py` | the three shader masters → `HASHED`/`DITHERED`/no transparent back, i.e. the Looper library's depth-writing setting |
| `add_spork_decal.py` | appends `Uma Decal` into the Spork library, asserts its rest state (opacity 1, white tint) |
| `verify_spork_library.py` | the regression net: manifest rows/roles/hints, both groups' maths and wiring, master settings, the face role's drivers, weather slots. **Run this after any library or manifest change** |
| `verify_export_matrix.py` | shades real exports and reports materials, decals (with map + extension), weather counts, eye drivers, outline |
| `pmx_inspect.py` | `summary` / `diff` / `check` on a `.pmx` |

Invocation (Blender 5.2):

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background --factory-startup `
  --python Tools\verify_spork_library.py -- C:\tmp\out.txt
```

Viewer/export side: `Tools\headless_export.ps1 -Char 1001 -Costume 00 -Out <path.pmx>` (Unity 2022.3.62f1, editor
closed, `Config.json` pointing at the game data), and the build via `Assets/Editor/UmaBuildPlayer.cs`
(`-executeMethod UmaBuildPlayer.BuildWindows -umaBuildOut <dir>`). The add-on zip:
`blender --command extension build --source-dir <a git-archive of the addon HEAD> --output-dir <out>`, then install-test
it with `blender --command extension install-file -r user_default -e <zip>` against a throwaway
`BLENDER_USER_EXTENSIONS`.

## Traps this session added to the list

* a master material's **render settings** (`blend_method`, `surface_render_method`, `show_transparent_back`) are part of
  its look, and every copy inherits them — a graph that can produce alpha plus a blended master puts the whole
  character in EEVEE's depth-less pass;
* a **driver** on a socket overrides a probe's direct `default_value` write — set the value on the `Uma Sun` property
  and let `frame_set` evaluate it, or the probe measures nothing (this made a working feature look broken twice);
* `--background` does not draw the decal quads at all (their opacity can be zeroed with no change to the image) — decals
  are a GUI check, both shaders behave the same;
* node **counts** are a bad assertion: a group may hold as many frames, reroutes and `Group Input` nodes as its author
  likes, so count the maths;
* `git add -A` swept a tool's `.bak` into a commit — `*.bak` is ignored now, and the zip build is verified for junk;
* counting sockets by *type* rather than name in a `ShaderNodeMix` is how a dirt retarget fed a node its own result.

## What is next

1. **Asset families (`docs/ASSET_FAMILIES.md`).** A member compared us against Umamusume Explorer: all ten `3d/chara/*`
   families are already in our database, but `attach`, `richprop`, `sweat` and `toonprop` are never used, and the prop
   panel's filter is the literal substring `pfb_chr_prop` — likely why those families never show. **The dump now exists
   and has been run** (`-umaListFamilies [filter]`, also `-ListFamilies` / `-ListFamiliesFilter` on the wrapper; metadata
   only, opens no bundle). It refutes "one wider substring": none of the four families is named `pfb_chr_prop*`. The
   real names are `pfb_attach####_##_####_##` (321 entries, with their own `_wet`/`_dirt` maps — real shaded models),
   `pfb_toon_prop####_##` (153), `pfb_rich_prop####_##` (3, i.e. barely a family), and `pfb_chr####_##[_face###]_sweatlocator`
   (503 — locators, no geometry, so `sweat` stays out). **Loading them then changed the plan once more**: `attach` is
   not a prop at all — the prefab carries a **copy of the character rig** (`Hip`, `Spine`, every finger,
   `Hand_Attach_L/R`), so it is an accessory that has to be retargeted onto the character it is worn by
   (nail polish, bandages; the user confirmed this from the viewer). It is now a character feature:
   `UmaContainerCharacter.AttachAccessory` retargets the prefab's skins by bone name (the same `MergeBone` hair
   uses), the materials panel offers one toggle per prefab (`UISettingsModel.LoadAttachPanel`, 28 for 1048), and
   `-umaAttach` does it headlessly. Only **ten characters** ship the family (`attach{id}_00/`). Measured on 1048:
   +830 verts, +2 928 indices, +1 material, +4 textures, **+2 bones** (the accessory's own holder nodes) against the
   same character bare, with every rig bone retargeted. The prop panel keeps `prop`, `toonprop` and `richprop`
   (1 194 → 1 342 entries); `toonprop` is one held/effect prop per character (140 folders), `sweat` stays out.
   Remaining: the user's look at the toggles in the viewer (the shipped 3.5 exe predates all of this), and whether
   `mini`'s `pfb_mattach*` should get the same attachment treatment. `docs/ASSET_FAMILIES.md` has the numbers and
   the three traps.
2. **Spork's look has never been compared with the viewer's.** The viewer draws its own Unity shaders; nothing has put
   a Spork-shaded uma in Blender beside the same uma in the viewer. The only Spork export that exists is 1063.
3. **Decals need eyes**, not tests: see the trap above.
4. **Still open by design**: whether `Spin`/`Tear_Scroll` become node-driven (self-containment); whether the FACE
   group's dirt should darken the final colour (as now) or only the diffuse taps.
5. **The user's uncommitted `PmxTextureVariants = true`** in `Assets/Scripts/Config.cs` — decide with them whether that
   becomes the shipped default (it roughly doubles texture output) or is reverted.
