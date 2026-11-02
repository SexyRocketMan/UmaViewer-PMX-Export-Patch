# Prop names: does the game have any, and what can we label with instead

Research note, no implementation, written to settle one question before building a browsable prop UI:
**the prop ids (`pfb_chr_prop1003_10`) are unreadable, so is a human name anywhere in the game data to show
instead?** Short answer: **no.** There is no name field, no name table and no naming convention to borrow —
but the props do carry a partial vocabulary in their bone names: a quarter of the `prop` family names the
object itself (`Hashi`, `Ramen`, `Broom`), which is enough to *seed* a labelling pass and to give an
unlabelled prop a fallback label. The size of that vocabulary, per family, is the sweep in the second half
of this note.

Everything below was read from the installed game data and the viewer's own tooling; nothing here needs the
viewer to run.

## What was checked, and what each place holds

| where | what it holds for props | names? |
|---|---|---|
| manifest (`AbList`, `meta_plain.sqlite`) | asset paths: `3d/chara/prop/prop1003_10/pfb_chr_prop1003_10`, `tex_chr_prop1003_10_diff` | **no** — ids only. `meta_plain.sqlite` in this repo is the manifest graph (`a` = 360 456 assets, `r` = 559 840 relations, `i.version = 1.2`), its `n` column is the path (`//story`, `//root`) |
| `master.mdb` → `character_prop_animation` | 21 rows: `prop_id`, `prop_anim_id`, `layer_index`, `use_state_name` (`S`/`loop`/`E`), `scene_type` | **no** |
| `master.mdb` → `character_prop_system_text` | **1 row**: `prop_id=700200`, `lip_sync_data='snd_voi_outgame_prop700200_0003'` | **no** |
| `master.mdb` → `crane_game_prop` | 2 rows: `type`, `num` | **no** |
| `master.mdb` → `home_prop_setting` | 20 rows: `pos_id`, `personality`, `prop_id`, `attach_node` — placement only | **no** |
| `master.mdb` → `text_data` (95 842 rows, `id/category/index/text`) | No prop catalogue. Searching for `景品` (prize), `プロップ` (prop), `小道具` gives **0 hits**; `ティーセット` (tea set) gives 2, and both are one **gacha/event title** — "カワカミプリンセスの プリンセス☆アフタヌーンティーセット", category 273 — not a prop |
| the exported `.pmx` | material `pfb_chr_prop1003_10`, texture `tex_chr_prop1003_10_diff.png` | **no** |
| the prop's **bone names** | `Cover_L`, `Cover_R`, `Page0_0`, `page0_1`, `Page1_0`, … `Root`, `M_Prop` | **partly** — see below |

So the six `prop`-named tables in `master.mdb` describe *placement, animation and one voice line*; none of them
carries a title, and the game's localisation table has no prop entries at all. A prop's own name simply is not
data the game ships.

## The one signal that does exist: bone names inside the prefab

`-umaScanProps <family>` loads each prefab of a family and prints how it is rigged and up to eight hand
bone and eight other bone names. Swept over every prop-like family in the manifest (one editor boot per
family, ~30 s each):

| family | prefabs in the manifest | scanned | one `Root` bone | own rig, no body bones | worn on the body | hand-rigged | a bone name carries a word |
|---|---|---|---|---|---|---|---|
| `prop` (`pfb_chr_prop*`, what the prop panel shows) | 1 229 | 400 (the 400 whose bundles are local) | 238 (60%) | 138 (35%) | 13 (3%) | 2 | **128 of 391 (33%)** |
| `toonprop` (`pfb_toon_prop*`) | 145 | 145 | 32 (23%) | 59 (42%) | 14 (10%) | **34 (24%)** | 28 of 140 (20%) |
| `richprop` (`pfb_rich_prop*`) | 3 | 3 | 0 | 2 | 1 | 0 | 3 of 3 |
| `attach` (`pfb_attach*`) | 267 | 267 | 0 | 0 | 267 — every one a 66-bone skin | 0 | **0** |
| `mini attach` (`pfb_mattach*`) | 66 | 66 | 0 | 0 | 66 — 37- to 69-bone skins | 0 | 9 (14%, all `Ribbon`) |

Four things fall out of that table.

**The `prop` family is where the words are.** Three in five of its prefabs are one skinned mesh on a bone
called `Root`, and the rest are a small rig of their own — and it is that rig which carries the vocabulary.
`attach` and `mini attach` are not props at all: every one is bound to the character's whole skeleton (66
bones, `Wrist_L` through `Toe_R`), i.e. a second skin worn over the body, so their bone names are the
character's and say nothing about the object. That also explains why 267 `attach` prefabs look like
browsable props in the manifest and are not.

**The vocabulary splits into objects and parts.** The 400 `prop` prefabs scanned name 58 distinct words:

