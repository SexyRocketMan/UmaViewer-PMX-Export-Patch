# Recording a VMD: what the take is, what went wrong, and what the instrument is

Written at the end of the session that fixed a long-standing report — *"records motions come out with the upper
body twisted; 3.1 was fine"*. Three separate defects were found and fixed, and the reason they were so hard to
pin is worth more than the fixes: **a take is a delta against a pose captured once, and nothing about the recorded
file says what that pose was.** Everything below is measured; the numbers come from the harness runs named in
each section, and the takes themselves are ordinary game clips.

## What a take actually is

1. `UnityHumanoidVMDRecorder.Initialize()` builds the **reference**: `animator.enabled = false`, then
   `UmaContainerCharacter.ResetBodyPose()`, `UpBodyReset()`, `UmaApose.IntoAPose()` (the 38.5° A-pose the motions
   are authored against), then `new BoneGhost(...)` which stores each bone's **world** rotation, then
   `BackToTPose()`.
2. `BoneGhost.GhostAll()` runs once per sample and gives each ghost a world rotation:
   `liveWorld · inv(capturedWorld) · ghostOriginal` — a **delta expressed in world space**.
3. `SaveFrame()` writes `ghost.localRotation`, converted Unity→MMD by `new Quaternion(-x, y, -z, w)`, and the
   recorded local is therefore `inv(ghostParentWorld) · ghostWorld`.

Two consequences, and every bug below is one of them:

* **A pose present at capture but not at sample is written into every key as a constant**, and looks like a basis
  error rather than a sampling error. "Wrong in the file, right in the viewport" is this.
* **The world frame is in the expression.** `liveWorld` and `capturedWorld` are world rotations, so whatever the
  character's placement or orientation is, it lands in the take.

The ghost chain is *not* the live hierarchy: it parents `上半身` to the ghost of `Hip`, skipping `UpBody_Ctrl` and
`Waist`, so their bend is folded into the recorded spine. That is deliberate and lossless for the fold — but it
means those two bones have no key of their own, and anything they hold goes into the bone above them.

## The three defects, in the order they were found

### 1. The viewer's look-at solve was written into takes

The solve (`BipedIK` + `IKSolverLookAt`, `bodyWeight 0.25`, driven at `Camera.main`) bends `spine`/`chest`/`neck`/
`head` on top of the clip, and the sampler reads exactly those bones. Two separate mistakes:

* the suspension lived in the **auto-record coroutine** and around the reference capture, but the **manual Record
  VMD button** (`UmaViewerUI.RecordVMD` → `Initialize()` → `StartRecording()` → sample in `FixedUpdate`) never
  reached it, so it captured a clean reference and sampled a solved pose;
* the flag alone is not enough either — it only takes effect on the container's next `FixedUpdate`.

Fix: the hold lives at **`StartRecording`/`StopRecording`** (`UmaContainerCharacter.SuspendLookAt()`, which also
zeros the solver's `IKPositionWeight`), so every path samples what its reference was captured against. Take *Look
at Camera* is off by default in the viewer as well.

Measured, spine offset against a known-good recording of the same clip: **68.79° with the solve live at the
samples, 34.91° with it off, 170.11° with it switched off after loading** — i.e. the size tracked the session, and
a session that had been looking at the camera kept its pose on the rig.

### 2. The rest pose was incomplete, and applied in the wrong order

`ResetBodyPose()` restored **one table, and that table held only the skinned bones**. `UpBody_Ctrl` and `Waist`
are not skinned, so they kept whatever pose the session had left on them — and they sit *between* the hips and the
spine. Measured at the moment of capture, from the dump's `reference` rows and frame 0:

```
hip    world yaw  0.0°
spine  world yaw  93.9°      ← the turn is on a control bone in between
```

The take then came out with a **constant 94.00° of yaw on the spine and everything above it, legs untouched** — and
across the reported takes it was 23°, 94°, 122°, 144° and 180° for the *same clip and model*, because it was the
session's pose rather than the clip's.

Two parts to the fix, and the second is why an earlier attempt made things **worse**:

* the rest table now also captures each skinned bone's **ancestors** up to the character root;
* `ResetBodyPose()` applies the table **parents-first**. The values are world transforms; applying a parent after
  its own descendant drags that descendant off the value it was just given, and with the control bones in the table
  that re-creates the offset. (The earlier attempt appended the ancestors last and dragged the already-restored
  spine, chest, neck, head and arms by the full session deviation.)
* `UpBodyReset()` prefers the table too, instead of `UpBodyPosition/UpBodyRotation`, which are read in
  `Initialize()` — *after* the idle has started and the solve has moved that very bone.

Result on the same clip: **94.00° → 6.68°** on the spine, every other bone from ~100° to under 12°.

### 3. The ghosts were written children-first, so frame 0 was wrong

`GhostAll()` writes each ghost's **world** rotation, and a child's local is fixed against its parent's world *at
the moment it is written* — but the loop went in dictionary order. A child written before its parent computed its
local against a stale parent, worst on the first frame, when every parent still sits at its capture state. From
the dump, frame by frame:

| frame | live head step | recorded head step |
|---|---|---|
| 0 → 1 | 0.4° | **29.5°** |
| 1 → 2 | 0.5° | 0.5° |

Fix: `GhostAll` walks the ghost hierarchy **parents-first** (computed once per ghost set; the hierarchy does not
change during a take). After: the take's first step is 0.0499 against a typical 0.0736, `Head` is no longer an
opening outlier, and **51 of 52 bones are identical** to the take recorded before the change — the fix moved only
the head.

Note the *same class of bug* appeared twice, at two levels (rest pose, then ghosts): **nothing that applies
absolute world transforms may do so in arbitrary order.**

## The instrument

* `Tools\vmd_inspect.py` — `summary`, `loop`, `motion`, and **`diff`**. `diff` separates a *constant* per-bone
  difference (a basis error: the reference pose) from one that *moves with the animation* (a sampling error), and
  pairs up tracks that changed bone name. `motion` reports, per bone, the ratio of its first step to its own
  typical step — this is what named `Head` at 71.8× when the summed-over-bones check the recorder uses could not
  see it.
* `UMA_GHOST_DUMP=1` writes `%TEMP%\ghost_dump.jsonl`: one `reference` row per bone (the pose the take is a delta
  against), per-frame rows (written value, live value, both parent frames), and `skipped` rows for control bones
  the ghost chain does not represent. `Tools\ghost_dump_check.py` reads it. **This is the instrument that answered
  "what pose was the rig in at capture"**, which the vmd cannot.
* `Tools\vmd_inspect.py diff <known-good> <new>` plus a **reference recording** is enough to see the size and the
  character of a regression within seconds.

## How the regression was isolated without the user running the viewer

1. The clip was identified from the user's **player log**
   (`%USERPROFILE%\AppData\LocalLow\<company>\<product>\Player.log`), which printed the asset path
   (`…/anm_eve_type00_dance_minuet03_loop`) and the recorder's own step log.
2. The v3.1 line was built from source — `git worktree add C:\tmp\wt-master origin/master`, copy the current
   `Config.json` (for the game path) into it, and run its own `Tools\headless_export.ps1 -ProjectPath …`. The first
   run in a fresh worktree pays a full asset import; after that a take is ~1.5 min.
3. Both harnesses record the same clip; compare against the user's baseline and against each other.
4. `UMA_GHOST_DUMP=1` on both sides for the capture-vs-sample questions.

**The harness itself needed three fixes before any of that was trustworthy** (all harness-only, no recorder
behaviour): wait for the character's animator before applying `-umaMotion` (`LoadAnimation` is a silent no-op
before it exists); record the clip that was **asked for** rather than the clip the state machine reports right
after `Play("motion_1")` (which is the clip being transitioned *from*); re-apply the motion if the load path takes
the animation layer back and log which clip is actually playing; and flush the dump from `StopRecording` so the
manual button produces one too.

