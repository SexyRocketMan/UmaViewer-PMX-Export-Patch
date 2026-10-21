# CySpring (the wiggle-bone physics): what this fork learned

**Status: shelved (2026-10).** The mechanism is ported, verified against the game's own C# and baked onto
models, and it still does not look like the game. The blocker is not the solver — it is that there is no
numeric reference for what the game's tail actually does, so every version is judged by eye alone. This file
records what works, what was ruled out and what would unblock it, so the next attempt starts from here.

Nothing in this work touched `Assets/` — the viewer and the Blender addon are as they were. The whole thing
lives in `Tools/`, on the branch `feat/cyspring-export` (12 commits, listed at the end).

## The mechanism

The game simulates tails, hair and skirts with its own solver, `CySpring`. The decompiled C# is in this
repository (`Assets/Scripts/umamusume/Gallop/DereScript/StageScript/CySpring/`) and it is both the reference
implementation and the only specification there is:

* `CySpringNative.UpdateNativeCloth` seeds each bone from its parent (`_oldPosition = previous bone's
  _currentPosition`, `_oldRotation = initLocalRotation * parent finalRotation`) and then steps every bone:
  a Verlet position update driven by `aim * stiffness + velocity * drag + springForce + windPower - gravity`,
  projected back onto the bone's own length, and a final `final_rotation = FromToRotation(aim, direction) *
  oldRotation`.
* The game ships a native `CySpringPlugin.dll` for this. It cannot run outside the game: DllMain fails
  (`0x8007045A`), and loading it with `DONT_RESOLVE_DLL_REFERENCES` crashes on first call (`0xC0000005`). The
  managed fallback above is what the game decompiles to, and it is what `Tools/cyspring_port.py` transcribes.
* Struct sizes, for anyone reading the binary later: `NativeClothWorking` 280, `NativeClothCollision` 48,
  `NativeClothParameter` 128. Collision types: `0` sphere, `1` none, `2` capsule, `3` plane.

Two things that are easy to get wrong and cost this fork days:

* The solver holds each link's **tail** in `_CurrentPosition` and its **head** in `_OldPosition`
  (`Reset` walks `currentPosition = diff * initBoneDistance + oldPosition`). Seeding the head into both
  stretches the chain.
* A link's distance and axis run from a bone **down** to its child (`Initialize` measures
  `_parentBone.InitBoneDistance = Distance(child, parent)`), and a leaf with no child copies its parent's.
  Reading the non-leaf case upwards folds the chain in half.

## The game's own data

A costume prefab carries the parameters. For character 1001's tail (`tail0001`, 5 spring bones):

| bone | length | stiffness | drag | gravity | wind rates |
|---|---|---|---|---|---|
| `Sp_Hi_Tail0_B_00` | 6.6 cm | 170 | 200 | 50 | 1/1/1/1 |
| `Sp_Hi_Tail0_B_01` | 11.6 cm | 190 | 400 | 50 | 1/1/1/1 |
| `Sp_Hi_Tail0_B_02` | 16.5 cm | 210 | 600 | 50 | 1/1/1/1 |
| `Sp_Hi_Tail0_B_03` | 15.5 cm | 230 | 600 | 50 | 1/1/1/1 |
| `Sp_Hi_Tail0_B_04` | inherited | 250 | 600 | 50 | 1/1/1/1 |

and on the container: `verticalWind`/`horizontalWind` enabled with slow cycles 0.7 / 1.4 and angle widths
17.3 / 24.9 degrees, fast cycles 0.6 / 1.2 with widths 35.7 / 45.5, plus `hipMove` with influence distances
0.01 and 0.03.

Two traps in the assets themselves:

* **The tail's bones are split across two prefabs.** `clothes/pfb_<tail>_cloth00` carries the spring
  parameters and two colliders; `pfb_<tail>` (the tail's own prefab) carries the transforms. Reading only the
  cloth prefab gave a chain of 3 bones and silently dropped the rest.
* **`Sp_Hi_Tail0_B_03` is in the model but in no prefab.** It is a real bone the mesh is skinned to, so the
  visible tail bends at it, but the spring data's transform for it does not exist. `uma_spring_dump.py --pmx`
  places it from the model by fitting the model's frame onto the prefab's (scale 0.08193, a 180 degree yaw,
  a translation; residual 0.00000 on the three bones both sides share).

## The pipeline

All of it is in `Tools/`; each file explains itself at the top.

| file | what it does |
|---|---|
| `uma_spring_dump.py` | A costume's spring data to a chain spec: parameters, bone positions in game units, scripted drive, wind block. `--pmx` fills bones the prefabs lack, `--links N` truncates, `--swing`/`--axis` script a parent motion. |
| `cyspring_port.py` | The solver, float32, transcribed from the C#. `--compare` holds it against `spring_probe`'s trace frame by frame; `simulate_spec` runs a real chain with a drive (rotation and translation), an optional warm-up and wind. |
| `spring_probe/` | A .NET project that compiles the repo's `CySpringNative.cs` and writes a trace, so the transcription can be checked bit for bit. |
| `cyspring_metrics.py` | What a chain did, as numbers: link length against the prefab's, angle off the driven direction, lag in frames, overshoot, settle, ringing, per-frame step, tip path and sag. |
| `spring_plot.py` | The same run as a picture: a self-contained HTML page with playback, an animated GIF, and a matplotlib sheet with the anchor's travel per frame. |
| `chain_drive_from_vmd.py` | Drives a chain with a real clip recorded from the viewer: the hip's rotation *and* travel, interpolated between the VMD's every-second-frame keys, converted out of MMD space. |
| `blender_cyspring_demo.py` | Imports a PMX at mmd_tools' own scale (0.08), optionally puts a recorded motion on it, bakes the chain as world rotations onto the model's own bones, verifies the bake against the solver, saves the blend (and optionally renders it). |

## What works

* **The port is exact.** `cyspring_port.py --compare` against the trace from `spring_probe` (which runs the
  game's own C#) matches to 0.000e+00 on every frame and every component.
* **Chains run.** Link lengths hold to 1e-8 game units over 120+ frames; the chain droops under gravity but
  not rigidly; gravity alone leaves link 0 about 7 degrees off its rest direction and the tip about 10.
* **Bakes are exact.** The demo reads the keyed animation back and compares every bone against the solver:
  0.0000 degrees. Getting there needs the prefab-to-armature frame carried per bone (`M @ R @ M^-1`); applied
  directly, the solver's rotations are about 87 degrees off, because the spec is in the game's frame and the
  imported armature is in Blender's.
* **A running character.** With `--body` the demo applies a recorded motion and bakes the tail on top. On
  `anm_rac_type01_run02_stride` at 60 fps: hip travels 0.078 units, the tail tip travels 0.321 with a 0.805
  path, 1.8x what the body carries on its own.
* **Resistance to the harness getting in the way.** The renders here write with `Image.save_render` because
  `scene.render.filepath` is silently rewritten by a startup add-on; the VMD drive interpolates because a VMD
  keys every second frame; the import scale is mmd_tools' 0.08 because 1.0 put a 20 unit model in a 1.6 metre
  scene.

## Dead ends, with the evidence

* **The native solver cannot be borrowed.** See above: DllMain fails outside the game, and skipping it
  crashes. There is no way to run the game's own spring solver on its own.
* **The `*_tail` clips are not tail animation.** The game ships `anm_eve_type00_*_tail` clips; they are Unity
  *humanoid muscle* clips with no generic curves and no tail tracks, and the viewer's VMD recorder writes
  humanoid bones only (positions `(-x, y, -z) * 12.5`, rotations `(-x, y, -z, w)`), so recording one gives an
  almost-static file. In this game the tail is simulation and nothing else.
* **The viewer's CySpring is dormant.** Its `CySpring*` classes are in `Assets/Scripts`, but nothing calls
  `UpdateNativeCloth`, `DoGatherAllSpring` or `DoPostAllSpring` — the classes exist so the prefabs deserialize.
  The viewer's tail is rigid, which is why it looks wrong, and implementing the driver there would be the same
  port with the same unknowns, not a source of truth.
* **The game cannot be decompiled further.** It is IL2CPP with no `global-metadata.dat`; `GameAssembly.dll`
  will not yield the wind generator, the hipMove behaviour or the update loop. The spring solver we do have is
  already decompiled C# from another route.
* **Video of the game is no use** as a reference: the pose data cannot be recovered from pixels.

## Why it still does not look like the game

The solver reproduces the mechanism; the *look* does not. What is left, in order of how much it matters:

1. **No ground truth.** Nothing measures the game's tail. Every judgement is "that looks stiff to me", which
   is why this route ran out of road: each fix moved a number in the right direction and still missed.
2. **The balance is genuinely stiff.** With the game's own numbers (stiffness 170 against drag 200-600) the
   chain follows its parent closely: about one frame of lag, under a degree of overshoot, a tip path 1.3-1.9x
   a rigid chain's. That may simply *be* the game's behaviour — but the memory of the game is livelier, and
   there is no data to settle it. Raising drag to make it whip destabilizes the integrator (steps of 400 mm
   per frame at drag x5); lowering stiffness does the same.
3. **Wind is a reconstruction, not a transcription.** The generator lives in game code we do not have. The
   version here reads the authored cycles and widths as two sines per axis scaled by each bone's stiffness.
   The slow and fast cycles *beat* when added (the tip jumps up to 13 cm in a frame), so `--wind-fast 0`,
   which keeps the slow sway alone, is the usable setting. Being invented, it is the least trustworthy part
   of the whole thing.
4. **The leaf bone is unstable.** The chain's last bone inherits its length from the one above it, and this
   port whips it (112 degrees in one frame). Chains are capped at the four real links, which cover the
   visible tail.
5. **Colliders as parsed are unusable.** The hip collider's radius is 1.0 in prefab units against links of
   0.066-0.116, so every bone sits inside it and gets pushed out every frame (the chain parks 26 degrees off
   and never settles). They are off, which is why the tail clips through the legs.
6. **`hipMove` is not implemented** — the authored thresholds (0.01 / 0.03) exist but the behaviour does not.

## What would unblock it

In order of value:

1. **A numeric reference for the game's tail.** Everything else is tuning against a memory. The candidates
   are: hooking the running game (IL2CPP, hard), an instrumented build of the game (not available), or
   someone with more dungeon time against `GameAssembly.dll` than this fork had. Without it, the remaining
   knobs are guesses dressed as parameters.
2. **The wind formula** and **the hipMove behaviour** from the game's code.
3. **The leaf fix** in the port, so a chain can be its full five bones.
4. A collider scale that is actually understood (the authored radius is plainly not in game units).

## Where the artifacts are

`C:\Users\user\Documents\umaviewer_exports\spring\`:

* `spec_child.json`, `spec4.json`, `spec_run60.json` — chain specs (3 links with a wag, 4 links, 4 links
  driven by the run clip).
* `run.vmd`, `run60.vmd`, `tail_loop01.vmd` — recordings from the viewer (the 60 fps one has every frame keyed).
* `chain_*.gif`, `chain_*.png`, `chain_*.html` — the 2D views and diagnostic sheets.
* `tail_model.blend`, `tail_run60.blend` + `.mp4`, `tail_idle_wind.blend`, `tail_idle4_*.blend` + `.mp4` —
  the bakes: the synthetic wag, the run, and the idle tail with wind at four settings.

## How to resume

```powershell
# a chain, in numbers and in a picture, before any Blender
python Tools/uma_spring_dump.py 1001 00 --chain Sp_Hi_Tail0_B_00 --swing 35 --pmx model.pmx -o tail.json
python Tools/cyspring_metrics.py tail.json --no-colliders --motion impulse
python Tools/spring_plot.py tail.json --no-colliders --gif tail.gif --sheet tail.png

