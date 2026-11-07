using CriWareFormats;
using Gallop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>One line of dialogue that a clip of audio belongs to.</summary>
public struct TranscriptLine
{
    /// <summary>Who says it. Empty for narration and for voices the game plays without a speaker.</summary>
    public string Speaker;
    /// <summary>The line as the game displays it, with its own line breaks.</summary>
    public string Text;
    /// <summary>The story the line comes from, e.g. "041047007". Empty when the line came from the master data.</summary>
    public string StoryId;
    /// <summary>The story's title, when it has one.</summary>
    public string StoryTitle;

    public bool IsEmpty => string.IsNullOrEmpty(Text);
}

/// <summary>
/// The text that belongs to a sound clip.
///
/// The game keeps this in two different places, and which one applies depends on what the clip is:
///
///  * story dialogue - the lines live in the story timeline assets (`story/data/XX/YYYY/storytimeline_*`),
///    where each line names the cue sheet and cue it is voiced with. This is what <see cref="ForSheet"/>
///    reads, through the game's own `StoryTimelineTextClipData` class (see Gallop/Story/StoryTimeline.cs).
///  * everything else that is a voice - the master data's `character_system_text` table holds one row per
///    recorded character voice line, keyed by the cue name the acb gives the clip.
///
/// A clip is identified the way the Sound page identifies it: the awb asset plus the index of the subsong,
/// which is the order <see cref="UmaViewerBuilder.LoadAudio"/> lists them in.
/// </summary>
public static class AudioTranscripts
{
    // ------------------------------------------------------------------ public API

    /// <summary>The lines belonging to one clip of one sound asset. Never null; may be empty.</summary>
    public static List<TranscriptLine> ForClip(UmaDatabaseEntry awb, int clipIndex)
    {
        var lines = new List<TranscriptLine>();
        if (awb == null || clipIndex < 0) return lines;

        string sheet = StorySheetOf(awb);
        if (sheet != null)
        {
            // the story naming is exact: the timeline's lines carry the cue sheet and the cue, so the
            // clip index only has to be turned into a cue id
            var byCue = ForSheet(sheet);
            SoundCues.Cue cue = SoundCues.Of(awb, clipIndex);
            int cueId = cue.HasName && cue.Id >= 0 ? cue.Id : clipIndex;
            if (byCue.TryGetValue(cueId, out var storyLines)) lines.AddRange(storyLines);
            return lines;
        }

        // any other voice: the master data is keyed by the cue name the acb carries. Music and effects have
        // no text, so the table (33k rows) is only worth reading for an asset that is a voice at all.
        if (!IsVoiceAsset(awb)) return lines;
        SoundCues.Cue named = SoundCues.Of(awb, clipIndex);
        if (!named.HasName) return lines;
        if (UmaDatabaseController.TryGetSystemVoiceText(named.Name, out string speaker, out string text))
        {
            lines.Add(new TranscriptLine { Speaker = speaker, Text = text });
        }
        return lines;
    }

    /// <summary>Every clip of an asset that has text, keyed by clip index.</summary>
    public static Dictionary<int, List<TranscriptLine>> ForAsset(UmaDatabaseEntry awb)
    {
        var result = new Dictionary<int, List<TranscriptLine>>();
        if (awb == null) return result;

        string sheet = StorySheetOf(awb);
        Dictionary<int, List<TranscriptLine>> byCue = sheet != null ? ForSheet(sheet) : null;
        bool isVoice = byCue != null || IsVoiceAsset(awb);
        if (!isVoice) return result;

        List<SoundCues.Cue> cues = SoundCues.Of(awb);
        if (cues == null) return result;

        for (int index = 0; index < cues.Count; index++)
        {
            if (byCue != null)
            {
                int cueId = cues[index].HasName ? cues[index].Id : index;
                if (byCue.TryGetValue(cueId, out var storyLines))
                {
                    result[index] = new List<TranscriptLine>(storyLines);
                }
                continue;
            }

            if (!cues[index].HasName) continue;
            if (!UmaDatabaseController.TryGetSystemVoiceText(cues[index].Name, out string speaker, out string text)) continue;
            result[index] = new List<TranscriptLine> { new TranscriptLine { Speaker = speaker, Text = text } };
        }
        return result;
    }

