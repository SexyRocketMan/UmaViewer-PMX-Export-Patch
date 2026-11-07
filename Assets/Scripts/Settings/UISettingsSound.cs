#if !UNITY_ANDROID || UNITY_EDITOR
using SFB;
#endif
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UmaMusumeAudio;
using UnityEngine;
using UnityEngine.UI;

public class UISettingsSound : MonoBehaviour
{
    static UmaViewerBuilder Builder => UmaViewerBuilder.Instance;

    public TextMeshProUGUI TitleText;
    public TextMeshProUGUI ProgressText;
    public Slider ProgressSlider;
    public Button PlayButton;
    public Text LyricsText;

    // How many lines of a transcript the panel shows at once. A story voice sheet can carry a whole
    // episode's worth of lines, and the panel sits over the viewport.
    private const int TranscriptLinesShown = 12;

    private string transcriptKey;
    private string transcriptText;

    internal void UpdateTrack(AudioSource mianSource)
    {
        TitleText.text = mianSource.clip.name;
        ProgressText.text = string.Format("{0} / {1}", ToTimeFormat(mianSource.time), ToTimeFormat(mianSource.clip.length));
        ProgressSlider.SetValueWithoutNotify(mianSource.time / mianSource.clip.length);

        // A story line or a recorded character voice says what it says; only the live songs have lyrics.
        string transcript = CurrentTranscript();
        LyricsText.text = transcript.Length > 0
            ? transcript
            : UmaUtility.GetCurrentLyrics(mianSource.time, Builder.CurrentLyrics);
    }

    /// <summary>
    /// The text of what is playing, ready for the panel: the story line the clip voices, or the master
    /// data's line for a recorded character voice. Empty for music and sound effects, which have no text.
    /// </summary>
    public string CurrentTranscript()
    {
        UmaDatabaseEntry entry = CurrentSound;
        if (entry == null) return "";

        int index = Builder.CurrentLiveSoundAWBIndex;
        string key = $"{entry.Name}#{index}";
        if (key == transcriptKey) return transcriptText;

        transcriptKey = key;
        string text = index >= 0
            ? AudioTranscripts.Format(AudioTranscripts.ForClip(entry, index))
            : AudioTranscripts.Format(AudioTranscripts.ForAsset(entry).OrderBy(pair => pair.Key)
                                                       .SelectMany(pair => pair.Value));
        transcriptText = Limit(text, TranscriptLinesShown);
        return transcriptText;
    }

    /// <summary>The sound asset the page is playing, or null.</summary>
    private static UmaDatabaseEntry CurrentSound =>
        Builder.CurrentLiveSoundAWB.Count > 0 ? Builder.CurrentLiveSoundAWB[0] : null;

    private static string Limit(string text, int lines)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string[] split = text.Split('\n');
        if (split.Length <= lines) return text;
        return string.Join("\n", split.Take(lines)) + "\n…";
    }

    public void PauseAudio()
    {
        var sources = Builder.CurrentAudioSources;
        if (sources.Count > 0)
        {
            AudioSource MainSource = sources[0];
            var state = MainSource.isPlaying;
            foreach (AudioSource source in sources)
            {
                if (state)
                {
                    source.Pause();
                }
                else if (source.clip)
                {
                    source.Play();
                }
                else
                {
                    source.Stop();
                }
            }
        }
    }

    public void StopAudio()
    {
        var sources = Builder.CurrentAudioSources;
        foreach (AudioSource source in sources)
        {
            source.Stop();
        }
    }

    public void ResetPlayer()
    {
        TitleText.text = "No Audio";
        ProgressText.text = "00:00:00 / 00:00:00";
        ProgressSlider.SetValueWithoutNotify(0);
        LyricsText.text = "";
        transcriptKey = null;
        transcriptText = null;
    }

    public void ExportAudio()
    {
#if !UNITY_ANDROID || UNITY_EDITOR
        var LiveSources = Builder.CurrentLiveSoundAWB;
        if (LiveSources.Count > 0)
        {
            var lists = new List<UmaWaveStream>();
            foreach (var source in LiveSources) {
                var z = Builder.LoadAudioStreams(source);
                foreach (var item in z)
                {
                    lists.Add(item);
                }
            }

            var path = StandaloneFileBrowser.SaveFilePanel("Save Music MP3 File", Config.Instance.MainPath, $"{TitleText.text}", "mp3");
            if (!string.IsNullOrEmpty(path))
            {
                StartCoroutine(AudioExporter.ExportAudio(lists, path, Builder.CurrentLiveSoundAWBIndex, BuildTag()));
            }

        }
#else
        UmaViewerUI.Instance.ShowMessage("Not supported on this platform", UIMessageType.Warning);
#endif
    }

    /// <summary>
    /// The transcript to carry into the exported mp3, or null when the clip has no text.
    ///
    /// The clip the page is on decides what goes in: its own line for the title and the speaker, the story
    /// it belongs to for the album, and the whole text (all lines, when the whole asset is being mixed)
    /// for the lyrics frame.
    /// </summary>
    private AudioTranscriptTag? BuildTag() => AudioTranscripts.Tag(CurrentSound, Builder.CurrentLiveSoundAWBIndex);

    public void AudioProgressChange(float val)
    {
        if (Builder.CurrentAudioSources.Count > 0)
        {
            var time = Builder.CurrentAudioSources[0].clip.length * val;
            foreach (AudioSource source in Builder.CurrentAudioSources)
            {
                if (source.clip)
                {
                    source.time = Mathf.Clamp(time, 0, source.clip.length);
                }
            }
        }
    }

    public static string ToTimeFormat(float time)
    {
        int seconds = (int)time;
        int hour = seconds / 3600;
        int minute = seconds % 3600 / 60;
        seconds = seconds % 3600 % 60;
        return string.Format("{0:D2}:{1:D2}:{2:D2}", hour, minute, seconds);
    }
}
