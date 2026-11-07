# Audio transcripts

The viewer can say what a clip of game audio *is*: the Sound page shows the line above the progress bar and in
the clip rows, and an exported `.mp3` carries it in its ID3 tag. This file covers where that text comes from, how
a clip is joined to it, and what is not covered.

Nothing is downloaded. Two sources in the game's data folder hold every line:

| The clip | Where its text is |
|---|---|
| recorded character voices (`snd_voi_home_*`, `snd_voi_training_*`, `snd_voi_outgame_*`, `snd_voi_minigame_*`, `snd_voi_title_*`, `snd_voi_gacha_*`, `snd_voi_race_*`, `snd_voi_teamst_*`, `snd_voi_live_*`, ...) | the master database (`master/master.mdb`), table `character_system_text` |
| story dialogue (`snd_voi_story_*`) | the story timeline assets, `story/data/XX/YYYY/storytimeline_*` |

Music (`snd_bgm_*`) and effects (`snd_sfx_*`) carry no text. Live songs carry lyrics, which the viewer reads from
the songs' own asset (`live/musicscores/m<id>/m<id>_lyrics`).

---

## What a clip is called

The sound assets are CRI containers, and neither holds text:

* the **`.awb`** is a list of HCA audio payloads, which is what the viewer plays and exports;
* the **`.acb`** next to it is the cue sheet: it maps a *cue name* to the awb wave id it plays
  (`snd_voi_home_100100_0021` is cue 21 of that sheet).

The cue name keys all the text, so `SoundCues` (in `Assets/Scripts/Audio/AudioTranscripts.cs`) reads the acb through
`AcbReader`/`AcbNameLoader` and asks, per clip, which cue plays that wave.

Two details to keep in mind:

* `AcbNameLoader.LoadWaveName` takes a `memory` flag and **skips a streaming waveform when asked for memory ones**.
  Uma voice acbs record as streaming, so `memory` must be `false`, or nothing comes back.
* A waveform's `Id` is `0xFFFF` in these files and the real wave id is in `StreamAwbId`, which the loader handles.
  The clip order the loader walks (`awbReader.Waves`) is the order the Sound page lists.

## The join for a recorded character voice

`character_system_text` holds one row per recorded line:

| column | meaning |
|---|---|
| `character_id`, `voice_id` | who, and the line's id inside that character's voice set |
| `cue_sheet`, `cue_id` | the cue sheet and cue the audio is played from |
| `lip_sync_data` | the cue *name*, e.g. `snd_voi_home_100100_0021` |
| `text` | the line, in Japanese, with the game's own line breaks |

The join is `lip_sync_data == cue name`. The table is not consistent about it: 1418 rows carry `..._00` where the
sheet and cue id spell `..._0000`, so `UmaDatabaseController` indexes both the `lip_sync_data` value and a
`cue_sheet_cueid` spelling, picking up 30,668 cue names out of 32,919 rows.

The speaker is the character name from `text_data` category 6 - the rows the character list is built from.

## The join for story dialogue

A story episode is one asset, `storytimeline_<story id>`: a `Gallop.StoryTimelineData` MonoBehaviour whose
`BlockList[i].TextTrack.ClipList[j]` entries are `Gallop.StoryTimelineTextClipData` objects, one per line. Each
line carries `VoiceSheetId` (the cue sheet) and `CueId` (the cue inside it) - exactly what the awb's cue names say -
plus `Name` (the speaker), `Text` (the line) and `StartFrame`/`ClipLength`.

