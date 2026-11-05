# Keyboard shortcuts, and the modes they have to stay out of

Keys live in `Config.json`; the action list and the context rules live in `UIShortcuts`. The same key is a
different thing in each mode, so check the audit before choosing one.

## Where they live

`Config.json` → `ViewerShortcuts`, one line per key:

```json
"ViewerShortcuts": [
    { "Name": "ListUp",      "Key": "UpArrow", "Alt": false },
    { "Name": "ListUp",      "Key": "W",       "Alt": false },
    { "Name": "RainLook",    "Key": "R",       "Alt": false }
]
```

* `Name` is the action (below), `Key` a `UnityEngine.KeyCode` name, `Alt` requires Alt held.
* One line per key: an action can have several, and deleting a line removes that key.
* An unknown `Name` or `Key` is logged and skipped. Startup logs the bound set, e.g.
  `[Shortcuts] 9 binding(s): LeftArrow -> PanelPrev, ...`.
* `UIShortcuts` owns the actions and the context rules; `ShortcutConfig` in `Config.cs` is only the shape.

Defaults (`Config.DefaultViewerShortcuts`): `PanelPrev`/`PanelNext` on `LeftArrow`/`RightArrow`,
`ListUp`/`ListDown` on `UpArrow`/`DownArrow` plus `W`/`S`, `ExportModel` on `E`, `AutoRecord` on `A`,
`RainLook` on `R`.

## The actions

| Name | Does | Repeats while held |
|---|---|---|
| `PanelPrev` / `PanelNext` | shows the previous/next entry of `UmaViewerUI.TogglablePanels` | no |
| `ListUp` / `ListDown` | walks the active panel's list, **loading each entry it lands on** | yes (0.5 s delay, then every 30 ms) |
| `ExportModel` | `UISettingsModel.ExportModel`, the same call the button makes | no |
| `AutoRecord` | `UmaViewerUI.AutoRecordVMD`, the same call the button makes | no |
| `RainLook` | `UISettingsModel.ToggleWetLook`, the wet texture set a rained-on race uses | no |

`ListUp`/`ListDown` skip the character panel: characters load too slowly to compare by keypress.

## The modes, and what they suspend

| Context | Suspends | Why |
|---|---|---|
| a text box has focus | everything | the field owns the arrows and every letter (`TypingInField`) |
| **free camera** (`CameraOrbit` mode 1) | any binding on `W A S D` or an arrow | that mode flies on the `Horizontal`/`Vertical` axes, which are those keys. Matched against the key a shortcut was actually given, so `PanelNext` rebound to `J` works while flying; `E` and `R` keep working. |
| **pose mode** | `ListUp`/`ListDown`, `PanelPrev`/`PanelNext`, `AutoRecord` | the list walk *loads* what it lands on and would replace the model mid-pose; `PoseManager.EnablePoseMode` disables the `UmaAnimator` the recorder samples, so a take would be frozen. `ExportModel` and `RainLook` stay. |

The pose gizmo (`TransformGizmo`) uses **LeftAlt** for all of its own keys (`Alt+W`, `Alt+E`, `Alt+R`, `Alt+Y`,
`Alt+Z`, `Alt+C`, `Alt+X`, `Alt+S`), and `UIShortcuts` fires a plain binding only without Alt, so plain
`W`/`E`/`R` cannot fight it.

## The audit: every key the project reads

Check a new binding against this, not against the mode names.

| Key | Reader | Mode |
|---|---|---|
| `W A S D`, arrows | `CameraOrbit` free mode, `Input.GetAxis("Horizontal"/"Vertical")` | free camera only |
| `LeftControl` | `CameraOrbit` orbit mode: moves the orbit target up/down | orbit only |
| `H` | `PoseManager` help panel | pose mode only |
| `Space` | `HandleManager` IK toggle | pose mode only |
| `Alt` + `W E R Y Z C X S` | `TransformGizmo` gizmo type/pivot/space | pose mode only |
| `Shift`/`Ctrl` + `Z Y` | `TransformGizmo` undo/redo | pose mode only |
| `LeftShift`/`LeftControl` | `TransformGizmo` multi-select, snapping | pose mode only |
| `Escape` | `Director`, while a live is playing | live playback |
| `A S D ...` | `FreeCam.cs` | **live playback** - see below |

`FreeCam.cs` (`Assets/Scripts/LiveToolBox/`) sits on a camera in `Assets/Scenes/LiveScene.unity`, which
`UmaViewerBuilder` loads additively for live playback: while a live plays it moves that camera on
`W/A/S/D/E/R/Q/F`, `LeftShift` and the mouse wheel in every context, so bindings on those keys are contested
there. Outside live playback the viewer flies with `CameraOrbit` mode 1, on the same axes plus the mouse.

Unity's UI navigation also owns the arrow keys, so `UIShortcuts.Fire` clears the selection
(`EventSystem.current.SetSelectedGameObject(null)`) before running a binding.

## The dirt set, and why there is no key for it

The dirt variant is not a wet-style swap. `tex_<body>_dirt` exists as an asset (`tex_bdy1001_00_dirt` beside
`_diff`/`_base`/`_shad_c`), but the uma shader draws it from its own slot: `_DirtTex` (`Texture`, unbound on
every material this project exports), `_DirtScale` (`Range`), a per-material `_DirtRate` (`Vector`,
`(0,0,0,0)`), and the globals `_DirtRate` (**float array of 3**, `_DirtRate_0..2` on
`UmaViewerGlobalShader`), `_GlobalDirtColor`, `_GlobalDirtToonColor` and `_GlobalDirtRimSpecularColor`.

No binding steps those globals: stepping the global rate through its whole range changes nothing visible, which
an unbound `_DirtTex` predicts. A dirt look needs the material side (the `tex_*_dirt` map on `_DirtTex`, a
`_DirtScale`, the per-material `_DirtRate`), so it belongs with the addon's wet/dirt wiring, not a viewer key.
The exporter writes those maps under `Config.PmxTextureVariants` and names them per material with `dirt=`
tokens; until that wiring exists they are exported and unused, as the model comment's `texture variants:` line
says.

## Related

`docs/UI_HOOKUP.md` covers the panel side (which control calls which method); the rain look is
`UmaContainerCharacter.SetWetLook`.