## Traps, each paid for

* **"Differs from the reference recording" is not the same as "is broken".** The known-good v3.1 baseline carries
  the head jerk itself (`Head` 71.7× its own typical step) — the fix makes takes *better* than the baseline, so a
  correct take legitimately differs from it at the head on frame 0.
* **`VmdKeyReductionLevel` confounds every comparison.** The user's dev `Config.json` had `2` (every second key)
  against the reference's `1`: 2065 bone keys against 3952. Halving keys distorts fast-moving bones most, which
  looked like "the right hand's fingers are wrong" because that hand moved twice as much in that clip. Always
  compare takes recorded with the same value.
* **The harness is deterministic**: same build, same clip, two runs → 52/52 bones identical. So a difference
  between two harness runs is a *code* difference, not noise — that is what made bisection possible.
* **A world-space reference makes a take depend on the character's placement.** Measured: the hip's world yaw at
  capture 0.0° vs 93.9° at the first sample, and the recorded hip at frame 0 was exactly 93.9°. Don't compare
  takes recorded with the character posed or placed differently.
* **A local rotation compared against a stored *world* one reads as a spurious 180°** on every bone deep in a
  chain. That was a bug in the first version of the reference dump, and it looked exactly like the defect being
  hunted. `InitBoneTransform` stores world transforms; compare like with like.
* **Judge a first-frame outlier per bone.** The recorder's own `DropAnomalousLeadIn` sums the step over every bone
  and needs 3× the interior maximum; the opening summed to 38° against an interior spike of 104°, so a bone moving
  71× its own typical step was invisible to it. (In one take it did fire — and then the take lost a frame, 75
  against the clip's 76, which shifts every later comparison by one frame.)
* **`TotalFrames` / key counts are a fair comparison only at the same reduction and the same drop count.**
* **Running the viewer from the Editor is a different environment** from a player build (idle already running, scene
  in memory, different frame timing), which is why a "which build made this take" question is worth answering from
  the log before believing a measurement. `[Uma] look-at at load: on|off` and `[VMD] recording session started: the
  look-at solve is held off for it` are printed for exactly that.

## Open, and honestly unfinished

* **A residual 6.68° on the spine** against the v3.1 baseline after all three fixes (it was 94.00°). Not explained;
  the candidates are the load-time value of `Waist` or the A-pose reference, and neither has been measured.
* **`Toe_L` (3.7× its own typical step) and `Shoulder_R` (3.0×, absolute 0.0023)** still show small opening steps.
  Not established whether these are animation or artefact.
* **Player vs Editor parity is unverified** for every fix in this document: they were all proved in the batch
  harness. Given how often that gap has mattered, one take from a real build is worth doing.
* **Whether *Look at Camera* still changes a recording** now that the reference pose is also reset correctly has
  not been measured; the takes that showed the toggle mattering all predate these fixes. The default is off
  regardless.
* Control bones the ghost chain skips (`UpBody_Ctrl`, `Waist`) have no key of their own by design. If their motion
  ever needs to survive into MMD independently of the spine, that is a model/format question, not a recorder one.

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