# with a real clip driving it, then baked onto the model with the motion on the body
pwsh -File Tools/headless_export.ps1 -Char 1001 -Motion anm_rac_type01_run02_stride `
    -RecordVmd run60.vmd -RecordFps 60 -RecordReduction 1
python Tools/chain_drive_from_vmd.py tail.json run60.vmd -o tail_run.json --fps 60
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b -P Tools/blender_cyspring_demo.py -- `
    tail_run.json out.blend --model model.pmx --body run60.vmd

# the reference that came closest: body at rest, tail alone, slow wind only
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b -P Tools/blender_cyspring_demo.py -- `
    tail.json out.blend --model model.pmx --motion idle --frames 240 --wind 0.9 --wind-fast 0 --fps 60
```

## The commits

`feat/cyspring-export`, twelve commits, `Tools/` only:

```
bac80ee spring: the whole tail, and a wind that can be dialled
4cd9fd1 blender: an idle bake, the body at rest and only the tail moving
1f0398e blender: import at mmd_tools' own scale, and bake onto a running character
b0e7279 spring: interpolate the recorded hip, and plot the anchor's speed
d199a37 spring: wind, and the two links the dump was dropping
efa0b2f spring: look at the chain in 2D, and drive it with a real clip
037e2ea spring: the chain was seeded wrong, and here are the numbers
b880c07 spring: bake onto a real model, and write where we are told
0da75cd blender: bake a chain and render it, for a human to judge
77b307c spring: build a chain from the prefabs, in the game's own units
0a4ceec port: run a real chain, and the two binding questions it raises
22c2c57 port: the game's spring solver in Python, held to the C# frame by frame
6371526 probe: the game's spring solver, and why it is not the reference
```
