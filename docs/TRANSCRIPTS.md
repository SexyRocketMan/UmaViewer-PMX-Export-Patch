# Audio transcripts

The viewer can say what a clip of game audio *is*: the Sound page shows the line above the progress bar and
in the clip rows, and an exported `.mp3` carries it in its ID3 tag. This document records where that text
comes from, how a clip is joined to it, and what is not covered.

Nothing new is downloaded. Two sources already in the game's data folder hold every line:

| The clip | Where its text is |
|---|---|
| recorded character voices (`snd_voi_home_*`, `snd_voi_training_*`, `snd_voi_outgame_*`, `snd_voi_minigame_*`, `snd_voi_title_*`, `snd_voi_gacha_*`, `snd_voi_race_*`, `snd_voi_teamst_*`, `snd_voi_live_*`, ...) | the master database (`master/master.mdb`), table `character_system_text` |
| story dialogue (`snd_voi_story_*`) | the story timeline assets, `story/data/XX/YYYY/storytimeline_*` |

Music (`snd_bgm_*`) and effects (`snd_sfx_*`) carry no text. The live songs do, but as lyrics, which the
viewer already reads from the songs' own asset (`live/musicscores/m<id>/m<id>_lyrics`).

---

## What a clip is called

The sound assets are CRI containers, and neither of them holds text:

* the **`.awb`** is just a list of HCA audio payloads, which is what the viewer plays and exports;
* the **`.acb`** next to it is the cue sheet: it maps a *cue name* to the awb wave id it plays
  (`snd_voi_home_100100_0021` is cue 21 of that sheet).

The viewer never needed the `.acb` before, because playing and exporting only need the awb. The cue name is
what all the text is keyed by, so `SoundCues` (in `Assets/Scripts/Audio/AudioTranscripts.cs`) reads the acb
through the existing `AcbReader`/`AcbNameLoader` and asks it, for each clip, which cue plays that wave.

Two details that cost time to find, and are worth keeping in mind:

* `AcbNameLoader.LoadWaveName` takes a `memory` flag, and it **skips a streaming waveform when asked for
  memory ones**. Every uma voice acb records its voices as streaming, so `memory: true` returns nothing at
  all; the flag has to be `false`.
* A waveform's `Id` is `0xFFFF` in these files and the real wave id is in `StreamAwbId`, which the loader
  handles. The clip order the loader walks (`awbReader.Waves`) is the order the Sound page lists.

## The join for a recorded character voice

`character_system_text` holds one row per recorded line:

| column | meaning |
|---|---|
| `character_id`, `voice_id` | who, and the line's id inside that character's voice set |
| `cue_sheet`, `cue_id` | the cue sheet and cue the audio is played from |
| `lip_sync_data` | the cue *name*, e.g. `snd_voi_home_100100_0021` |
| `text` | the line, in Japanese, with the game's own line breaks |

The join is `lip_sync_data == cue name`. The table is not quite consistent about it: 1418 rows carry
`..._00` where the sheet and cue id spell `..._0000`, so `UmaDatabaseController` indexes both the
`lip_sync_data` value and a `cue_sheet_cueid` spelling, and picks up 30,668 cue names out of 32,919 rows.

The speaker is the character name from `text_data` category 6 - the same rows the character list is built
from.

## The join for story dialogue

A story episode is one asset, `storytimeline_<story id>`, and it is not a text blob: it is a
`Gallop.StoryTimelineData` MonoBehaviour whose `BlockList[i].TextTrack.ClipList[j]` entries are
`Gallop.StoryTimelineTextClipData` objects - one per line. Each line carries `VoiceSheetId` (the cue sheet)
and `CueId` (the cue inside it), which is exactly what the awb's cue names say, plus `Name` (the speaker),
`Text` (the line) and `StartFrame`/`ClipLength` for where it sits in the episode.

Reading those needs the game's own classes, which is how the rest of this project already reads game data:
`Assets/Scripts/umamusume/Gallop/Story/StoryTimeline.cs` declares `StoryTimelineData`,
`StoryTimelineBlockData`, `StoryTimelineTextTrackData` and `StoryTimelineTextClipData` with the fields the
viewer uses. Two things matter there:

* **the identity**: Unity binds a serialized MonoBehaviour to a script by assembly, namespace and class
  name. The game ships these as assembly `umamusume`, namespace `Gallop`, which is what
  `Assets/Scripts/umamusume.asmdef` compiles to - the same mechanism that already makes `AssetHolder`,
  `CySpringDataContainer` and the live-timeline classes work.
