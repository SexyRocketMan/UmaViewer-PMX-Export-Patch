# spring_probe

A way to run the game's spring solver outside the game, on a chain built from the numbers the game itself
ships. It exists to answer, before any of this becomes a feature: **what do we compare a Blender port
against?**

```
cd Tools/spring_probe
dotnet run -c Release -- managed        # the solver the viewer ships, 120 frames, two scenarios
dotnet run -c Release -- native         # the game's own plugin (see the finding below)
dotnet run -c Release -- both

# the frames it produces, for another implementation to be held to
dotnet run -c Release -- managed --trace trace.json
python ../cyspring_port.py --compare trace.json
```

## What it runs

It compiles **the viewer's own copies** of the game's spring code rather than re-declaring it, so what is
exercised is what the viewer would run:

| source | role |
|---|---|
| `Assets/Scripts/.../CySpring/NativeClothWorking.cs`, `NativeClothCollision.cs`, `NativeClothParameter.cs` | the three structs the solver reads and writes, laid out with explicit offsets |
| `Assets/Scripts/.../CySpring/CySpringNative.cs` | `UpdateNativeCloth` and its managed implementation, `UpdateNativeClothInternalMono` |
| `UnityShim.cs` | only the engine maths those sources need - `Vector3`, `Quaternion`, `CySpring.eCapability` |

The chain it builds is the tail's real shape and numbers (five bones, links of 0.25, stiffness 170…250,
drag 200…1000, gravity 50), anchored the way the game anchors it: the root bone's old position is the bone
it hangs from, and every other bone is seeded from its parent by the solver.

## Finding 1 - the shipped plugin will not initialise outside the game

`umamusume_Data/Plugins/x86_64/CySpringPlugin.dll` exports exactly `NativeClothUpdate`,
`NativeClothSkirtUpdate` and `NativeSkirtUpdate`, and its only import is a deliberately miscased
`KeRnEl32.dLl` - no Unity, no CRT. So it looks like a plain drop-in. It is not:

```
plain load failed: ... A dynamic link library (DLL) initialization routine failed. (0x8007045A)
```

`0x8007045A` is `ERROR_DLL_INIT_FAILED`, and the failure is inside the plugin's own `DllMain`: it refuses to
initialise in a process that is not the game. That is a guard, not a missing dependency - the import table
has nothing else it could be missing.

Skipping the initialisation (`LoadLibraryExW` with `DONT_RESOLVE_DLL_REFERENCES`) does load the module and
resolve the symbol, but calling the export then faults with `0xC0000005`: the state `DllMain` sets up is
required.

**Consequence:** the native solver is not a reference we can call. Getting at it would mean either running
inside the game process, or defeating the guard - a disassembly job of its own, not a validation step.

## Finding 2 - the managed port is the reference instead

The same equations exist in C#, in `CySpringNative.UpdateNativeClothInternalMono` (in this copy of the game
code the `NativeClothUpdate` call is commented out, and the managed path is what runs). That is the game's
own algorithm and the game's own parameter semantics, so it is what a port should be measured against.

Behaviour it shows, on the tail's numbers:

| check | result |
|---|---|
| rest length of every link over 120 frames | preserved to 1e-4 (0.2500 each) |
| NaN / Inf | none |
| rest pose under gravity | holds - stiffness balances gravity, drag settles it |
| a sphere the chain passes through | pushes the chain out; the residual penetration (0.175 with a 0.6 sphere and 0.25 links) is the link re-projection winning over the collision, which is how this solver behaves |
| determinism | same input, same output |

Two things learned while building this, both worth keeping:

- **A collider's position is relative to the bone it is attached to**, and `parentWorldPosition` is what that
  bone's world position is passed in as. A collider offset that looks right in local numbers can sit
  nowhere near the chain if the parent is not what you think it is.
- **The root bone's `_oldPosition` is the anchor it swings around** (its parent's position), not its own
  rest position. Getting that wrong shifts the whole chain by one link and looks like a solver bug.

## What this means for the Blender bake

The bake will be a transcription of `UpdateNativeClothInternalMono` plus the chain seeding in
`UpdateNativeCloth`, and the check that it is faithful is a numeric one: same input state, same number of
steps, compare the positions and rotations the C# produces with those the Python produces. That comparison
is cheap, deterministic, and independent of rendering - the kind of check `docs/VERIFICATION_WORKFLOW.md`
asks for. Whether the result *looks* like the game stays a human judgement, on a labelled sheet.
