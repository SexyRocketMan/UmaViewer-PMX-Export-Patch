using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop
{
    /// <summary>
    /// A story timeline asset - one episode script, and the root of everything the episode uses.
    ///
    /// Unity binds a serialized MonoBehaviour to a script by assembly, namespace and class name, and
    /// ignores serialized fields the class does not declare, so a partial class reads the same asset. The
    /// identity matters: the game ships these with assembly "umamusume" and namespace "Gallop", which is
    /// exactly what Assets/Scripts/umamusume.asmdef compiles to - the same mechanism the rest of
    /// Assets/Scripts/umamusume (AssetHolder, CySpringDataContainer, ...) already relies on.
    ///
    /// `BlockList` is what makes the dialogue reachable at all. Every dialogue line is its own
    /// MonoBehaviour, and one of those is not an asset of the bundle: Unity's AssetBundle API will not list
    /// it, and it is only loaded because the timeline holds a reference to it - `BlockList[i].TextTrack`
    /// -> `ClipList[j]`. Declaring those two fields is what turns the bundle's objects into the lines.
    /// </summary>
    public class StoryTimelineData : MonoBehaviour
    {
        /// <summary>The story id, as a string - it is also the "storytimeline_" asset name's tail.</summary>
        public string StoryId;
        /// <summary>The episode title the game shows in its story list, e.g. "たづなさんの育成アドバイス".</summary>
        public string Title;
        public int Length;

        public List<StoryTimelineBlockData> BlockList = new List<StoryTimelineBlockData>();

        /// <summary>Every line of the episode, in the order the story shows them.</summary>
        public List<StoryTimelineTextClipData> TextClips()
        {
            var clips = new List<StoryTimelineTextClipData>();
            if (BlockList == null) return clips;

            foreach (StoryTimelineBlockData block in BlockList)
            {
                StoryTimelineTextTrackData track = block?.TextTrack;
                if (track?.ClipList == null) continue;
                foreach (StoryTimelineTextClipData clip in track.ClipList)
                {
                    if (clip) clips.Add(clip);
                }
            }
            clips.Sort((a, b) => a.StartFrame.CompareTo(b.StartFrame));
            return clips;
        }
    }

    /// <summary>One block of a story timeline: the span one character speaks (or the scene is still) for.</summary>
    [Serializable]
    public class StoryTimelineBlockData
    {
        public int BlockIndex;
        public int BlockLength;
        public byte IsForceAuto;
        public byte IsEnableTapOnForceAuto;

        /// <summary>The block's dialogue track - the lines, and through them the text of the episode.</summary>
        public StoryTimelineTextTrackData TextTrack;
    }

    /// <summary>A track of dialogue lines inside one block.</summary>
    [Serializable]
    public class StoryTimelineTextTrackData
    {
        public int GroupIndex;
        public byte Enable;

        /// <summary>
        /// The lines. This type must stay <see cref="StoryTimelineTextClipData"/>: Unity resolves the
        /// timeline's references into these objects while it deserializes the asset, which is the only way
        /// the lines get loaded.
        /// </summary>
        public List<StoryTimelineTextClipData> ClipList = new List<StoryTimelineTextClipData>();
    }

    /// <summary>
    /// One line of dialogue: who says it, what is displayed, and which recorded voice the game plays with it.
    /// The voice is named the way the sound assets are: `VoiceSheetId` is the cue sheet (the awb's base name
    /// minus the `snd_voi_story_` prefix) and `CueId` picks the cue inside it, which is the clip index the
    /// viewer's sound page lists.
    ///
    /// Field types are the serialized ones (ints are int32, and the flags are single bytes), because Unity
    /// refuses a field whose type does not match the asset.
    /// </summary>
    public class StoryTimelineTextClipData : MonoBehaviour
    {
        public int DataVer;
        public int Lerp;
        public AnimationCurve AnimationCurve;
        public int EasingType;
        public byte IsEnableClip;
        public int StartFrame;
        public int ClipLength;
        public byte IsForceUpdate;
        public byte _isUpdateOverBlock;

        /// <summary>Who speaks: the character id, or one of the game's "not a character" ids.</summary>
        public int CharaId;
        public string SubCharaId;
        public int SubIconId;

        /// <summary>The cue sheet the voice is played from, e.g. "051015001" (see the class summary).</summary>
        public string VoiceSheetId;
        /// <summary>The cue inside that sheet - the viewer's clip index for that awb.</summary>
        public int CueId;
        public byte UseVoiceSelector;

        /// <summary>The speaker name as displayed, e.g. "ゼンノロブロイ". Empty for narration.</summary>
        public string Name;
        /// <summary>The line as displayed. Line breaks are "\r\n" or "\n" depending on the story.</summary>
        public string Text;

        public int Size;
        public byte IsCustomFontStyle;
        public int OutlineSizeType;
        public int GradientColorType;
        public int OutlineColorType;
        public int NextBlock;
        public int DifferenceType;
        public int DifferenceFlag;
        public byte IsTypewriteAnimation;
        public int WaitFrame;
        /// <summary>How long the voice runs, in frames; -1 when the line has no voice.</summary>
        public int VoiceLength;
        public float Pan3dAngle;
        public int Type;
        public int IconMode;
        public int GainTypeOnExitLoop;

        /// <summary>Whether the game plays a recorded voice for this line at all.</summary>
        public bool HasVoice => !string.IsNullOrEmpty(VoiceSheetId) && CueId >= 0;
    }
}