The classes are the game's own, declared with the fields the viewer uses in `Assets/Scripts/umamusume/Gallop/Story/
StoryTimeline.cs`: `StoryTimelineData`, `StoryTimelineBlockData`, `StoryTimelineTextTrackData` and
`StoryTimelineTextClipData`. Two things matter:

* **the identity**: Unity binds a serialized MonoBehaviour to a script by assembly, namespace and class name. The
  game ships these as assembly `umamusume`, namespace `Gallop`, which is what `Assets/Scripts/umamusume.asmdef`
  compiles to - the mechanism the other game-data classes here use too.
* **the references**: a dialogue line is *not* an asset of its bundle. `AssetBundle.LoadAllAssets` returns only the
  timeline, so the lines are reachable only through the timeline's own references: without the
  `BlockList` -> `TextTrack` -> `List<StoryTimelineTextClipData>` declaration, the bundle holds nothing but the
  timeline.

### Which timelines belong to a cue sheet

The sheet id's shape decides which timeline answers:

* **a story's own sheet** (`041047007`) - `snd_voi_story_041047007` is voiced by `storytimeline_041047007`, in
  `story/data/04/1047/`. The sheet id *is* the timeline id, so one bundle answers for the whole awb.
* **a character's voice bank** (`051015001`) - that is `<05><chara 1015><001>`, and its lines are spread over every
  episode of that character's arc: `story/data/11/1015/`, `story/data/50/1015/`, `story/data/80/1015/` and so on.
  The folder name is the sheet's middle four digits, so the resolver reads every timeline in every folder with that
  number.
* **a home scene** (`00000_01_1032001`) - one conversation, in `home/data/00000/01/hometimeline_00000_01_1032001`:
  again a single asset whose id is the sheet. Home scenes are serialized exactly like story episodes, with the same
  classes, so the same code reads them.

A story's own sheet or a home scene is one bundle (~30-60 ms); a voice bank is a couple of hundred (~5 s). The
result is cached per sheet for the session, and only the voice banks are worth warning about, which is what
`AudioTranscripts.NeedsFolderScan` answers.

### When the acb is missing

A clip's cue id normally comes from the `.acb` next to its `.awb`. Some assets have an awb but never had their acb
downloaded; the clip list still comes from the awb, so those clips keep their index, and the index is what the cue
id almost always is. That is why the Sound page labels clips by index first and uses the cue name when the acb has
one: a missing acb loses the cue *names*, not the text.

### One clip, several lines

A voice bank is smaller than the arc it serves, so a cue can serve more than one line - a long take over several
text boxes, the same take reused between episodes. The viewer lists all of them: the clip row says how many more
there are (`3 (+13)`), the panel shows the first twelve lines, and the tag's lyrics frame gets the whole list. A
story's own sheet is one line per cue, so this mostly affects the `05…` banks.

## What is covered

Measured over the whole sound index (16,209 awb assets, 92,054 clips) with `Tools/uma_transcript_survey.py`:

| family | clips | with text |
|---|---|---|
| `snd_voi_story` (story dialogue - not in the master data, see the join above) | 33,286 | from the timeline assets: exact where a sheet is a story's own or a home scene, one clip may answer for several lines on a character's voice bank |
| `snd_voi_home` | 10,235 | 97.7 % |
| `snd_voi_training` | 8,252 | 93.4 % |
| `snd_voi_outgame` | 3,589 | 98.8 % |
| `snd_voi_minigame` | 2,244 | 100 % |
| `snd_voi_race` | 2,015 | 78.3 % |
| `snd_voi_teamst` | 1,449 | 99.9 % |
| `snd_voi_event` | 1,032 | 52.4 % |
| `snd_voi_title`, `_live`, `_roulette`, `_valentine`, `_aimteam`, `_gacha` | 1,436 | 99-100 % |
| `snd_voi_jky`, `snd_voi_cmt` (race commentary) | 8,087 | 45.9 % / 71.6 % - from `race_jikkyo_message` / `race_jikkyo_comment`, **not yet wired into the viewer** |
| `snd_voi_chara` (character commentary reactions) | 9,831 | none found in the master data |
| `snd_bgm_*`, `snd_sfx_*` | 26,000+ | none by nature |

The text is whatever the installed client has: a Japanese client gives Japanese lines. A clip of an asset in the
index but not downloaded shows no text, since that would mean a network round trip for a label.

## Checking it without the UI

```powershell
# what does a voice asset resolve to? (sheet, asset name, or full asset path)
./Tools/headless_export.ps1 -Transcript 041047007
./Tools/headless_export.ps1 -Transcript snd_voi_story_00000_01_1032001
./Tools/headless_export.ps1 -Transcript snd_voi_home_100100

# export one clip to an mp3 and read the tag back out of it
./Tools/headless_export.ps1 -ExportAudio snd_voi_story_041047007 -ExportClip 3 -Out out.mp3
```

`-umaTranscript` prints the lines a sheet carries, then clip by clip what the viewer would label. `-umaExportAudio`
writes the mp3 and logs the ID3 frames it finds afterwards.

The same reading without Unity is `Tools/mp3_transcript.py` - standard library only:

```powershell
python Tools/mp3_transcript.py D:/out/line.mp3
python Tools/mp3_transcript.py "D:/rips/*_transcribed.mp3" --json
```

It prints the title, speaker, story and full text, and exits non-zero if a file carries no transcript, which makes
it usable as a check over a folder of exports.

## The tag

`Assets/Scripts/Exporters/Id3Tag.cs` writes an ID3v2.3 tag in front of the mp3 once LAME has closed it (the tag has
to precede the audio):

| frame | contents |
|---|---|
| `TIT2` | the line itself |
| `TPE1` | the speaker |
| `TALB` | the story title (`Album`), when the clip is story dialogue |
| `COMM` | the asset and clip it came from |
| `USLT` | every line of the clip, as `speaker: line` |

Text frames are UTF-16 with a byte order mark, which players need for Japanese; the lyrics and comment frames carry
the language code `jpn` and an empty descriptor. A clip with no text is exported untagged.
