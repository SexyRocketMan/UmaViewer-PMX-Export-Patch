using System.Collections;
using System.Collections.Generic;
using NAudio.Lame;
using UmaMusumeAudio;
using NAudio.Wave.SampleProviders;
using NAudio.Wave;

/// <summary>What gets written into an exported mp3's tag: the text that belongs to the clip.</summary>
public struct AudioTranscriptTag
{
    /// <summary>The line itself - what a player shows as the title.</summary>
    public string Title;
    /// <summary>Who says it.</summary>
    public string Speaker;
    /// <summary>Where it comes from: the story title, or the asset for a non-story voice.</summary>
    public string Album;
    /// <summary>The asset and clip it was exported from, so the file can be traced back.</summary>
    public string Comment;
    /// <summary>The full line(s), in a frame players show as lyrics.</summary>
    public string Lyrics;

    public bool IsEmpty => string.IsNullOrEmpty(Title) && string.IsNullOrEmpty(Speaker)
                           && string.IsNullOrEmpty(Album) && string.IsNullOrEmpty(Lyrics);
}

public class AudioExporter
{
    public static IEnumerator ExportAudio(List<UmaWaveStream> a, string path, int index = -1,
                                          AudioTranscriptTag? transcript = null) {

        var l = new List<ISampleProvider>();
        if (index == -1)
        {
            foreach (var z in a)
            {
                l.Add(GetSampleFixedLength(z));
            }
        } else
        {
            l.Add(GetSampleFixedLength(a[index]));
        }

        var wf = a[0].WaveFormat;

        var mixer = new MixingSampleProvider(l);
        IWaveProvider sourceProvider = new SampleToWaveProvider16(mixer);


        using (var writer = new LameMP3FileWriter(path, new NAudio.Wave.WZT.WaveFormat(wf.SampleRate, wf.Channels), 320))
        {
            byte[] buffer = new byte[wf.AverageBytesPerSecond]; // Read about 1 second of audio at a time
            int bytesRead;

            // Loop to read from the source IWaveProvider and write to the MP3 writer
            // The Read method returns 0 when there's no more data
            int safeguard = 0;
            while ((bytesRead = sourceProvider.Read(buffer, 0, buffer.Length)) > 0)
            {
                writer.Write(buffer, 0, bytesRead);
                if(++safeguard % 100 == 0)
                {
                    if(safeguard % 500 == 0)
                    {
                        UmaViewerUI.Instance.ShowMessage("Possible loop detected! Close UmaViewer if this message continues appearing", UIMessageType.Warning);
                    }
                    yield return 0;
                }
            }
        }

        // The tag goes on after the encoder closed the file: the writer has to finalise the stream first,
        // and writing in front of the audio is what makes the tag readable by every player.
        if (transcript.HasValue && !transcript.Value.IsEmpty)
        {
            try
            {
                AudioTranscriptTag tag = transcript.Value;
                Id3Tag.WriteTranscript(path, tag.Title, tag.Speaker, tag.Album, tag.Comment, tag.Lyrics);
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning($"[AudioExporter] could not tag {path}: {e.Message}");
                UmaViewerUI.Instance.ShowMessage("Exported, but the transcript tag could not be written", UIMessageType.Warning);
            }
        }

        UmaViewerUI.Instance.ShowMessage("Export complete", UIMessageType.Default);
    }

    private static ISampleProvider GetSampleFixedLength(UmaWaveStream sample)
    {
        var blah = sample.ToSampleProvider().Take(sample.TotalTime);
        return blah;
    }
}
