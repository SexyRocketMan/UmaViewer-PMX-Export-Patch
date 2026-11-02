# Prop names: does the game have any, and what can we label with instead

Research note, no implementation, written to settle one question before building a browsable prop UI:
**the prop ids (`pfb_chr_prop1003_10`) are unreadable, so is there a human name anywhere in the game data to
show instead?** Short answer: **no.** There is no name field, no name table and no naming convention to borrow —
but the props do carry a partial, component-level vocabulary in their bone names, which is enough to *seed* a
labelling pass and to give an unlabelled prop a fallback label.

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

Scanning 150 props from the `prop` family (`-umaScanProps prop -ScanCount 150`, which reads each prefab's bone
list without exporting):

* **59 of 150 (39%) have at least one bone whose name carries a word** beyond `Root`;
* the rest are a single skinned renderer on a bone called `Root` — nothing to read;
* the vocabulary is **component-level, not object-level**: `Page` (33 props), `Cover`, `Tea`, `Saucer`, `Rice`,
  `Glove`, `Paper`, `Hashi`, `Acc`, `cloth`.

The book is the clearest case: `Cover_L`, `Cover_R`, `Page0_0`, `page0_1`, `Page1_0`… is enough to say "a book",
and it is exactly what a label should say. But `Page` alone cannot distinguish two different books, and a
single-`Root` prop cannot be named at all.

## What this means for the plan

* **The VLM/render pipeline is warranted** — there is nothing to read, so the label has to come from the pixels
  (or from a human). The scale is friendly: **1 229 `prop` prefabs** (+145 `toonprop`, +3 `richprop`), and a
  thumbnail render per prefab is the same work that makes the prop browser useful in the first place.
* **Seed it with the bone vocabulary.** A prop whose bones say `Page`/`Cover` is a book; a run that starts from
  "these 33 are books" needs far less VLM review, and the same words give every unlabelled prop a *fallback*
  label ("Cover/Page") rather than a bare id.
* **Keep the labels out of the game data.** They are our annotation, not the game's: a sidecar keyed by prefab
  name (next to `Config.json`, or a `.json` in the prop cache) survives re-exports and cannot corrupt an export.
  A human correction pass then edits that file only.
* **The id stays visible** next to any label. It is the stable key: `pfb_chr_prop1003_10` is how a user reports a
  bad classification, and how the label file is joined.

## Not established

* Whether the **other** families have better words — `toonprop` (145 prefabs, described elsewhere as one
  held/effect prop per character) and `richprop` (3) were not scanned here, and `attach` (321 entries, real
  shaded accessories) may well name its parts better. Worth the same `-umaScanProps` sweep before assuming the
  39% figure generalises.
* Whether the **texture set** can help: the book's single map is an id, but a prop with several maps might name
  its parts (`_wet`/`_dirt` aside).
* Nothing was rendered. The claim "pixels carry what the bones do not" is an assumption of the plan, not a
  measurement.