* **object-level** (a label in itself): `Hashi` (18, chopsticks), `Men` (11, in `Hashi_Men_*` — noodles, if
  the romaji means what it looks like), `Wing` (11), `Cap` (7), `Paper` (6), `Paperbag` (4), `Tea`, `Rice`,
  `Fish`, `Straw`, `Karuta` (3, playing cards), `Mirror`, `Saucer`, `Glove`, `Plastic`, `Computer`, `Pen`,
  `Donut`, `Mato` (target), `Chain`, `Bamboo`, `Ramen`, `Buns`, `Carrot`, `Lettuce`, `Meat`, `Onion`,
  `Rope`, `Block`, `Tile`, `Letter`, `Sandbag`, `Duster`, `Bucket`, `Broom`, `Bag`, `Hammer`, `Trigger`,
  `Ticket`, `Button`, `Banana`, `Fan`, `Fin`, `Lure`, `Rose`, `Parachute`, `Cow`, `Belt`, `Pumpkin`,
  `String`;
* **component-level** (says what the object is made of, or what colour it is, not which one): `Page` (23),
  `Cover` (21), `Ribon`/`PinkRibon`, `Shadow`, `Blue`, `White`;
* **unclear**: `Uki` (1 prefab);
* **not words**: suffixes that look like them — `OffsetL`/`OffsetR`, `LineA`/`LineB`, `Mant_FLL` (front
  left lower), `Thigh_rol` (roll), `Scl`, `Efc`. `Tools/prop_rig_names.py` filters these out, which is why
  its 33% is lower than the 39% a plain "is there any bone name besides Root" count gives.

**The book is the awkward case, and it is component-level.** `Cover_L`, `Cover_R`, `Page0_0`, `Page1_0` is
enough to say "a book" — but 23 prefabs name only `Page` and 21 only `Cover`, and `Page` cannot distinguish
two different books. Neighbouring ids do help: `pfb_chr_prop1026_00..07` are the chopsticks props and
`1023_02`/`1023_03` are the saucer-and-tea ones, so the id groups by object even where the words are parts.

**`toonprop` answers a different question better: it says which props are held.** 34 of 145 name a wrist
or a finger, so their skin follows a hand — the prop browser needs that to know where a prop goes. But only
20% carry any word, and much of that (`Rod`, `Liquid`, `Ribbon`, `Leaf`, `Mant`) is a part.

What the words are good for, then: as a **seed** for the 25% of `prop` prefabs that name an object, and as a
fallback label (`Cover/Page` rather than a bare `pfb_chr_prop1003_10`) for the rest of the named ones.

## What this means for the plan

* **The VLM/render pipeline is warranted** — there is nothing to read for two thirds of the family, so the
  label has to come from the pixels (or from a human). The scale is friendly: **1 229 `prop` prefabs**, and
  a thumbnail render per prefab is the same work that makes the prop browser useful in the first place.
* **Seed it with the bone vocabulary, and manage the expectation.** 33% of `prop` prefabs have a word, and
  the split favours objects: **98 of the 391 (25%) name an object** (`Hashi`, `Ramen`, `Broom`, `Pumpkin`),
  **29 (7%) name only a part or a colour** (`Page`/`Cover`, `Blue`/`White`), and the other **60% are one
  `Root` bone** with nothing to read. The `attach` and `mini attach` families are not worth seeding at all.
* **Keep the mount information; it is separate and complete.** The scan's rig line already says whether a
  prop is hand-rigged, worn on a body part, or self-rigged, and that is exactly what the prop browser needs
  to place one. It costs nothing extra to keep next to the label.
* **Keep the labels out of the game data.** They are our annotation, not the game's: a sidecar keyed by
  prefab name (next to `Config.json`, or a `.json` in the prop cache) survives re-exports and cannot corrupt
  an export. A human correction pass then edits that file only.
* **The id stays visible** next to any label. It is the stable key: `pfb_chr_prop1003_10` is how a user
  reports a bad classification, and how the label file is joined.

## Not established

* **The other 829 `prop` prefabs.** Only 400 of the 1 229 have bundles on this machine, so the sweep reads
  the 400 lowest ids. Nothing suggests the naming habit changes further into the family, but that is an
  assumption; `-umaScanProps chr_prop -ScanCount 1229` answers it once the rest are downloaded.
* Whether a prop's **material names** add anything the bones do not. `SummarizeMaterials` already prints
  shader and texture-set names per prop, and every one seen so far is an id (`pfb_chr_prop1003_10`).
* Whether the **texture set** can help: the book's single map is an id, but a prop with several maps might
  name its parts (`_wet`/`_dirt` aside).
* Nothing was rendered. The claim "pixels carry what the bones do not" is an assumption of the plan, not a
  measurement.