    /// <summary>
    /// The lines that a story voice sheet is used for, keyed by cue id. Cached: resolving a sheet can mean
    /// loading a couple of hundred timelines the first time it is asked for.
    /// </summary>
    public static Dictionary<int, List<TranscriptLine>> ForSheet(string sheet)
    {
        if (string.IsNullOrEmpty(sheet)) return new Dictionary<int, List<TranscriptLine>>();
        if (sheetCache.TryGetValue(sheet, out var cached)) return cached;

        var byCue = new Dictionary<int, List<TranscriptLine>>();
        sheetCache[sheet] = byCue;

        foreach (UmaDatabaseEntry entry in CandidateTimelines(sheet))
        {
            List<StoryTimelineTextClipData> clips = LoadClips(entry);
            if (clips == null || clips.Count == 0) continue;

            string storyId = TimelineIdOf(entry);
            // A home scene has no episode title, and its StoryTimelineData's Title field holds a placeholder.
            // The title is only worth reading once a line of this timeline turns out to be wanted anyway.
            bool isHome = IsHomeTimeline(entry);
            string storyTitle = isHome ? "" : null;

            foreach (StoryTimelineTextClipData clip in clips)
            {
                if (clip == null || !clip.HasVoice) continue;
                if (!string.Equals(clip.VoiceSheetId, sheet, StringComparison.Ordinal)) continue;
                if (storyTitle == null) storyTitle = LoadStoryTitle(entry) ?? "";

                if (!byCue.TryGetValue(clip.CueId, out var list))
                {
                    list = new List<TranscriptLine>();
                    byCue[clip.CueId] = list;
                }

                var line = new TranscriptLine
                {
                    Speaker = clip.Name,
                    Text = Normalize(clip.Text),
                    StoryId = storyId,
                    StoryTitle = storyTitle,
                };
                // a long voice can be spread over several message boxes, so the same cue shows up more
                // than once; identical text is the same box seen twice (from the resource list copy)
                if (!list.Exists(l => l.Text == line.Text && l.Speaker == line.Speaker)) list.Add(line);
            }
        }

        return byCue;
    }

    /// <summary>
    /// The transcript to carry on an exported clip, or null when the clip has no text: the line for the
    /// title and the speaker, the story it belongs to for the album, and every line of it (for the lyrics
    /// frame) - all of them when the whole asset is being mixed rather than one clip.
    /// </summary>
    public static AudioTranscriptTag? Tag(UmaDatabaseEntry awb, int clipIndex)
    {
        if (awb == null) return null;

        List<TranscriptLine> lines = clipIndex >= 0
            ? ForClip(awb, clipIndex)
            : ForAsset(awb).OrderBy(pair => pair.Key).SelectMany(pair => pair.Value).ToList();

        string lyrics = Format(lines);
        if (string.IsNullOrEmpty(lyrics)) return null;

        TranscriptLine first = lines.FirstOrDefault(line => !line.IsEmpty);
        string story = lines.Select(line => line.StoryTitle).FirstOrDefault(title => !string.IsNullOrEmpty(title));
        string storyId = lines.Select(line => line.StoryId).FirstOrDefault(id => !string.IsNullOrEmpty(id));
        string asset = Path.GetFileNameWithoutExtension(awb.Name);

        return new AudioTranscriptTag
        {
            // the title is the line itself, with its line breaks flattened: the speaker has its own frame
            Title = first.IsEmpty ? "" : first.Text.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Trim(),
            Speaker = first.Speaker ?? "",
            Album = !string.IsNullOrEmpty(story) ? story : (!string.IsNullOrEmpty(storyId) ? $"story {storyId}" : ""),
            Comment = clipIndex >= 0 ? $"{asset} clip {clipIndex}" : asset,
            Lyrics = lyrics,
        };
    }

    /// <summary>The lines as one block of text, for a tag or a label. Empty when there are none.</summary>
    public static string Format(IEnumerable<TranscriptLine> lines)
    {
        if (lines == null) return "";
        var parts = new List<string>();
        foreach (TranscriptLine line in lines)
        {
            if (line.IsEmpty) continue;
            string text = line.Text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
            if (string.IsNullOrEmpty(line.Speaker)) parts.Add(text);
            else parts.Add($"{line.Speaker}: {text}");
        }
        return string.Join("\n", parts);
    }

