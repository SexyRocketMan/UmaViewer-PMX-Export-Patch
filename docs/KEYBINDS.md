# Keyboard shortcuts, and the modes they have to stay out of

The viewer's keys, where they live, and the audit behind the rules. Read the audit before adding a
binding: the same key is a different thing in each mode, and that is not obvious from the binding table.

## Where they live

`Config.json` → `ViewerShortcuts`, one line per key:

```json
"ViewerShortcuts": [
    { "Name": "ListUp",      "Key": "UpArrow", "Alt": false },
    { "Name": "ListUp",      "Key": "W",       "Alt": false },
    { "Name": "RainLook",    "Key": "R",       "Alt": false }
]
```

* `Name` is the action (below), `Key` a `UnityEngine.KeyCode` name, `Alt` requires Alt to be held.
* One line per key, so an action can have several, and deleting a line removes that key.
* Nothing is guessed at: an unknown `Name` or `Key` is named in the log and that line is skipped. On
  startup the viewer logs what it bound, e.g. `[Shortcuts] 9 binding(s): LeftArrow -> PanelPrev, ...`.
* `UIShortcuts` owns the action list and the context rules; `ShortcutConfig` in `Config.cs` is only the
  shape. A dispatch on an action name that is not in `UIShortcuts.Definitions` cannot happen silently.

## The actions

| Name | Does | Repeats while held |
|---|---|---|
| `PanelPrev` / `PanelNext` | shows the previous/next entry of `UmaViewerUI.TogglablePanels` | no |
| `ListUp` / `ListDown` | walks the active panel's list, **loading each entry it lands on** | yes (0.5 s delay, then every 30 ms) |
| `ExportModel` | `UISettingsModel.ExportModel`, the same call the button makes | no |
| `AutoRecord` | `UmaViewerUI.AutoRecordVMD`, the same call the button makes | no |
| `RainLook` | `UISettingsModel.ToggleWetLook`, the wet texture set a rained-on race uses | no |
| `DirtRates` | **a measurement knob, not a feature**: steps the shader global `_DirtRate` (three zones) through 0 / 0.25 / 0.5 / 0.75 / 1 and logs it. See the dirt section below | no |

## The modes, and what they suspend

A shortcut is only useful when it does not fight the mode the viewer is in. Three contexts suspend
bindings, and each suspension has a reason that was measured rather than assumed:

| Context | Suspends | Why |
|---|---|---|
| a text box has focus | everything | the search field owns the arrows and every letter (`TypingInField`) |
| **free camera** (`CameraOrbit` mode 1) | any binding whose key is `W A S D` or an arrow | that mode moves the camera through the `Horizontal`/`Vertical` axes, which are exactly those keys. Matched against the key a shortcut was actually given, so rebinding `PanelNext` to `J` makes it work while flying. `E` and `R` do not collide and keep working. |
| **pose mode** | `ListUp`/`ListDown`, `PanelPrev`/`PanelNext`, `AutoRecord` | the list walk *loads* what it lands on, which would replace the model mid-pose; `PoseManager.EnablePoseMode` disables the `UmaAnimator` the recorder samples, so a take would be frozen. `ExportModel` and `RainLook` stay. |

The pose gizmo (`TransformGizmo`) needs **LeftAlt** for every one of its own keys (`Alt+W`, `Alt+E`,
`Alt+R`, `Alt+Y`, `Alt+Z`, `Alt+C`, `Alt+X`, `Alt+S`), and `UIShortcuts` fires a plain binding only when
Alt is *not* held. So plain `W`/`E`/`R` never fight the gizmo - that half of the conflict did not exist,
which is worth knowing before moving any of these keys around.

## The audit: every key the project reads

Taken from a grep of `Input.GetKey*` / `GetAxis` over `Assets/Scripts`. A new binding has to be checked
against this, not against the mode names.

| Key | Reader | Mode |
|---|---|---|
| `W A S D`, arrows | `CameraOrbit` free mode, `Input.GetAxis("Horizontal"/"Vertical")` | free camera only |
| `LeftControl` | `CameraOrbit` orbit mode: moves the orbit target up/down | orbit only |
| `H` | `PoseManager` help panel | pose mode only (gated this session; it used to fire in every mode) |
| `Space` | `HandleManager` IK toggle | pose mode only (gated this session; same leak) |
| `Alt` + `W E R Y Z C X S` | `TransformGizmo` gizmo type/pivot/space | pose mode only |
| `Shift`/`Ctrl` + `Z Y` | `TransformGizmo` undo/redo | pose mode only |
| `LeftShift`/`LeftControl` | `TransformGizmo` multi-select, snapping | pose mode only |
| `Escape` | `Director`, while a live is playing | live playback |
| `A S D ...` | `FreeCam.cs` | **nothing** - see below |

`FreeCam.cs` (`Assets/Scripts/LiveToolBox/`) is dead code: no scene references its script GUID and
nothing calls `AddComponent<FreeCam>`. The free camera the viewer actually has is `CameraOrbit` mode 1.
It reads `W/A/S/D/E/R/Q/F` on top of the arrows, so if it were ever wired up it would collide with
almost every shortcut here - worth deleting rather than chasing.

Unity's own UI navigation also owns the arrow keys. `UIShortcuts.Fire` drops the selected object
(`EventSystem.current.SetSelectedGameObject(null)`) before running a binding, so the selection does not
move at the same time - keep that if the dispatch is ever restructured.

## The dirt set, and the knob for it

There is a dirt variant set, and it is **not** a wet-style swap: `tex_<body>_dirt` exists as an asset
(`tex_bdy1001_00_dirt` beside `_diff`/`_base`/`_shad_c`), but the uma shader draws it from a dedicated slot
— `_DirtTex` (`Texture`, unbound on every material this project exports), `_DirtScale` (`Range`), and a
per-material `_DirtRate` (`Vector`, `(0,0,0,0)`). On top of that there is a global `_DirtRate`
(**float array of 3**, `_DirtRate_0..2` on `UmaViewerGlobalShader`) plus `_GlobalDirtColor`,
`_GlobalDirtToonColor` and `_GlobalDirtRimSpecularColor`.

`DirtRates` (default `D`) steps those three globals so the shader's response can be watched. It sets the
shader global directly, because `UmaViewerGlobalShader` pushes its fields in `Start()` only, so moving
them afterwards would not reach the shader. **A rate alone may show nothing** — the texture slot is empty
on these materials — so what it measures is whether the game's shaders respond to the rate at all, before
anything binds `_DirtTex` or picks a `_DirtScale`. Nothing is guessed into a real "dirty look" until that
is seen; the handoff records what the pieces are.

## Related

`docs/UI_HOOKUP.md` is the panel side of the same options (which control calls which method). The rain
look itself is `UmaContainerCharacter.SetWetLook`, whose doc comment explains why a wet character owns
private copies of its materials.
