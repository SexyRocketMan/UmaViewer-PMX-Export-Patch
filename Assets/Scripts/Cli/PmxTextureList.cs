using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Reads the texture list out of a PMX file - the names of the maps a model is made of.
///
/// This is what a dump needs to answer two questions a web service asks constantly: which files does one model
/// need (for a per-model download) and does everything it references actually exist (for a validation pass). The
/// exporter knows the answer while it writes, but the dump runs afterwards and the answer has to be recoverable
/// from the file - especially once several models share one `Texture2D/` folder, where the folder no longer says
/// which maps belong to whom.
///
/// Only the header and the sections before the texture table are walked, so no geometry is allocated: a vertex is
/// skipped by arithmetic. PMX 2.0 only, which is what this project writes.
/// </summary>
public static class PmxTextureList
{
    /// <summary>The texture paths a PMX names, in file order, or an empty list if it has none.</summary>
    public static List<string> Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        return Read(reader);
    }

    private static List<string> Read(BinaryReader reader)
    {
        string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != "PMX ") throw new InvalidDataException($"not a PMX file (magic '{magic}')");

        float version = reader.ReadSingle();
        if (version < 1.99f || version > 2.01f) throw new InvalidDataException($"PMX version {version} not supported");

        int globalsCount = reader.ReadByte();
        byte[] globals = reader.ReadBytes(globalsCount);
        bool utf8 = globals[0] == 1;
        int extraUv = globals[1];
        int vertexIndexSize = globals[2];
        int boneIndexSize = globals[5];
        // materials, morphs and rigid bodies come after the textures, so their index sizes are not needed

        SkipText(reader, utf8);   // model name, japanese
        SkipText(reader, utf8);   // model name, english
        SkipText(reader, utf8);   // comment, japanese
        SkipText(reader, utf8);   // comment, english

        SkipVertices(reader, vertexIndexSize, boneIndexSize, extraUv);
        SkipFaces(reader, vertexIndexSize);

        int count = reader.ReadInt32();
        var textures = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            int length = reader.ReadInt32();
            byte[] bytes = reader.ReadBytes(length);
            textures.Add((utf8 ? Encoding.UTF8 : Encoding.Unicode).GetString(bytes));
        }
        return textures;
    }

    /// <summary>Reads a PMX string and drops it.</summary>
    private static void SkipText(BinaryReader reader, bool utf8)
    {
        int length = reader.ReadInt32();
        reader.BaseStream.Seek(length, SeekOrigin.Current);
    }

    /// <summary>
    /// Steps over the vertex section.
    ///
    /// A vertex is a position, a normal, a UV, any number of extra UVs, then a weight block whose size depends on
    /// its type: BDEF1 is one bone, BDEF2 two and a float, BDEF4/QDEF four and four floats, and SDEF two bones, a
    /// float and three C sets - which is why this cannot be a fixed stride.
    /// </summary>
    private static void SkipVertices(BinaryReader reader, int vertexIndexSize, int boneIndexSize, int extraUv)
    {
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            // position, normal, uv, then any extra UV sets: these are all fixed size, so step over them first
            reader.BaseStream.Seek(12 + 12 + 8 + extraUv * 16, SeekOrigin.Current);

            byte weightType = reader.ReadByte();
            long rest;
            switch (weightType)
            {
                case 0: rest = boneIndexSize; break;                 // BDEF1
                case 1: rest = boneIndexSize * 2 + 4; break;         // BDEF2
                case 2: rest = boneIndexSize * 4 + 16; break;        // BDEF4
                case 3: rest = boneIndexSize * 2 + 4 + 36; break;    // SDEF
                case 4: rest = boneIndexSize * 4 + 16; break;        // QDEF
                default:
                    throw new InvalidDataException(
                        $"unknown vertex weight type {weightType} at vertex {i + 1} of {count}, "
                        + $"offset {reader.BaseStream.Position} (vertex index size {vertexIndexSize}, "
                        + $"bone index size {boneIndexSize}, extra UV {extraUv})");
            }
            reader.BaseStream.Seek(rest + 4, SeekOrigin.Current);    // weights, then the edge scale
        }
    }

    /// <summary>Steps over the face section: a count, then that many indices of the model's vertex index size.</summary>
    private static void SkipFaces(BinaryReader reader, int vertexIndexSize)
    {
        int count = reader.ReadInt32();
        reader.BaseStream.Seek((long)count * vertexIndexSize, SeekOrigin.Current);
    }

    /// <summary>
    /// The texture paths as file names, which is how a PMX references them on disk: the exporter writes
    /// `Texture2D/tex_x_diff.png` and the loader resolves that next to the model.
    /// </summary>
    public static List<string> FileNames(string path)
    {
        var names = new List<string>();
        foreach (string texture in Read(path))
        {
            string name = texture.Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            names.Add(slash >= 0 ? name.Substring(slash + 1) : name);
        }
        return names;
    }
}
