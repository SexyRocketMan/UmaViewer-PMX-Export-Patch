using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Writes an ID3v2.3 tag in front of an mp3 file.
///
/// The mp3 the viewer writes comes straight out of LAME and has no tag, and the tag is how the transcript
/// travels with the file: players show a text/lyrics field, and the file keeps saying what was said even
/// after it leaves the folder it was exported into. ID3v2.3 is the version every player reads, and the
/// frames are written as UTF-16 with a byte order mark because the lines are Japanese.
///
/// Only what is needed is implemented - text frames (`T...`) and an unsynchronised lyrics frame (`USLT`) -
/// so there is no dependency on a tagging library.
/// </summary>
public static class Id3Tag
{
    private const byte EncodingUtf16WithBom = 1;   // ID3v2.3 §4: 0 latin-1, 1 UTF-16 with BOM
    private const string Language = "jpn";

    /// <summary>
    /// Replaces whatever tag is in front of <paramref name="path"/> with these frames. Frames are
    /// (frame id, text) pairs, e.g. ("TIT2", "えっ。"); a null or empty text drops the frame.
    /// </summary>
    public static void Write(string path, IEnumerable<KeyValuePair<string, string>> frames)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        byte[] tag = BuildTag(frames);
        if (tag == null || tag.Length == 0) return;

        long audioOffset = AudioOffset(path);   // past an existing tag, if the file already has one
        string temp = path + ".id3tmp";
        try
        {
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write))
            {
                output.Write(tag, 0, tag.Length);
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    input.Position = audioOffset;
                    input.CopyTo(output, 1 << 20);
                }
            }
            File.Replace(temp, path, null);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>
    /// The frames a clip's transcript goes into: the line as the title, the speaker as the artist, the story
    /// as the album, and the full text as the lyrics. Empty strings drop their frame, so a clip with no
    /// transcript is left untagged.
    /// </summary>
    public static void WriteTranscript(string path, string title, string speaker, string album, string comment, string lyrics)
    {
        Write(path, new[]
        {
            new KeyValuePair<string, string>("TIT2", title),
            new KeyValuePair<string, string>("TPE1", speaker),
            new KeyValuePair<string, string>("TALB", album),
            new KeyValuePair<string, string>("COMM", comment),
            new KeyValuePair<string, string>("USLT", lyrics),
        });
    }

    // ------------------------------------------------------------------ building the tag

    private static byte[] BuildTag(IEnumerable<KeyValuePair<string, string>> frames)
    {
        var body = new MemoryStream();
        foreach (KeyValuePair<string, string> frame in frames)
        {
            if (string.IsNullOrEmpty(frame.Value)) continue;
            byte[] payload = frame.Key == "USLT" || frame.Key == "COMM"
                ? BuildDescribedPayload(frame.Value)
                : BuildTextPayload(frame.Value);
            WriteFrame(body, frame.Key, payload);
        }
        if (body.Length == 0) return null;

        var tag = new MemoryStream();
        tag.Write(Encoding.ASCII.GetBytes("ID3"), 0, 3);
        tag.WriteByte(3);       // version 2.3
        tag.WriteByte(0);       // revision
        tag.WriteByte(0);       // flags: no unsynchronisation, no extended header
        WriteSyncSafe(tag, (int)body.Length);
        body.Position = 0;
        body.CopyTo(tag);
        return tag.ToArray();
    }

    private static void WriteFrame(Stream output, string id, byte[] payload)
    {
        output.Write(Encoding.ASCII.GetBytes(id), 0, 4);
        WriteBigEndian(output, payload.Length);
        output.WriteByte(0);    // flags
        output.WriteByte(0);
        output.Write(payload, 0, payload.Length);
    }

    private static byte[] BuildTextPayload(string text)
    {
        var payload = new MemoryStream();
        payload.WriteByte(EncodingUtf16WithBom);
        WriteUtf16(payload, text);
        return payload.ToArray();
    }

    /// <summary>
    /// A frame that carries a language and a description before its text - `USLT` (lyrics) and `COMM`
    /// (comment) are shaped this way. The description is left empty, which is what players expect for a
    /// single lyrics or comment frame.
    /// </summary>
    private static byte[] BuildDescribedPayload(string text)
    {
        var payload = new MemoryStream();
        payload.WriteByte(EncodingUtf16WithBom);
        payload.Write(Encoding.ASCII.GetBytes(Language), 0, 3);
        WriteUtf16(payload, "");        // empty description, itself a BOM-prefixed string
        payload.WriteByte(0);           // ... terminated
        payload.WriteByte(0);
        WriteUtf16(payload, text);
        return payload.ToArray();
    }

    /// <summary>
    /// Writes a UTF-16 string the way ID3v2.3 asks for it under encoding 1: a byte order mark, then the
    /// text. .NET's <see cref="Encoding.Unicode"/> is little endian and emits no mark of its own, and a
    /// reader handed the text without one is entitled to reject the frame - ffmpeg and mid3v2 both do,
    /// which is what made a tagged file look untagged.
    /// </summary>
    private static void WriteUtf16(Stream payload, string text)
    {
        payload.Write(new byte[] { 0xFF, 0xFE }, 0, 2);
        byte[] encoded = Encoding.Unicode.GetBytes(text);
        payload.Write(encoded, 0, encoded.Length);
    }

    private static void WriteBigEndian(Stream output, int value)
    {
        output.WriteByte((byte)((value >> 24) & 0xFF));
        output.WriteByte((byte)((value >> 16) & 0xFF));
        output.WriteByte((byte)((value >> 8) & 0xFF));
        output.WriteByte((byte)(value & 0xFF));
    }

    /// <summary>ID3 sizes are "sync safe": four bytes of seven bits each.</summary>
    private static void WriteSyncSafe(Stream output, int value)
    {
        output.WriteByte((byte)((value >> 21) & 0x7F));
        output.WriteByte((byte)((value >> 14) & 0x7F));
        output.WriteByte((byte)((value >> 7) & 0x7F));
        output.WriteByte((byte)(value & 0x7F));
    }

    private static int ReadSyncSafe(byte[] data, int offset)
    {
        return ((data[offset] & 0x7F) << 21)
               | ((data[offset + 1] & 0x7F) << 14)
               | ((data[offset + 2] & 0x7F) << 7)
               | (data[offset + 3] & 0x7F);
    }

    /// <summary>Where the audio starts in a file that may already carry a tag.</summary>
    private static long AudioOffset(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        var header = new byte[10];
        if (stream.Read(header, 0, header.Length) < header.Length) return 0;
        if (header[0] != 'I' || header[1] != 'D' || header[2] != '3') return 0;

        long size = 10 + ReadSyncSafe(header, 6);
        if ((header[5] & 0x10) != 0) size += 10;   // footer present
        return Math.Min(size, stream.Length);
    }
}