    /// <summary>A one-line version, for a list row or a title.</summary>
    public static string Summarize(IEnumerable<TranscriptLine> lines)
    {
        if (lines == null) return "";
        foreach (TranscriptLine line in lines)
        {
            if (line.IsEmpty) continue;
            string text = line.Text.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Trim();
            return string.IsNullOrEmpty(line.Speaker) ? text : $"{line.Speaker}: {text}";
        }
        return "";
    }

    /// <summary>Forgets every resolved sheet. Called when the game data folder changes.</summary>
    public static void ClearCache()
    {
        sheetCache.Clear();
        SoundCues.ClearCache();
        timelineById = null;
        timelineIndex = null;
    }

    // ------------------------------------------------------------------ story assets

    private static readonly Dictionary<string, Dictionary<int, List<TranscriptLine>>> sheetCache
        = new Dictionary<string, Dictionary<int, List<TranscriptLine>>>();

    private static Dictionary<string, List<UmaDatabaseEntry>> timelineById;
    private static List<UmaDatabaseEntry> timelineIndex;

    /// <summary>
    /// The asset-name markers of the two families of serialized script the game uses for dialogue. A story
    /// episode is one asset per episode; a home scene (`hometimeline_`) is one asset per conversation.
    /// </summary>
    private static readonly string[] TimelineMarkers = { "/storytimeline_", "/hometimeline_" };

