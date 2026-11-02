# The prop grid: showing props instead of naming them

How the prop panel's 1 400 ids became a page of pictures, why it is a separate window, and what was measured to
get there. Companion to [`PROP_LABELS.md`](PROP_LABELS.md), which is the research this came out of: the game
ships no name for a prop, only a quarter of them name themselves through their bone names, so the props have to
be *looked at*.

## What the window is

`Props` panel → **Grid view** (under the page controls) opens it. A page of cards, each one a prop rendered live
from the same bundles the scene loads, turning slowly on a turntable. Clicking a card loads that prop, exactly
as a list row does. The small square in a card's corner marks it for **Export selected**, which writes every
marked prop to a folder as `.pmx`. Unticking **Grid view** (or ✕) closes the window and the list is back.

The list stays the list. This is an overlay, not a replacement: for a prop whose id you already know, the list
is faster, and nothing here should take that away.

## How the pictures are made

One stage, one camera, one texture. The models are laid out in a row 4 000 units below the origin - well past
the viewer camera's 1 000 unit far plane, so the game camera can never see them - and a single orthographic
camera frames the whole row into one `RenderTexture` of 256 pixel cells. Each card is a `RawImage` reading its
own square of that texture through `uvRect`, so a page of 36 cards costs **one** render per frame rather than
36 cameras and 36 targets.

The parts that were not obvious, all of them found by measuring rather than by looking:

* **A model's bounds are not its shape.** Scaling each prop to its `Renderer.bounds` left every book at a third
  of its cell, because a skinned prop's bounds follow its mesh and its bones, not the transform that was just
  scaled. The stage now *measures what it drew*: render, read the cell back, find the opaque pixels, scale by
  target/measured, move the model's middle to the cell's middle, and repeat until it stops changing. On the
  first page of books that converges `8x27 px -> 52x162 -> 65x200` with a 200 px target.
* **A render only lands on a frame boundary.** Two measure-and-fix passes inside one editor tick measure the
  same picture twice; the second "corrects" a change that was never drawn. Measured the hard way: a sixfold
  scale-up between passes, with the renderer's bounds agreeing it had grown sixfold, came back as the identical
  8x27 pixel sliver. Every pass therefore gets its own frame.
* **The stage must not be parented to the UI.** A canvas hands its children a scaled transform (0.33 on a
  1440p screen). The models shrank with it while the camera's orthographic size stayed in world units, so the
  page drew as a small blob in the middle of the atlas and only 4 of 36 cells had anything in them. The stage is
  now a root object.
* **Centre the holder, not the model.** Moving the model so its middle sits on the turntable pivot leaves it
  orbiting that pivot as the table turns, which for a prop modelled away from its own origin swings it out of
  its cell within a second. The offset lives on a holder between the pivot and the model instead.

## What a page comes out as

`-umaPropGridCheck` opens the window headless, fills a page and reports it. On a 1920x1440 canvas:

```
window 1920x1440, grid 1773x1197, 6 column(s) x 6 row(s) = 36 per page,
page 1/38 of 1337 shown (1337 in the manifest), cards 36 [prop 1001_00 170px],
atlas 1536x1536, stage built with 36 cell(s), 0 failure(s)
prop grid pictures: 36 of 36 cell(s) drew something at this turntable angle
cell 0: pfb_chr_prop1001_00  17.6% covered, x 95..159 y 33..231 of 256, mean colour 107,161,200
cell 1: pfb_chr_prop1001_01  17.7% covered, x 95..159 y 33..231 of 256, mean colour 141,138,36
prop grid click loaded: pfb_chr_prop1001_00
prop grid page 2: page 2/38, cards 36 [prop 1006_00]
prop grid search 'rich': page 1/1 of 3 shown, cards 3 [rich 1001_00]
```

The coverage numbers are the point of that output: a square that is *empty* reads as `EMPTY`, and a page that
has lost its pictures would fail the check instead of looking fine to a human who assumes a blank card is a
blank prop. The books come out thin (65 wide against 198 tall) because they are flat and the turntable is
turning; the mean colour is what tells a book from a brick.

## What it costs

* A page is `columns x rows` props loaded at once, one per frame: about 65 ms per prop on this machine, so a
  full page of 36 fills in roughly two seconds while the window stays responsive.
* Bundles are tracked per page (`SearchAB` is the same walk the loader did) and released when the page changes,
  so browsing ten pages does not hold ten pages of props. The one exception is the prop the viewport is
  showing: unloading its bundles would break the model on screen.
* One extra camera render per frame while the window is open (1536x1536, small models). Closing the window
  destroys the stage, its texture and its models.
* `LoadProp` - a card click or the list's own row click - unloads *every* bundle, including a page's. The page
  is therefore rebuilt after a card click; without that, every thumbnail on screen goes blank the moment a card
  is used.

## Not done

* **No names.** A card shows `prop 1003_10`, not "a book". That is intentional until there is a label store to
  read from ([`PROP_LABELS.md`](PROP_LABELS.md) has the plan and the numbers).
* **No keyboard walk.** The list's arrow-key browsing works through `PageManager`; the grid has its own paging
  and does not join it, so arrow keys do nothing here yet.
* **No scene props.** The grid lists the three `3d/chara` prop families, the same set the panel lists. Scenes
  (`3d/env`) have their own panel and their own sizes; the same stage would work for them with a different
  framing.
* **Not seen by a human.** Everything above was measured headless. The layout is right to the number and the
  pictures are verifiably drawn, but nobody has yet looked at the window and said it *reads* well - card size,
  font size, the dimming behind it.
