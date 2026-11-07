using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Reads what a dump needs to know about a PMX without loading it: the maps it names and how big it really is, so a
/// service can answer "which files does this model need" and "do they all exist" from the file long after the export.
/// Only the header, the vertices and the texture table are walked; PMX 2.0 only, which is what this project writes.
///
/// The size comes from the vertices, not from a renderer: `SkinnedMeshRenderer.bounds` is the box of the model's
/// *bones*, and one stray bone claimed character 1001 was 25.9 units deep while she stands 1.6.
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

    /// <summary>The maps a PMX names as file names, and the box its vertices occupy, in one pass over the file.</summary>
    public struct Facts
    {
        /// <summary>The texture paths' file names, which is how a PMX references them on disk.</summary>
        public List<string> Maps;
        /// <summary>Width, height and depth in model units, from the vertices: "x,y,z" ready for the index.</summary>
        public string Size;
    }

    /// <summary>Reads a PMX once and returns both facts; throws if the file is not a PMX this reads.</summary>
    public static Facts Scan(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        var maps = Read(reader, out double[] size);
        return new Facts
        {
            Maps = FileNames(maps),
            Size = size.Length == 3
                ? FormattableString.Invariant($"{size[0]:F3},{size[1]:F3},{size[2]:F3}")
                : "",
        };
    }

    private static List<string> Read(BinaryReader reader)
    {
        return Read(reader, out _);
    }

    private static List<string> Read(BinaryReader reader, out double[] size)
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

        ReadVertices(reader, vertexIndexSize, boneIndexSize, extraUv, out size);
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
    /// Reads the vertex positions and steps over everything else in the section. A vertex is a position, a normal, a
    /// UV, any number of extra UVs, then a weight block whose size depends on its type (BDEF1 one bone, BDEF2 two and
    /// a float, BDEF4/QDEF four and four floats, SDEF two bones, a float and three C sets), so this cannot be a fixed
    /// stride: the position is the first 12 bytes and the rest is stepped over.
    /// </summary>
    private static void ReadVertices(BinaryReader reader, int vertexIndexSize, int boneIndexSize, int extraUv,
                                     out double[] size)
    {
        int count = reader.ReadInt32();
        var min = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
        var max = new[] { double.MinValue, double.MinValue, double.MinValue };
        for (int i = 0; i < count; i++)
        {
            double x = reader.ReadSingle();
            double y = reader.ReadSingle();
            double z = reader.ReadSingle();
            if (x < min[0]) min[0] = x;
            if (y < min[1]) min[1] = y;
            if (z < min[2]) min[2] = z;
            if (x > max[0]) max[0] = x;
            if (y > max[1]) max[1] = y;
            if (z > max[2]) max[2] = z;

            // normal, uv, then any extra UV sets: all fixed size, so step over them before the weight block
            reader.BaseStream.Seek(12 + 8 + extraUv * 16, SeekOrigin.Current);

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

        size = count > 0
            ? new[] { max[0] - min[0], max[1] - min[1], max[2] - min[2] }
            : new double[0];
    }

    /// <summary>Steps over the face section: a count, then that many indices of the model's vertex index size.</summary>
    private static void SkipFaces(BinaryReader reader, int vertexIndexSize)
    {
        int count = reader.ReadInt32();
        reader.BaseStream.Seek((long)count * vertexIndexSize, SeekOrigin.Current);
    }

    /// <summary>The texture paths as file names, which is how a PMX references them on disk: the exporter writes
    /// `Texture2D/tex_x_diff.png` and the loader resolves that next to the model.</summary>
    public static List<string> FileNames(string path)
    {
        return FileNames(Read(path));
    }

    private static List<string> FileNames(List<string> textures)
    {
        var names = new List<string>();
        foreach (string texture in textures)
        {
            string name = texture.Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            names.Add(slash >= 0 ? name.Substring(slash + 1) : name);
        }
        return names;
    }
}