    /// <summary>
    /// Whether the asset holds recorded voices at all. The game names every voice `snd_voi_*`; music, sound
    /// effects and the live vocal tracks (`snd_bgm_live_*`) are not spoken lines, even though the live
    /// songs do have lyrics, which the viewer reads from their own asset.
    /// </summary>
    public static bool IsVoiceAssetName(string assetName)
    {
        string file = Path.GetFileNameWithoutExtension(assetName);
        return !string.IsNullOrEmpty(file) && file.StartsWith("snd_voi_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVoiceAsset(UmaDatabaseEntry awb) => awb != null && IsVoiceAssetName(awb.Name);

    /// <summary>
    /// The cue sheet a voice asset belongs to, or null when the asset is not a line of scripted dialogue.
    /// Story voices are the ones named `snd_voi_story_&lt;sheet&gt;`; the sheet is what the timelines key on.
    /// </summary>
    private static string StorySheetOf(UmaDatabaseEntry awb)
    {
        const string prefix = "snd_voi_story_";
        string file = Path.GetFileNameWithoutExtension(awb.Name);
        if (string.IsNullOrEmpty(file) || !file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        string sheet = file.Substring(prefix.Length);
        return sheet.Length == 0 ? null : sheet;
    }

    /// <summary>
    /// Whether reading this asset's text means scanning a whole story folder - the slow case, worth telling
    /// the user about before it starts. False for the two exact cases and for anything that is not dialogue.
    /// </summary>
    public static bool NeedsFolderScan(UmaDatabaseEntry awb)
    {
        string sheet = awb == null ? null : StorySheetOf(awb);
        if (string.IsNullOrEmpty(sheet)) return false;
        EnsureTimelineIndex();
        return !timelineById.ContainsKey(sheet) && sheet.Length >= 6;
    }

    /// <summary>
    /// The timelines that can hold lines for a sheet. Three cases, and which one applies is visible in the
    /// sheet's shape:
    ///
    ///  * a nine digit sheet that is a story's own - `snd_voi_story_041047007` is voiced by
    ///    `story/data/04/1047/storytimeline_041047007`. The sheet id *is* the timeline id, one asset.
    ///  * a nine digit sheet that is a character's voice bank - `snd_voi_story_051015001` is
    ///    `&lt;05&gt;&lt;chara 1015&gt;&lt;001&gt;`, and its lines are spread over every episode of that arc, in the
    ///    folders named after the middle digits (`story/data/11/1015/`, `story/data/50/1015/`, ...). All of a
    ///    folder's timelines have to be read.
    ///  * a `&lt;5 digits&gt;_&lt;2 digits&gt;_&lt;7 digits&gt;` sheet that is a **home** scene - those are
    ///    `home/data/00000/01/hometimeline_00000_01_1032001`, again one asset with the same id.
    /// </summary>
    private static IEnumerable<UmaDatabaseEntry> CandidateTimelines(string sheet)
    {
        EnsureTimelineIndex();

        if (timelineById.TryGetValue(sheet, out var exact)) return exact;
        if (sheet.Length < 6) return Array.Empty<UmaDatabaseEntry>();

        string folder = "/" + sheet.Substring(2, 4) + "/storytimeline_";
        return timelineIndex.Where(e => e.Name.IndexOf(folder, StringComparison.Ordinal) > 0);
    }

    private static void EnsureTimelineIndex()
    {
        if (timelineById != null) return;
        timelineById = new Dictionary<string, List<UmaDatabaseEntry>>(StringComparer.Ordinal);
        timelineIndex = new List<UmaDatabaseEntry>();

        var abList = UmaViewerMain.Instance?.AbList;
        if (abList == null) return;

        // both families of script, because a voice asset does not say which one it belongs to:
        // story episodes and home scenes are serialized the same way and read the same way
        foreach (KeyValuePair<string, UmaDatabaseEntry> pair in abList)
        {
            string id = null;
            bool isStory = false;
            foreach (string marker in TimelineMarkers)
            {
                int at = pair.Key.LastIndexOf(marker, StringComparison.Ordinal);
                if (at <= 0) continue;
                id = pair.Key.Substring(at + marker.Length);
                isStory = marker == TimelineMarkers[0];
                break;
            }
            if (string.IsNullOrEmpty(id) || id.IndexOf('/') >= 0) continue;   // "_resources" copies are not scripts

            // the folder scan below only walks story episodes: a home scene is found by its exact id
            if (isStory) timelineIndex.Add(pair.Value);

            if (!timelineById.TryGetValue(id, out var entries))
            {
                entries = new List<UmaDatabaseEntry>();
                timelineById[id] = entries;
            }
            entries.Add(pair.Value);
        }
    }

    /// <summary>The id a script asset is named by, i.e. what follows its `storytimeline_`/`hometimeline_` marker.</summary>
    private static string TimelineIdOf(UmaDatabaseEntry entry)
    {
        foreach (string marker in TimelineMarkers)
        {
            int at = entry.Name.LastIndexOf(marker, StringComparison.Ordinal);
            if (at > 0) return entry.Name.Substring(at + marker.Length);
        }
        return entry.Name;
    }

    private static bool IsHomeTimeline(UmaDatabaseEntry entry) =>
        entry.Name.IndexOf("/hometimeline_", StringComparison.Ordinal) > 0;

    private static List<StoryTimelineTextClipData> LoadClips(UmaDatabaseEntry entry)
    {
        var bundle = LoadBundle(entry);
        if (bundle == null) return null;

        // The lines are not assets of the bundle - they are objects the timeline points at - so they come
        // through the timeline's own block list rather than through LoadAllAssets.
        StoryTimelineData story = LoadStory(entry, bundle);
        if (story != null)
        {
            List<StoryTimelineTextClipData> clips = story.TextClips();
            if (clips.Count > 0) return clips;
        }

        // still ask the bundle directly, in case a timeline is built the other way round
        var direct = bundle.LoadAllAssets<StoryTimelineTextClipData>();
        if (direct != null && direct.Length > 0) return direct.ToList();
        return bundle.LoadAllAssets().OfType<StoryTimelineTextClipData>().ToList();
    }

    private static StoryTimelineData LoadStory(UmaDatabaseEntry entry, AssetBundle bundle = null)
    {
        bundle = bundle ?? LoadBundle(entry);
        if (bundle == null) return null;
        return bundle.LoadAllAssets<StoryTimelineData>().FirstOrDefault();
    }

    private static string LoadStoryTitle(UmaDatabaseEntry entry)
    {
        StoryTimelineData story = LoadStory(entry);
        return story != null ? story.Title : "";
    }

    private static AssetBundle LoadBundle(UmaDatabaseEntry entry)
    {
        // Path, not FilePath: FilePath downloads a missing asset, and asking for the text of a story the
        // player never downloaded should be a quiet miss, not a network round trip.
        try
        {
            if (!File.Exists(entry.Path)) return null;
            return UmaAssetManager.LoadAssetBundle(entry);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Transcript] {entry.Name}: {e.Message}");
            return null;
        }
    }

    private static string Normalize(string text) =>
        string.IsNullOrEmpty(text) ? "" : text.Replace("\\n", "\n");
}

/// <summary>
/// The cue a clip of an awb plays, read out of the acb that sits next to the awb.
///
/// The viewer plays and exports awbs directly and never needed the acb, but the cue names - the thing the
/// game's text is keyed by - only exist there: an acb maps a cue name to the awb wave id it plays
/// (`AcbNameLoader` walks cue -&gt; sequence/synth -&gt; waveform to do it). A clip's cue id is the number the
/// name ends with, so `snd_voi_story_051015001_91` is cue 91.
/// </summary>
public static class SoundCues
{
    public struct Cue
    {
        public string Name;
        public int Id;
        public bool HasName => !string.IsNullOrEmpty(Name);
    }

    private static readonly Dictionary<string, List<Cue>> cache = new Dictionary<string, List<Cue>>();

    public static void ClearCache() => cache.Clear();

    /// <summary>The cue of one clip, by the index <see cref="UmaViewerBuilder.LoadAudio"/> gives it.</summary>
    public static Cue Of(UmaDatabaseEntry awb, int clipIndex)
    {
        var cues = Of(awb);
        if (cues == null || clipIndex < 0 || clipIndex >= cues.Count) return default;
        return cues[clipIndex];
    }

    /// <summary>Every clip's cue, in the same order as the clips the Sound page lists. Null when unknown.</summary>
    public static List<Cue> Of(UmaDatabaseEntry awb)
    {
        if (awb == null) return null;
        if (cache.TryGetValue(awb.Name, out var cached)) return cached;

        List<Cue> cues = ReadCues(awb);
        cache[awb.Name] = cues;
        return cues;
    }

    private static List<Cue> ReadCues(UmaDatabaseEntry awb)
    {
        try
        {
            string awbPath = awb.Path;
            if (!File.Exists(awbPath)) return null;
            if (!awb.Name.EndsWith(".awb", StringComparison.OrdinalIgnoreCase)) return null;

            // The clip list comes from the awb itself, so it is known even when the acb is not on disk: an
            // asset whose acb was never downloaded still lists its clips, it just cannot name them. That
            // matters, because the cue *id* a transcript is keyed by is the clip's index in the common case.
            var waveIds = new List<int>();
            using (var awbStream = File.OpenRead(awbPath))
            using (var awbReader = new AwbReader(awbStream))
            {
                foreach (Wave wave in awbReader.Waves) waveIds.Add(wave.WaveId);
            }

            string acbName = awb.Name.Substring(0, awb.Name.Length - ".awb".Length) + ".acb";
            var abList = UmaViewerMain.Instance?.AbList;
            if (abList == null || !abList.TryGetValue(acbName, out var acbEntry) || !File.Exists(acbEntry.Path))
            {
                return waveIds.Select(_ => new Cue { Name = null, Id = -1 }).ToList();
            }

            var cues = new List<Cue>(waveIds.Count);
            using (var acbReader = new AcbReader(File.OpenRead(acbEntry.Path)))
            {
                foreach (int waveId in waveIds)
                {
                    string name = null;
                    try
                    {
                        // memory: false - these waveforms are streamed out of the awb, and the acb walks
                        // skip a streaming waveform when asked for the ones held in memory (which is how
                        // every uma voice acb records them). Asking for memory names returns nothing at all.
                        name = acbReader.GetWaveName(waveId, 0, false);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[SoundCues] {acbEntry.Name}: {e.Message}");
                    }
                    cues.Add(new Cue { Name = name, Id = CueIdOf(name) });
                }
            }
            return cues;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SoundCues] {awb.Name}: {e.Message}");
            return null;
        }
    }

    /// <summary>The number a cue name ends with, i.e. the cue id inside its sheet.</summary>
    private static int CueIdOf(string cueName)
    {
        if (string.IsNullOrEmpty(cueName)) return -1;
        int end = cueName.Length;
        int start = end;
        while (start > 0 && char.IsDigit(cueName[start - 1])) start--;
        if (start == end) return -1;
        return int.TryParse(cueName.Substring(start), out int id) ? id : -1;
    }
}
