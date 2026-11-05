# Recording a VMD: what a take is, how it is sampled, and how to measure it

A recorded take is a delta against a pose captured once, and nothing in the recorded file says what that pose was - so
a take can look right in the viewer and wrong when it is opened in MMD. Every number below is measured on takes of
ordinary game clips.

## What a take actually is

1. `UnityHumanoidVMDRecorder.Initialize()` builds the **reference**: `animator.enabled = false`, then
   `UmaContainerCharacter.ResetBodyPose()`, `UpBodyReset()`, `UmaApose.IntoAPose()` (the 38.5° A-pose the motions are
   authored against), then `new BoneGhost(...)` which stores each bone's **world** rotation, then `BackToTPose()`.
2. `BoneGhost.GhostAll()` runs once per sample and gives each ghost a world rotation:
   `liveWorld · inv(capturedWorld) · ghostOriginal` - a **delta expressed in world space**.
3. `SaveFrame()` writes `ghost.localRotation`, converted Unity→MMD by `new Quaternion(-x, y, -z, w)`, so the recorded
   local is `inv(ghostParentWorld) · ghostWorld`.

Consequences: **a pose present at capture but not at sample is written into every key as a constant** and reads as a
basis error rather than a sampling error (a take wrong in the file and right in the viewport is this); and **the world
frame is in the expression**, so whatever the character's placement or orientation is, it lands in the take.

The ghost chain is not the live hierarchy: it parents `上半身` to the ghost of `Hip`, skipping `UpBody_Ctrl` and
`Waist`, so their bend is folded into the recorded spine. The fold is lossless, but those two bones have no key of their
own and anything they hold goes into the bone above them.

**How it is sampled.** The clip is rewound to time 0 and advanced one fixed step per sampled frame
(`Time.captureDeltaTime = 1/fps`), for `round(clip length × fps) + 1` frames at `-RecordFps` (30 by default). A clip
that declares itself a loop - `isLooping`, or `_loop` in the asset name - has frame 0 copied onto its last frame; a clip
that does not declare it is closed only when its seam is within 0.05 rotation and 0.5 position. Up to three leading
frames are dropped when their step away from the cycle stands out (3× the interior maximum), because
`CloseRecordedLoop` would otherwise copy that offset to both ends. Physics-driven bones are the one part that cannot be
made identical to the first frame.

## What a take gets wrong, and the guard

| what goes wrong | guard | measured |
|---|---|---|
| the viewer's look-at solve (`BipedIK` + `IKSolverLookAt`, `bodyWeight 0.25`, driven at `Camera.main`) bends `spine`/`chest`/`neck`/`head` on top of the clip, and the sampler reads exactly those bones | `UmaContainerCharacter.SuspendLookAt()` is held by `StartRecording`/`StopRecording` and also zeros the solver's `IKPositionWeight`, so every path - the auto-record coroutine, the manual *Record VMD* button, live recording - samples what its reference was captured against. *Look at Camera* is off by default | spine offset 68.79° with the solve live at the samples against 34.91° with it off |
| the rest pose restores one table that holds only the skinned bones, so the unskinned `UpBody_Ctrl` and `Waist` - between the hips and the spine - keep whatever pose the session left on them | the rest table also captures each skinned bone's **ancestors** up to the character root, and `ResetBodyPose()` applies it **parents-first**; `UpBodyReset()` prefers the table over `UpBodyPosition`/`UpBodyRotation`, which are read after the idle has already moved that bone | a constant 94.00° of yaw on the spine and everything above it, for the same clip and model; 6.68° after |
| the ghosts are written in dictionary order, so a child fixes its local against a parent not yet written - worst on frame 0, when every parent still sits at its capture state | `GhostAll` walks the ghost hierarchy **parents-first**, computed once per ghost set (the hierarchy does not change during a take) | frame 0→1 head step 29.5° recorded against 0.4° live; after, the first step was 0.0499 against a typical 0.0736, and 51 of 52 bones were identical |

**Nothing that applies absolute world transforms may do so in an arbitrary order.** The same class of fault appears at
two levels here: the rest pose, then the ghosts.

## The instrument

* `Tools\vmd_inspect.py` - `summary`, `loop`, `motion`, **`diff`**. `diff` separates a *constant* per-bone difference (a
  basis error: the reference pose) from one that *moves with the animation* (a sampling error), and pairs up tracks that
  changed bone name; `motion` reports, per bone, the ratio of its first step to its own typical step.
* `UMA_GHOST_DUMP=1` writes `%TEMP%\ghost_dump.jsonl`: one `reference` row per bone (the pose the take is a delta
  against), per-frame rows (value written, live value, both parent frames), and `skipped` rows for control bones the
  ghost chain does not represent. `Tools\ghost_dump_check.py` reads it and separates a constant frame conversion from a
  difference that moves with the animation. It is the only record of what pose the rig was in at capture, which the vmd
  cannot carry; off unless the variable is set, it changes nothing recorded, and it is flushed from `StopRecording` so
  either button produces one.
