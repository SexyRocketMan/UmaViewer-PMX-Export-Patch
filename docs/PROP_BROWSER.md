# The prop grid: showing props instead of naming them

How the prop panel's 1 337 ids became a page of pictures, why it is a view of the panel rather than a window,
and what was measured to get there. Companion to [`PROP_LABELS.md`](PROP_LABELS.md), the research this came out
of: the game ships no name for a prop, only a quarter of them name themselves through their bone names, so props
have to be *looked at*.

## What it is

The props panel has two views of one list:

| | list | grid |
|---|---|---|
| what a page is | text rows, one per prop | cards with a picture, a name and a mark box |
| paging | the panel's own controls (`PageManager`) | **the same** |
| search | the panel's own box | **the same** |
| keyboard walk | `PageManager.MoveSelection` | **the same** |
| where it draws | `Prop List` scroll view | a second scroll view, in `PropGridArea` |

The switch is the **Grid view** row under the page controls (or `UmaViewerUI.PropViewRow` when one is placed in
the scene). The two views share everything except what a page is made of - one `PageManager`, one search box, one
set of page controls - so switching cannot leave the two disagreeing about what is on screen, and the list is
still there for a prop whose id you already know. Clicking a card loads that prop, exactly as a row does. The
square in a card's corner marks it for **Export selected**, which writes every marked prop to a folder as `.pmx`.

## The one scene object it wants

A `RectTransform` assigned to `UmaViewerUI.PropGridArea`: the space the grid should fill. Nothing else is needed;
the grid builds its scroll view, cards and thumbnails inside it.

When the field is empty the grid makes its own guess at that space - the prop list's vertical extent, widened to
the whole panel - and logs a warning saying so. The guess is legible (a 1904x1248 area on a 1440p window) but it
is a guess: an area placed where the grid belongs is what makes it fill exactly what was meant.

The two control rows (`PropViewRow`, `PropExportRow`) can also be placed in the scene and assigned; left empty
they are built from the panel's own prefabs under the page controls. Rows placed in the scene are the ones that
match the layout they were designed for.

## How the pictures are made

One stage, one camera, one texture. The models stand in a row 4 000 units below the origin - past the viewer
camera's 1 000 unit far plane, so the game camera can never see them - and a single orthographic camera frames
them all into one `RenderTexture` of 256 pixel cells. Each card is a `RawImage` reading its own square through
`uvRect`, so a page of 70 cards costs **one** render per frame rather than 70 cameras and 70 targets.

The parts that were not obvious, each found by measuring rather than by looking:

* **A model's bounds are not its shape.** Scaling to `Renderer.bounds` left every book at a third of its cell:
  a skinned prop's bounds follow its mesh and its bones, not the transform that was just scaled. The stage now
  measures what it *drew* and corrects. Books converge `8x27 px -> 52x162 -> 65x200` against a 200 px target.
* **A render only lands on a frame boundary.** Two passes in one editor tick measure the same picture twice; a
  sixfold scale-up between passes, with the renderer's bounds agreeing it had grown sixfold, came back as the
  identical 8x27 pixel sliver. Every pass gets its own frame.
* **The footprint of a turning object is the union of its projections.** Fitting one angle left props cut in half
  at 90 degrees, and combining two angles as `sqrt(w0² + w90²)` fixed the size but not a sideways drift, because
  the centre of a projection is not the centre of rotation. The fit now draws the page at 0, 45, 90 and 135
  degrees, merges each cell's four footprints, scales so the merged one fits and moves the model so its middle
  sits on the cell's middle. `-umaPropGridCheck` reports overflow at those angles: **0 of 70 cells at 0, 45, 90
  and 135 degrees**, against 4, 2, 0 and 10 before.
* **The stage must not be parented to the UI.** A canvas hands its children a scaled transform (0.33 on a 1440p
  screen); the models shrank with it while the camera's orthographic size stayed in world units, so the page drew
  as a blob in the middle of the atlas and only 4 of 36 cells had anything in them. The stage is a root object.
* **Centre a holder, not the model.** Moving the model so its middle sits on the turntable pivot leaves it
  orbiting that pivot as the table turns, which swings a prop modelled off its own origin out of its cell. The
  offset lives on a holder between the pivot and the model.
* **An inactive panel never draws.** A grid inside a panel that is not open never advances its coroutine, so the
  thumbnails silently stay blank; the routines run on `UmaViewerUI` instead, which is always live.

## What a page comes out as

`-umaPropGridCheck` opens the props panel, switches it to the grid, fills a page and reports it. On a 1920x1440
canvas, with the area guessed (no `PropGridArea` assigned):

```
prop grid: grid 1904x1248, 10 column(s) x 7 row(s), page 1 / 20, cards 70/70 [prop 1001_00 170px],
atlas 2560x1792, stage built with 70 cell(s), 0 failure(s)
prop grid pictures: 69 of 70 cell(s) drew something at this turntable angle
cell 0: pfb_chr_prop1001_00  17.8% covered, x 96..160 y 32..231 of 256, mean colour 109,163,201
prop grid at 0/45/90/135 degrees: 0 of 70 cell(s) overflow their square
prop grid click loaded: pfb_chr_prop1001_00
prop grid page 2: page 2 / 20, cards 70/70 [prop 1025_00]
prop grid search 'rich': page 1 / 1, cards 70/70 [prop 1001_00]
prop grid switched back to the list: 1 / 61 (22 row(s))
```

The numbers are the point. A square that drew nothing reads as `EMPTY`, a cell that spills reads as overflow, and
the view switch is checked to leave the list working - none of which a human glancing at a grid would notice.

## What it costs

* A page is one prop loaded per frame, about 65 ms each, so a 70 card page fills in roughly five seconds while
  the panel stays responsive. Cards appear at once; pictures arrive progressively.
* Bundles are tracked per page (`SearchAB` is the same walk the loader did) and released when the page changes, so
  browsing ten pages does not hold ten pages of props. The prop the viewport is showing is kept: unloading its
  bundles would break the model on screen.
* One extra camera render per frame while the grid is up, plus four per framing pass while a page settles.
  Closing the view releases the texture and the models.
* `LoadProp` - a card click or the list's own row - unloads *every* bundle, including a page's. The page is
  redrawn after a card click for that reason.

## Not done

* **No names.** A card shows `prop 1003_10`, not "a book", until there is a label store to read from.
  [`PROP_LABELS.md`](PROP_LABELS.md) has the plan and the numbers.
* **No per-family filter.** The grid lists the same three prop families the list does; search covers ids and
  family names, not "only the held ones" (which the scan can tell, see `Tools/prop_rig_names.py`).
* **Not seen by a human.** Everything above is measured: the layout resolves, the pictures are drawn, nothing
  overflows, the switch works. Nobody has yet looked at the grid in the panel and said it *reads* well - card
  size, font size, how many columns feel right.