* **the references**: a dialogue line is *not* an asset of its bundle. `AssetBundle.LoadAllAssets` returns
  only the timeline (one object), so the lines are only reachable through the timeline's own references.
  Declaring `BlockList` -> `TextTrack` -> `List<StoryTimelineTextClipData>` is what makes Unity load them;
  without it the bundle appears to contain nothing but the timeline.

### Which timelines belong to a cue sheet

A sheet id comes in two shapes, and which timeline answers for it follows from the shape:

* **a story's own sheet** (`041047007`) - `snd_voi_story_041047007` is voiced by `storytimeline_041047007`,
  which sits in `story/data/04/1047/`. The sheet id *is* the timeline id, so one bundle answers for the
  whole awb.
* **a character's voice bank** (`051015001`) - that is `<05><chara 1015><001>`, and its lines are spread over
  every episode of that character's arc: across `story/data/11/1015/`, `story/data/50/1015/`,
  `story/data/80/1015/` and so on. Here the folder name is the sheet's middle four digits, so the resolver
  reads every timeline in every folder with that number.
* **a home scene** (`00000_01_1032001`) - one conversation, and its lines are in
  `home/data/00000/01/hometimeline_00000_01_1032001`: again a single asset whose id is the sheet. Home
  scenes are serialized exactly like story episodes, with the same `StoryTimelineData` and
  `StoryTimelineTextClipData` classes, so they are read by the same code.

That is why one lookup can take a moment: a story's own sheet or a home scene is one bundle (~30-60 ms), a
voice bank is a couple of hundred (~5 s), and the result is cached per sheet for the session. Only the voice
banks are worth warning the user about, which is what `AudioTranscripts.NeedsFolderScan` answers.

### When the acb is missing

A clip's cue id normally comes from the `.acb` next to its `.awb` (see above). Some assets have an awb in the
data folder but never had their acb downloaded; the clip list still comes from the awb itself, so those
clips keep their index, and the index is what the cue id almost always is. That case is why the Sound page
labels clips by their index first and uses the cue name when the acb has one - a missing acb loses the cue
*names*, not the text.

### One clip, several lines

A voice bank is smaller than the arc it serves, so the same cue is used by more than one line - a long take
running over several text boxes, and the same take reused between episodes. The viewer lists all of them:
the clip row says how many more there are (`3 (+13)`), the panel shows the first twelve lines, and the tag's
lyrics frame gets the whole list. A story's own sheet is one line per cue, so this mostly affects the `05…`
banks.

## What is covered

Measured over the whole sound index (16,209 awb assets, 92,054 clips) with `Tools/uma_transcript_survey.py`:

| family | clips | with text |
|---|---|---|
| `snd_voi_story` (story dialogue - not in the master data, see below) | 33,286 | from the timeline assets: exact where a sheet is a story's own or a home scene, one clip may answer for several lines on a character's voice bank |
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

The text is whatever the installed client has: a Japanese client gives Japanese lines. A clip of an asset
that is in the index but not downloaded shows no text (that would mean a network round trip for a label).

## Checking it without the UI

```powershell
# what does a voice asset resolve to? (sheet, asset name, or full asset path)
./Tools/headless_export.ps1 -Transcript 041047007
./Tools/headless_export.ps1 -Transcript snd_voi_story_00000_01_1032001
./Tools/headless_export.ps1 -Transcript snd_voi_home_100100

# export one clip to an mp3 and read the tag back out of it
./Tools/headless_export.ps1 -ExportAudio snd_voi_story_041047007 -ExportClip 3 -Out out.mp3
```

`-umaTranscript` prints the lines a sheet carries and then, clip by clip, what the viewer would label that
clip with. `-umaExportAudio` writes the mp3 and logs the ID3 frames it finds in it afterwards.

The same reading of an exported file, without Unity, is `Tools/mp3_transcript.py` - standard library only:

```powershell
python Tools/mp3_transcript.py D:/out/line.mp3
python Tools/mp3_transcript.py "D:/rips/*_transcribed.mp3" --json
```

It prints the title, speaker, story and full text, and exits non-zero if a file carries no transcript,
which is what makes it usable as a check over a folder of exports.

## The tag

`Assets/Scripts/Exporters/Id3Tag.cs` writes an ID3v2.3 tag in front of the mp3 once LAME has finished with
it (the encoder has to close the file first, and the tag has to be in front of the audio):

| frame | contents |
|---|---|
| `TIT2` | the line itself |
| `TPE1` | the speaker |
| `TALB` | the story title (`Album`), when the clip is story dialogue |
| `COMM` | the asset and clip it came from |
| `USLT` | every line of the clip, as `speaker: line` |

Text frames are UTF-16 with a byte order mark, which is what players need for Japanese; the lyrics and
comment frames carry the language code `jpn` and an empty descriptor. A clip with no text is exported
untagged, exactly as before.