* `Tools\vmd_inspect.py diff <known-good> <new>` against a **reference recording** gives the size and character of a
  regression within seconds.

## Reproducing a reported take

1. Find the clip from the player log (`%USERPROFILE%\AppData\LocalLow\<company>\<product>\Player.log`), which prints the
   asset path (`…/anm_eve_type00_dance_minuet03_loop`) and the recorder's own step log.
2. Build the older line from source in a worktree - `git worktree add C:\tmp\wt-master origin/master` - copy the current
   `Config.json` (for the game path) into it, and run its own `Tools\headless_export.ps1 -ProjectPath …`. The first run
   in a fresh worktree pays a full asset import; after that a take is ~1.5 min.
3. Record the same clip in both and compare against the reported baseline and against each other. The harness is
   deterministic - same build, same clip, two runs give 52/52 identical bones - so a difference between two runs of one
   build is a code difference, not noise.
4. `UMA_GHOST_DUMP=1` on both sides for any capture-versus-sample question.

None of the harness work is recorder behaviour, and all of it is needed before its takes are evidence: wait for the
character's animator before applying `-umaMotion` (`LoadAnimation` is a silent no-op before it exists); record the clip
that was **asked for** rather than the one the state machine reports right after `Play("motion_1")`, which is the clip
being transitioned *from*; re-apply the motion if the load path takes the animation layer back, and log which clip is
playing; flush the dump from `StopRecording` so the manual button produces one too.

## Traps

* **"Differs from the reference recording" is not "is broken".** The known-good v3.1 baseline carries the head jerk
  itself (`Head` 71.7× its own typical step), so a correct take legitimately differs from it at the head on frame 0.
* **`VmdKeyReductionLevel` confounds every comparison.** Every second key against every key is 2065 bone keys against
  3952, and halving keys distorts fast-moving bones most - which reads as "the fingers are wrong" on whichever hand
  moved most in that clip. Compare takes recorded with the same value.
* **A world-space reference makes a take depend on the character's placement.** Measured: hip world yaw 0.0° at capture
  against 93.9° at the first sample, and the recorded hip at frame 0 was exactly 93.9°. Do not compare takes recorded
  with the character posed or placed differently.
* **A local rotation compared against a stored *world* one reads as a spurious 180°** on every bone deep in a chain.
  `InitBoneTransform` stores world transforms; compare like with like.
* **Judge a first-frame outlier per bone.** `DropAnomalousLeadIn` sums the step over every bone and needs 3× the interior
  maximum, so an opening that summed to 38° against an interior spike of 104° hides a bone moving 71× its own typical
  step. When it does fire the take loses a frame (75 against the clip's 76), which shifts every later comparison by one.
* **`TotalFrames` and key counts are a fair comparison only at the same reduction and the same drop count.**
* **The viewer running from the editor is a different environment** from a player build (idle already running, scene in
  memory, different frame timing), so answer "which build made this take" from the log before believing a measurement:
  `[Uma] look-at at load: on|off` and `[VMD] recording session started: the look-at solve is held off for it` are
  printed for exactly that.

## Open questions

* **A residual 6.68° on the spine** against the v3.1 baseline, after all three guards (it was 94.00°). Unexplained; the
  candidates are the load-time value of `Waist` and the A-pose reference, and neither is measured.
* **`Toe_L` (3.7× its own typical step) and `Shoulder_R` (3.0×, absolute 0.0023)** still show small opening steps; not
  established whether they are animation or artefact.
* **Player versus editor parity is unverified** for every guard here: all were proved in the batch harness, and one take
  from a real build is worth doing.
* **Whether *Look at Camera* still changes a recording**, given that the reference pose is also reset correctly, is not
  measured. The default is off regardless.
* Control bones the ghost chain skips (`UpBody_Ctrl`, `Waist`) have no key of their own by design. Making their motion
  survive into MMD independently of the spine is a model/format question, not a recorder one.

## Where the changes are

| commit | what |
|---|---|
| `f7302fe` | the look-at solve is suspended for a take (first attempt; coroutine only) |
| `d9ab252` | the hold moves to `StartRecording`/`StopRecording`, so both buttons obey it |
| `326cd6c` | Look at Camera off by default, in the UI model **and** the scene's toggle |
| `c33f9bf` | the load-time look-at state and the session hold are logged |
| `00426c6` | rest table captures the control bones; `ResetBodyPose` applies parents-first; `UpBodyReset` prefers it |
| `e814f5d` | ghosts are written parents-first, which removes the first-frame jerk |
| `ca088a7`, `ee544ba`, `e6dc4e5` | harness only: `-umaMotion` timing, the requested clip, the dump from `StopRecording` |
| `097478a`, `7988574`, `10fb224`, `43823fe`, `4a178fa` | the tools: `vmd_inspect diff`, per-bone opening outliers, the dump and its reference/skipped rows |
