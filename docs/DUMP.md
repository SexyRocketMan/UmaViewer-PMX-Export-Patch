# The dump a browser reads

`dump models` writes a folder a web service can serve as static files. This is what is in it, what each number means,
and how to check and repair it. The commands are in [CLI](CLI.md); the page that consumes it is a separate project
(`uma-model-viewer`) whose README covers serving, auth and the viewer itself.

## The shape

```
D:/web/
  index.json                       the whole dump in one file
  characters/1001_00.pmx           one file per character costume
  characters/Texture2D/*.png       shared by every character model
  props/pfb_chr_prop1003_10.pmx    one file per prop
  props/Texture2D/*.png            shared by every prop
  packs/character/1001_00.zip      optional: one model + only its maps
  thumbs/character/1001_00.png     optional: a 256 px preview
  labels.json                      optional, hand-written: names for props
```

**Textures are shared per kind, not per model.** A PMX names its maps as relative paths - `Texture2D/tex_x_diff.png`,
resolved next to the model - so models and maps must sit side by side, and one folder per kind lets models share them
(four costumes of character 1001 share 48 of their 180 maps). Everything that opens a PMX resolves it with no help.

**`index.json`** (schema `umaviewer-models/1`) is per model: kind, id, costume, its name (japanese and english), the
`costumeName` the game gives that outfit, the prop `family`, the model/texture/preview paths relative to the root, the
maps it needs, its byte size, its **size**, its bone and hand-bone counts, its pack, and any failure. Plus the totals:
how many were planned, written, failed, and how many bytes, texture files, packs and previews there are.

## The number that used to lie: size

`size` is the box of the model's **vertices**, in PMX units, read out of the PMX after it is written. It used to be
`Renderer.bounds`, which for a skinned mesh is the box of its *bones*: one stray bone wrote character 1001 as
16.7 x 6.2 x 25.9 units while she stands 1.6, and a browser printed that as "size".

**One PMX unit is 8 cm.** A PMX stores vertices twelve and a half times the scale people think in, and nothing in the
file says so - it is the convention the rest of this project works in (`--import-scale 0.08` for the Blender tools, the
reciprocal of `--scale 12.5` in `Tools/chain_drive_from_vmd.py`). So a 20.2-unit character is 1.62 m, and the browser
converts at the point of display. Measured on a full dump, character heights land between 159 and 172 cm, which is the
check that the units are right: 20 m or 16 cm would both have looked plausible as bare numbers.

## Previews, and why the framing is measured

`--thumbs` renders each model to a square PNG (256 px unless `--thumb-size` says otherwise) while it is loaded, and
records it as `thumb`. One camera render per model, on a run that already has the model in memory.

The camera is framed by **rendering and reading the alpha channel back**, not by computing a distance from the bounds:
one pass would otherwise be off by the same factor that made the sizes wrong. Each pass measures what was drawn, sets
the distance from the drawn size and the aim from the drawn offset, and stops once the model covers about 84% of the
square and is centred within a few pixels. `Tools/dump_report.py` reports the sampled fill, because a preview that
drew nothing still writes a valid PNG.

## Checking it

```powershell
python Tools/check_dump.py D:/web --packs --thumbs     # self-consistent?
python Tools/dump_report.py D:/web --sample 24         # what is in it, and does it look right?
python Tools/check_tails.py D:/web/characters          # does every joint point at the joint MMD points it at?
```

`check_dump.py` is the gate: every model present, every map on disk, byte counts agreeing, packs containing what they
should, previews that are actually pictures. It exits 1, so it can gate a deploy. It reports a dump as **incomplete**
when the index says more models were planned than it lists - which is what an interrupted run looks like.

`dump_report.py` is the reading: models per kind and family with their bytes, size ranges in centimetres with any
implausible model named, costume-name coverage, preview framing, and the failures if there are any.

`check_tails.py` is the orientation gate: a PMX stores no bone axis, so the tail *is* the frame a motion rotates
around, and a wrong one never fails loudly - the file opens, the vertices are right, the bone just leans the wrong way
once animated. It reads the tails and checks each joint against the joint MMD points it at (the thigh at the knee,
the elbow at the wrist, the wrist at the middle finger, the hip up the body), exits 1 below a floor, and prints which
costumes disagree.

## Repairing it instead of rebuilding it

A dump is hours of work, so nothing about it should require re-exporting to fix a number:

|problem|answer|
|---|---|
|a model's file, maps or pack is missing or truncated|re-run the same command - the skip test re-reads each PMX and re-exports only what is incomplete|
|`size` or the map list is wrong, or `costumeName` is missing|`dump models D:/web --refresh` - rewrites what can be read back from the models, exports nothing|
|a preview is missing|re-run with `--thumbs` - completeness includes the preview, so only the models lacking one are re-exported|
|the index is fine but the folder moved|nothing: paths in the index are relative, and `root` is the dump's name, not its path|
|a prop needs a human name|edit `labels.json` beside the index - see below|

`--refresh` also runs for the models a resumed dump skips, so a dump made before a fix repairs itself on the next run
over it.

## Labels

Props have no name in the game, only a prefab id (`pfb_chr_prop1003_10`), and the index is generated - so a name
written into it is lost the next time the dump runs. Labels are a separate file instead, merged by the page at load:

```json
{ "schema": "umaviewer-labels/1",
  "labels": { "pfb_chr_prop1003_10": { "name": "Book, small", "tags": ["book"] } } }
```

Correcting one is an edit and a reload; labelling can happen at any pace, in any order; an unlabelled model keeps
showing its prefab id, which is what the list has always done. The page searches labels and tags.

## Sizes, measured

On this project's install, per model including its pack and preview:

| | |
|---|---|
| character (PMX + its maps + pack + preview) | ~12.5 MB |
| prop | ~1.8 MB |

| the full run | with `--packs` | without |
|---|---|---|
| 400 character costumes + 1 377 props | ~7.5 GB | ~4.5 GB |

Transfer is a different number from disk: a character is ~4 MB over the wire (a gzipped PMX of ~1.05 MB plus ~2.9 MB
of PNG maps), and a PMX compresses to 19% while PNGs do not compress at all. A visitor pays that once per model, then
revalidates with a 304. `--packs` doubles the disk and not the traffic.

## Serving it

It is static files. Anything that serves a folder with gzip and an `ETag` will do; the companion project's
`tools/serve.py` is the one written for it (`--compress` is worth it: a PMX is 19% of its size and `index.json` about
10%). Keep the origin on loopback and put a proxy in front of it for TLS and access control - `deploy/README.md` in
that project walks through a Cloudflare Tunnel with Access, which needs nothing installed by the visitor.
